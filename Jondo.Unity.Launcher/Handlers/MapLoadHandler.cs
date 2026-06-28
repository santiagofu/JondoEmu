using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Google.Protobuf;
using Jondo.Unity.Launcher.Network;

namespace Jondo.Unity.Launcher.Handlers
{
    public static class MapLoadHandler
    {
        public static async Task HandleMapLoadRequest(NetworkStream stream, byte[] payload)
        {
            LogDebug("[Game Node] Received Map Complementary Info Request (kkr) [Initial Map Load]");
            byte[]? inner = NetworkEnvelope.ExtractMessagePayload(payload, "type.ankama.com/kkr");
            if (inner == null)
            {
                inner = NetworkEnvelope.ExtractMessagePayload(payload, "type.ankama.com/joi");
            }
            if (inner != null)
            {
                long requestedMapId = 0;
                try
                {
                    int pos = 0;
                    while (pos < inner.Length)
                    {
                        uint tag = NetworkEnvelope.ReadVarInt(inner, ref pos);
                        int wireType = (int)(tag & 7);
                        int fieldNum = (int)(tag >> 3);
                        if (fieldNum == 1 && wireType == 0)
                        {
                            requestedMapId = (long)NetworkEnvelope.ReadVarInt64(inner, ref pos);
                        }
                        else
                        {
                            NetworkEnvelope.SkipField(inner, wireType, ref pos);
                        }
                    }
                }
                catch { }

                long mapIdToLoad = requestedMapId > 0 ? requestedMapId : GameState.MapId;
                if (mapIdToLoad > 0)
                {
                    LogDebug($"[Game Node] Client requested map complementary info for Map ID: {mapIdToLoad} (extracted: {requestedMapId})");
                    GameState.MapId = mapIdToLoad;

                    int spawnCellId = GameState.CellId > 0 ? GameState.CellId : 344;
                    var mapInfo = MapManager.GetMapInfo(mapIdToLoad);
                    int subAreaId = mapInfo != null ? mapInfo.SubAreaId : 1;
                    if (subAreaId == 444)
                    {
                        subAreaId = 20663;
                    }
 
                    // 1. Send lxd (MapComplementaryInfo wrapper)
                    string lxdPath = NetworkEnvelope.ResolvePacketPath($"lxd_{mapIdToLoad}.bin");
                    if (!File.Exists(lxdPath))
                    {
                        lxdPath = NetworkEnvelope.ResolvePacketPath("lxd_packet.bin");
                    }

                    if (File.Exists(lxdPath))
                    {
                        byte[] lxdFileBytes = File.ReadAllBytes(lxdPath);
                        // If the file starts directly with the NetworkEnvelope Tag 3 (0x1A), it is already wrapped and lacks a length prefix.
                        byte[]? wrappedLxd = lxdFileBytes;
                        if (lxdFileBytes.Length > 0 && lxdFileBytes[0] != 0x1A)
                        {
                            wrappedLxd = NetworkEnvelope.UnpackLengthPrefixed(lxdFileBytes);
                        }

                        if (wrappedLxd != null)
                        {
                            byte[]? lxdSerialized = NetworkEnvelope.ExtractGameNodePayload(wrappedLxd);
                            if (lxdSerialized != null)
                            {
                                var lxdMsg = ProtoMessage.Parse(lxdSerialized);

                                // Remove all prism/alliance subarea entries from lxd in tutorial zones (like Incarnam) to prevent client crash
                                if (subAreaId == 20663)
                                {
                                    lxdMsg.Fields.RemoveAll(f => f.FieldNumber == 1 || f.FieldNumber == 3);
                                }

                                byte[] patchedLxdBytes = lxdMsg.ToByteArray();
                                byte[] lxdPacket = NetworkEnvelope.BuildGameNodePacket("type.ankama.com/lxd", patchedLxdBytes);
                                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, lxdPacket);
                                LogDebug($"[Game Node] Sent patched lxd for Map ID: {mapIdToLoad} from file {lxdPath}");
                            }
                        }
                    }
                    else
                    {
                        var emptyLxd = new Jondo.Unity.Protocol.Messages.lxd();
                        byte[] lxdPacket = NetworkEnvelope.BuildGameNodePacket("type.ankama.com/lxd", emptyLxd.ToByteArray());
                        await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, lxdPacket);
                        LogDebug($"[Game Node] Sent native empty lxd for Map ID: {mapIdToLoad}");
                    }
 
                    // 2. Send jpv (MapComplementaryInformationsDataMessage)
                    try
                    {
                        byte[]? jpvPayload = null;
                        string jpvPath = NetworkEnvelope.ResolvePacketPath("jpv_packet.bin");
                        if (File.Exists(jpvPath))
                        {
                            byte[] jpvFileBytes = File.ReadAllBytes(jpvPath);
                            int jpvPos = 0;
                            int jpvLength = 0;
                            int jpvShift = 0;
                            while (jpvPos < jpvFileBytes.Length)
                            {
                                int b = jpvFileBytes[jpvPos++];
                                jpvLength |= (b & 0x7F) << jpvShift;
                                if ((b & 0x80) == 0) break;
                                jpvShift += 7;
                            }
                            if (jpvLength > 0 && jpvPos + jpvLength <= jpvFileBytes.Length)
                            {
                                byte[] wrappedJpv = new byte[jpvLength];
                                Array.Copy(jpvFileBytes, jpvPos, wrappedJpv, 0, jpvLength);
                                jpvPayload = NetworkEnvelope.ExtractGameNodePayload(wrappedJpv);
                            }
                        }

                        if (jpvPayload != null)
                        {
                            // Parse and patch the recorded jpv template schema-freely
                            var jpvMsg = ProtoMessage.Parse(jpvPayload);

                            // Remove all Alliance / Prism subarea info (Field 3) in tutorial zones (like Incarnam) to prevent client prism null reference crash
                            if (subAreaId == 20663)
                            {
                                jpvMsg.Fields.RemoveAll(f => f.FieldNumber == 3);
                            }

                            // A. Patch/Ensure Map ID (Field 4)
                            var mapIdField = jpvMsg.Fields.FirstOrDefault(f => f.FieldNumber == 4 && f.WireType == 0);
                            if (mapIdField != null)
                            {
                                mapIdField.VarIntValue = mapIdToLoad;
                            }
                            else
                            {
                                jpvMsg.Fields.Add(new ProtoField { FieldNumber = 4, WireType = 0, VarIntValue = mapIdToLoad });
                            }

                            // B. Patch/Ensure SubArea ID (Field 12)
                            var subAreaField = jpvMsg.Fields.FirstOrDefault(f => f.FieldNumber == 12 && f.WireType == 0);
                            if (subAreaField != null)
                            {
                                subAreaField.VarIntValue = subAreaId;
                            }
                            else
                            {
                                jpvMsg.Fields.Add(new ProtoField { FieldNumber = 12, WireType = 0, VarIntValue = subAreaId });
                            }

                            // C. Filter out other player actors and patch/swap all map actors (Field 15)
                            var actorFields = jpvMsg.Fields.Where(f => f.FieldNumber == 15 && f.WireType == 2).ToList();
                            var fieldsToRemove = new System.Collections.Generic.List<ProtoField>();
                            
                            // Remove other player characters (ID > 0 and not matching our player's ID)
                            foreach (var actorField in actorFields)
                            {
                                try
                                {
                                    var actorMsg = ProtoMessage.Parse(actorField.BytesValue);
                                    var contextualIdField = actorMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 0);
                                    if (contextualIdField != null)
                                    {
                                        long id = contextualIdField.VarIntValue;
                                        if (id > 0 && id != GameState.CharacterId && id != 13825558L && id != 906071769378L && id != 670668947750L)
                                        {
                                            fieldsToRemove.Add(actorField);
                                            LogDebug($"[Game Node] Removing other player actor from jpv: ID={id}");
                                        }
                                    }
                                }
                                catch { }
                            }
                            foreach (var field in fieldsToRemove)
                            {
                                jpvMsg.Fields.Remove(field);
                                actorFields.Remove(field);
                            }

                            bool playerPatched = false;
                            foreach (var actorField in actorFields)
                            {
                                var actorMsg = ProtoMessage.Parse(actorField.BytesValue);
                                var contextualIdField = actorMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 0);
                                
                                // In Dofus 3.6:
                                // Field 1 = Disposition (lhi/lfj)
                                // Field 2 = Details (lnk.lnj.lni)
                                // Field 3 = ContextualId
                                var origDispField = actorMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                                var origDetailsField = actorMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);

                                if (contextualIdField != null && (contextualIdField.VarIntValue == GameState.CharacterId || contextualIdField.VarIntValue == 13825558L || contextualIdField.VarIntValue == 906071769378L || contextualIdField.VarIntValue == 670668947750L))
                                {
                                    // This is our player character
                                    contextualIdField.VarIntValue = GameState.CharacterId;

                                    if (origDispField != null) actorMsg.Fields.Remove(origDispField);
                                    if (origDetailsField != null) actorMsg.Fields.Remove(origDetailsField);

                                    // 1. Details goes to Field 2 (Look)
                                    byte[] detailsBytes = GameState.PlayerActorDetails ?? (origDetailsField?.BytesValue ?? new byte[0]);
                                    actorMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 2, BytesValue = detailsBytes });

                                    // 2. Disposition goes to Field 1
                                    byte[] dispBytes;
                                    if (origDispField != null)
                                    {
                                        var dispMsg = ProtoMessage.Parse(origDispField.BytesValue);
                                        var cellField = dispMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 0);
                                        if (cellField != null)
                                        {
                                            cellField.VarIntValue = spawnCellId;
                                        }
                                        else
                                        {
                                            dispMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 0, VarIntValue = spawnCellId });
                                        }
                                        
                                        // Ensure orientation Field 5 exists as a flat VarInt (WireType 0)
                                        var orientField = dispMsg.Fields.FirstOrDefault(f => f.FieldNumber == 5 && f.WireType == 0);
                                        if (orientField == null)
                                        {
                                            // Remove any legacy nested field 5 if present
                                            var legacyOrient = dispMsg.Fields.FirstOrDefault(f => f.FieldNumber == 5 && f.WireType == 2);
                                            if (legacyOrient != null) dispMsg.Fields.Remove(legacyOrient);
                                            
                                            dispMsg.Fields.Add(new ProtoField { FieldNumber = 5, WireType = 0, VarIntValue = GameState.Orientation });
                                        }
                                        else
                                        {
                                            orientField.WireType = 0;
                                            orientField.VarIntValue = GameState.Orientation;
                                        }
                                        dispBytes = dispMsg.ToByteArray();
                                    }
                                    else
                                    {
                                        var dispMsg = new ProtoMessage();
                                        dispMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 0, VarIntValue = spawnCellId });
                                        dispMsg.Fields.Add(new ProtoField { FieldNumber = 5, WireType = 0, VarIntValue = GameState.Orientation });
                                        dispBytes = dispMsg.ToByteArray();
                                    }
                                    actorMsg.Fields.Add(new ProtoField { FieldNumber = 1, WireType = 2, BytesValue = dispBytes });

                                    playerPatched = true;
                                    LogDebug($"[Game Node] Patched player actor inside jpv template: ID={GameState.CharacterId}, Cell={spawnCellId}");
                                }

                                actorField.BytesValue = actorMsg.ToByteArray();
                            }

                            if (!playerPatched)
                            {
                                LogDebug("[Game Node] WARNING: Player actor not found in jpv template to patch! Adding as a new actor in Field 15...");
                                var lfjMsg = new ProtoMessage();
                                lfjMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 0, VarIntValue = spawnCellId });
                                lfjMsg.Fields.Add(new ProtoField { FieldNumber = 5, WireType = 0, VarIntValue = GameState.Orientation });

                                var actorMsg = new ProtoMessage();
                                actorMsg.Fields.Add(new ProtoField { FieldNumber = 1, WireType = 2, BytesValue = lfjMsg.ToByteArray() });
                                if (GameState.PlayerActorDetails != null)
                                {
                                    actorMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 2, BytesValue = GameState.PlayerActorDetails });
                                }
                                actorMsg.Fields.Add(new ProtoField { FieldNumber = 3, WireType = 0, VarIntValue = GameState.CharacterId });
                                
                                jpvMsg.Fields.Add(new ProtoField { FieldNumber = 15, WireType = 2, BytesValue = actorMsg.ToByteArray() });
                            }

                            byte[] patchedJpvBytes = jpvMsg.ToByteArray();
                            byte[] jpvPacket = NetworkEnvelope.BuildGameNodePacket("type.ankama.com/jpv", patchedJpvBytes);
                            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, jpvPacket);
                            LogDebug($"[Game Node] Sent patched jpv for Map ID: {mapIdToLoad}, Cell: {spawnCellId} from template.");
                        }
                        else
                        {
                            LogDebug("[Game Node] WARNING: jpv_packet.bin not found or empty! Falling back to minimalist dynamic jpv.");
                            var lfjMsg = new ProtoMessage();
                            lfjMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 0, VarIntValue = spawnCellId });
                            lfjMsg.Fields.Add(new ProtoField { FieldNumber = 5, WireType = 0, VarIntValue = GameState.Orientation });

                            var actorMsg = new ProtoMessage();
                            actorMsg.Fields.Add(new ProtoField { FieldNumber = 1, WireType = 2, BytesValue = lfjMsg.ToByteArray() });
                            if (GameState.PlayerActorDetails != null)
                            {
                                actorMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 2, BytesValue = GameState.PlayerActorDetails });
                            }
                            actorMsg.Fields.Add(new ProtoField { FieldNumber = 3, WireType = 0, VarIntValue = GameState.CharacterId });

                            var jpvMsg = new ProtoMessage();
                            jpvMsg.Fields.Add(new ProtoField { FieldNumber = 1, WireType = 0, VarIntValue = 1 });
                            jpvMsg.Fields.Add(new ProtoField { FieldNumber = 4, WireType = 0, VarIntValue = mapIdToLoad });
                            jpvMsg.Fields.Add(new ProtoField { FieldNumber = 12, WireType = 0, VarIntValue = subAreaId });
                            jpvMsg.Fields.Add(new ProtoField { FieldNumber = 15, WireType = 2, BytesValue = actorMsg.ToByteArray() });

                            byte[] dynamicJpvBytes = jpvMsg.ToByteArray();
                            byte[] jpvPacket = NetworkEnvelope.BuildGameNodePacket("type.ankama.com/jpv", dynamicJpvBytes);
                            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, jpvPacket);
                            LogDebug($"[Game Node] Sent minimalist dynamic jpv for Map ID: {mapIdToLoad}, Cell: {spawnCellId} (fallback).");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"[Game Node] Error patching/sending jpv: {ex.Message}");
                    }

                    // 3. Send dynamic lsy containing the active subarea ID and status to match official capture
                    var lsyMsg = new ProtoMessage();
                    lsyMsg.Fields.Add(new ProtoField { FieldNumber = 1, WireType = 0, VarIntValue = (long)subAreaId });
                    lsyMsg.Fields.Add(new ProtoField { FieldNumber = 3, WireType = 0, VarIntValue = 45L });
                    byte[] lsyPayload = lsyMsg.ToByteArray();
                    byte[] lsyPacket = NetworkEnvelope.BuildGameNodePacket("type.ankama.com/lsy", lsyPayload);
                    await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, lsyPacket);
                    LogDebug($"[Game Node] Sent dynamic lsy containing SubArea ID: {subAreaId} (matches official capture).");

                    // 4. Send dynamically instantiated kns (Fymx = true)
                    var knsMsg = new Jondo.Unity.Protocol.Messages.kns
                    {
                        Fymx = true
                    };
                    byte[] knsPacket = NetworkEnvelope.BuildGameNodePacket("type.ankama.com/kns", knsMsg.ToByteArray());
                    await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, knsPacket);
                    LogDebug("[Game Node] Sent dynamically instantiated kns (Fymx = true).");
                }
            }
        }

        public static async Task HandleLoy(NetworkStream stream)
        {
            // Server must ACK with kmw (empty packet)
            byte[] rawKmw = NetworkEnvelope.ConvertHexStringToByteArray("0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6B-6D-77");
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, rawKmw);
            LogDebug("[Game Node] Received loy (world load ack) - Sent kmw response.");
        }

        public static async Task HandleLpj(NetworkStream stream)
        {
            // Server must ACK with jfc (empty packet)
            byte[] rawJfc = NetworkEnvelope.ConvertHexStringToByteArray("0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6A-66-63");
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, rawJfc);
            LogDebug("[Game Node] Received lpj (secondary ready signal) - Sent jfc response.");
        }

        private static void LogDebug(string msg)
        {
            Program.LogDebug(msg);
        }
    }
}
