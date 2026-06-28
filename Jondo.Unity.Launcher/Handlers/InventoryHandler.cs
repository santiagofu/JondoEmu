using System;
using System.IO;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf;
using Jondo.Unity.Launcher.Network;

namespace Jondo.Unity.Launcher.Handlers
{
    public static class InventoryHandler
    {
        private static readonly Dictionary<int, int> ItemGidToSkinId = new Dictionary<int, int>
        {
            { 10801, 53375140 },  // Sombrero de papel
            { 10800, 68293394 },  // Capa de papel
            { 10798, 84411912 },  // Escudo de papel
            { 10797, 110287861 }  // Espada de papel
        };

        public static async Task HandleItemMovementRequest(NetworkStream stream, byte[] payload)
        {
            LogDebug("[Inventory] Received Item Movement Request (isi) [3.6]");
            byte[]? inner = NetworkEnvelope.ExtractMessagePayload(payload, "type.ankama.com/isi");
            if (inner != null)
            {
                long itemUid = 0;
                int newPosition = 63; // Default unequipped slot
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
                            itemUid = (long)NetworkEnvelope.ReadVarInt64(inner, ref pos);
                        }
                        else if (fieldNum == 3 && wireType == 0)
                        {
                            newPosition = (int)NetworkEnvelope.ReadVarInt(inner, ref pos);
                        }
                        else
                        {
                            NetworkEnvelope.SkipField(inner, wireType, ref pos);
                        }
                    }
                }
                catch { }

                LogDebug($"[Inventory] Client requested to equip/move Item UID {itemUid} to position {newPosition}");
                
                // 1. Process equipment change in memory (updates stats)
                ProcessEquipmentChange(itemUid, newPosition);

                // 2. Build and send iry (ObjectMovementMessage) to confirm the move
                byte[] iryPacket = BuildIryPacket(itemUid, newPosition);
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, iryPacket);
                LogDebug($"[Inventory] Sent iry (ObjectMovement) for Item UID {itemUid} to position {newPosition}.");

                // 3. Send luy (InventoryTransactionFinishedMessage) to commit transaction in client UI
                byte[] luyPacket = NetworkEnvelope.BuildGameNodePacket("type.ankama.com/luy", Array.Empty<byte>());
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, luyPacket);
                LogDebug("[Inventory] Sent luy (InventoryTransactionFinishedMessage) to client.");

                // 4. Send hhf and hhh shortcut bar content packets
                byte[] hhfPacket = BuildHhfPacket();
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, hhfPacket);
                byte[] hhhPacket = BuildHhhPacket();
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, hhhPacket);
                LogDebug("[Inventory] Sent hhf and hhh (ShortcutBar) to client.");

                // 5. Update character appearance in memory and get updated look bytes
                byte[]? lookBytes = UpdateCharacterLook();

                if (lookBytes != null)
                {
                    // 6. Send luq (UpdateSelfLookMessage) to update the local player avatar in inventory
                    byte[] luqPacket = BuildLuqPacket(lookBytes);
                    await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, luqPacket);
                    LogDebug("[Inventory] Sent luq (UpdateSelfLookMessage) to client.");
                }

                // 7. Send isf (InventoryWeightMessage)
                byte[] isfPacket = BuildIsfPacket();
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, isfPacket);
                LogDebug("[Inventory] Sent isf (InventoryWeightMessage) to client.");

                if (lookBytes != null)
                {
                    // 8. Send kku (ActorLookMessage) to update the character look in the world
                    byte[] kkuPacket = BuildKkuPacket(lookBytes, GameState.CharacterId);
                    await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, kkuPacket);
                    LogDebug("[Inventory] Sent kku (ActorLookMessage) to client.");
                }

                // 9. Build and send updated stats (kri) to update the client UI
                byte[]? updatedKri = BuildUpdatedKriPacket();
                if (updatedKri != null)
                {
                    await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, updatedKri);
                    LogDebug("[Inventory] Sent updated stats (kri) to client.");
                }

                // 10. Send kns (InventoryTransactionCompletion / KnockAck) to finalize the redraw cycle
                byte[] knsPacket = BuildKnsPacket();
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, knsPacket);
                LogDebug("[Inventory] Sent kns (InventoryTransactionCompletion) to client.");

                // We still call BuildUpdatedImdPacket to keep the server's originalImdPayload updated and consistent
                CharacterSelectionHandler.BuildUpdatedImdPacket(itemUid, newPosition);
            }
        }

        private static void ProcessEquipmentChange(long itemUid, int newPosition)
        {
            var item = GameState.GetInventoryItem(itemUid);
            if (item != null)
            {
                item.Position = newPosition;
                DatabaseManager.SaveItemPosition(itemUid, newPosition);

                // Update equipped cache
                if (newPosition >= 0 && newPosition < 63)
                {
                    var equipped = new EquippedItemInfo { Slot = newPosition };
                    if (CharacterSelectionHandler.ItemStatsByGid.TryGetValue(item.ItemId, out var stats))
                    {
                        foreach (var kvp in stats)
                        {
                            equipped.Stats[kvp.Key] = kvp.Value;
                        }
                    }
                    GameState.SetEquippedItem(itemUid, equipped);
                }
                else
                {
                    GameState.RemoveEquippedItem(itemUid);
                }
            }
        }

        private static byte[]? UpdateCharacterLook()
        {
            if (GameState.PlayerActorDetails == null)
            {
                LogDebug("[-] Warning: PlayerActorDetails is null. Cannot update character look.");
                return null;
            }

            try
            {
                var detailsMsg = ProtoMessage.Parse(GameState.PlayerActorDetails);
                var gbfoField = detailsMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                if (gbfoField == null)
                {
                    LogDebug("[-] Warning: gbfo field (Field 2) not found in PlayerActorDetails.");
                    return null;
                }

                var gbfoMsg = ProtoMessage.Parse(gbfoField.BytesValue);
                var gbewField = gbfoMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                if (gbewField == null)
                {
                    LogDebug("[-] Warning: gbew field (Field 2) not found in gbfo message.");
                    return null;
                }

                // gbewField.BytesValue is the wrapper message (Gbew)
                var wrapperMsg = ProtoMessage.Parse(gbewField.BytesValue);
                var entityLookField = wrapperMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                if (entityLookField == null)
                {
                    LogDebug("[-] Warning: EntityLook field (Field 2) not found in gbew wrapper.");
                    return null;
                }

                var lookMsg = ProtoMessage.Parse(entityLookField.BytesValue);
                
                // 1. Extract existing skins from lookMsg (Field 2 of EntityLook)
                var allSkins = new List<int>();
                foreach (var field in lookMsg.Fields)
                {
                    if (field.FieldNumber == 2)
                    {
                        if (field.WireType == 2) // packed
                        {
                            int subPos = 0;
                            while (subPos < field.BytesValue.Length)
                            {
                                allSkins.Add((int)ReadVarIntFromBytes(field.BytesValue, ref subPos));
                            }
                        }
                        else if (field.WireType == 0) // unpacked
                        {
                            allSkins.Add((int)field.VarIntValue);
                        }
                    }
                }

                // 2. Filter/remove old equipment skins
                var equipmentSkins = new HashSet<int>(ItemGidToSkinId.Values);
                var updatedSkins = allSkins.Where(s => !equipmentSkins.Contains(s)).ToList();

                // 3. Add skins of currently equipped items
                foreach (var equippedItem in GameState.GetEquippedItemsCopy())
                {
                    long uid = equippedItem.Key;
                    var playerItem = GameState.GetInventoryItem(uid);
                    if (playerItem != null)
                    {
                        if (ItemGidToSkinId.TryGetValue(playerItem.ItemId, out int skinId))
                        {
                            if (!updatedSkins.Contains(skinId))
                            {
                                updatedSkins.Add(skinId);
                            }
                        }
                    }
                }

                // 4. Update skins field in lookMsg (EntityLook)
                lookMsg.Fields.RemoveAll(f => f.FieldNumber == 2);
                using (var msSkins = new MemoryStream())
                {
                    foreach (int skin in updatedSkins)
                    {
                        ProtoMessage.WriteVarInt(msSkins, (ulong)skin);
                    }
                    lookMsg.Fields.Add(new ProtoField
                    {
                        FieldNumber = 2,
                        WireType = 2,
                        BytesValue = msSkins.ToArray()
                    });
                }

                byte[] entityLookBytes = lookMsg.ToByteArray();

                // 5. Update EntityLook in wrapperMsg and write back to PlayerActorDetails
                entityLookField.BytesValue = entityLookBytes;
                gbewField.BytesValue = wrapperMsg.ToByteArray();
                gbfoField.BytesValue = gbfoMsg.ToByteArray();
                GameState.PlayerActorDetails = detailsMsg.ToByteArray();
                
                LogDebug($"[Appearance] Updated character look schema-freely. Total skins: {updatedSkins.Count}.");

                DatabaseManager.SaveCharacterLook(GameState.CharacterId, entityLookBytes);
                LogDebug("[Appearance] Saved updated character look to database.");

                return entityLookBytes;
            }
            catch (Exception ex)
            {
                LogDebug($"[-] Error in UpdateCharacterLook schema-freely: {ex.Message}");
                return null;
            }
        }

        private static ulong ReadVarIntFromBytes(byte[] data, ref int pos)
        {
            ulong value = 0;
            int shift = 0;
            while (pos < data.Length)
            {
                byte b = data[pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return value;
        }

        private static byte[] BuildIryPacket(long itemUid, int position)
        {
            using var ms = new MemoryStream();
            var output = new CodedOutputStream(ms);
            
            // Tag 1 (wire type 0): Item UID
            output.WriteTag((uint)((1 << 3) | 0));
            output.WriteInt64(itemUid);

            // Tag 2 (wire type 0): Position
            output.WriteTag((uint)((2 << 3) | 0));
            output.WriteInt32(position);

            output.Flush();
            return NetworkEnvelope.BuildGameNodePacket("type.ankama.com/iry", ms.ToArray());
        }

        private static byte[] BuildLuqPacket(byte[] lookBytes)
        {
            using var ms = new MemoryStream();
            var output = new CodedOutputStream(ms);
            
            // Tag 2 (wire type 2): EntityLook (lkr)
            output.WriteTag((uint)((2 << 3) | 2));
            output.WriteBytes(ByteString.CopyFrom(lookBytes));

            // Tag 3 (wire type 2): UUID string to identify this session/look update
            output.WriteTag((uint)((3 << 3) | 2));
            output.WriteString(Guid.NewGuid().ToString());

            output.Flush();
            return NetworkEnvelope.BuildGameNodePacket("type.ankama.com/luq", ms.ToArray());
        }

        private static byte[] BuildKkuPacket(byte[] lookBytes, long characterId)
        {
            using var ms = new MemoryStream();
            var output = new CodedOutputStream(ms);
            
            // Tag 1 (wire type 2): EntityLook
            output.WriteTag((uint)((1 << 3) | 2));
            output.WriteBytes(ByteString.CopyFrom(lookBytes));

            // Tag 2 (wire type 0): Character ID
            output.WriteTag((uint)((2 << 3) | 0));
            output.WriteInt64(characterId);

            output.Flush();
            return NetworkEnvelope.BuildGameNodePacket("type.ankama.com/kku", ms.ToArray());
        }

        private static byte[] BuildHhfPacket()
        {
            using var ms = new MemoryStream();
            var output = new CodedOutputStream(ms);
            output.WriteTag((uint)((1 << 3) | 0));
            output.WriteInt32(2); // Value 2 (HGZ_EBYW) to match Dofus 3.6 shortcut bar state
            output.Flush();
            return NetworkEnvelope.BuildGameNodePacket("type.ankama.com/hhf", ms.ToArray());
        }

        private static byte[] BuildHhhPacket()
        {
            using var ms = new MemoryStream();
            var output = new CodedOutputStream(ms);
            output.WriteTag((uint)((1 << 3) | 0));
            output.WriteInt32(2); // Value 2 (HGZ_EBYW) to match Dofus 3.6 shortcut bar state
            output.Flush();
            return NetworkEnvelope.BuildGameNodePacket("type.ankama.com/hhh", ms.ToArray());
        }

        private static byte[] BuildIsfPacket()
        {
            using var ms = new MemoryStream();
            var output = new CodedOutputStream(ms);
            
            // Tag 1 (wire type 0): Current Weight
            output.WriteTag((uint)((1 << 3) | 0));
            output.WriteInt32(9);

            // Tag 2 (wire type 0): Max Weight
            output.WriteTag((uint)((2 << 3) | 0));
            output.WriteInt32(1000);

            output.Flush();
            return NetworkEnvelope.BuildGameNodePacket("type.ankama.com/isf", ms.ToArray());
        }

        private static byte[] BuildKnsPacket()
        {
            using var ms = new MemoryStream();
            var output = new CodedOutputStream(ms);
            output.WriteTag((uint)((1 << 3) | 0));
            output.WriteBool(true);
            output.Flush();
            return NetworkEnvelope.BuildGameNodePacket("type.ankama.com/kns", ms.ToArray());
        }

        public static byte[]? BuildUpdatedKriPacket()
        {
            if (CharacterSelectionHandler.originalKriPayload == null)
            {
                Console.WriteLine("[-] Error: originalKriPayload is null!");
                return null;
            }

            try
            {
                var rootMsg = ProtoMessage.Parse(CharacterSelectionHandler.originalKriPayload);
                var rootField = rootMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 2);
                if (rootField == null) return null;

                var wrapperMsg = ProtoMessage.Parse(rootField.BytesValue);
                var wrapperField = wrapperMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                if (wrapperField == null) return null;

                var anyMsg = ProtoMessage.Parse(wrapperField.BytesValue);
                var anyValueField = anyMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                if (anyValueField == null) return null;

                var kriMsg = ProtoMessage.Parse(anyValueField.BytesValue);
                var larField = kriMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                if (larField == null) return null;

                var larMsg = ProtoMessage.Parse(larField.BytesValue);

                // Patch remaining points (capital)
                var remainingField = larMsg.Fields.FirstOrDefault(f => f.FieldNumber == 7 && f.WireType == 0);
                if (remainingField != null)
                {
                    remainingField.VarIntValue = (long)GameState.CharacterRemainingPoints;
                }

                // Patch statistics by mapping stats IDs in fields
                foreach (var field in larMsg.Fields)
                {
                    if (field.FieldNumber == 3 && field.WireType == 2)
                    {
                        var statMsg = ProtoMessage.Parse(field.BytesValue);
                        var statIdField = statMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 0);
                        if (statIdField != null)
                        {
                            int statId = (int)statIdField.VarIntValue;
                            int databaseBaseVal = 0;

                            if (statId == 11) databaseBaseVal = GameState.StatVitality;
                            else if (statId == 12) databaseBaseVal = GameState.StatWisdom;
                            else if (statId == 10) databaseBaseVal = GameState.StatStrength;
                            else if (statId == 15) databaseBaseVal = GameState.StatIntelligence;
                            else if (statId == 13) databaseBaseVal = GameState.StatChance;
                            else if (statId == 14) databaseBaseVal = GameState.StatAgility;
                            else continue;

                            // Calculate equipped item bonuses for this stat ID
                            int equipmentBonus = 0;
                            foreach (var equipped in GameState.GetEquippedItemsCopy().Values)
                            {
                                if (equipped.Stats.TryGetValue(statId, out int bonus))
                                {
                                    equipmentBonus += bonus;
                                }
                            }

                            // Patch base value (Field 2)
                            var baseValField = statMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 0);
                            if (baseValField != null)
                            {
                                baseValField.VarIntValue = (long)databaseBaseVal;
                            }
                            else
                            {
                                statMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 0, VarIntValue = (long)databaseBaseVal });
                            }
                            
                            // Patch equipment/objects value (Field 4)
                            var objValField = statMsg.Fields.FirstOrDefault(f => f.FieldNumber == 4 && f.WireType == 0);
                            if (objValField != null)
                            {
                                objValField.VarIntValue = (long)equipmentBonus;
                            }
                            else
                            {
                                statMsg.Fields.Add(new ProtoField { FieldNumber = 4, WireType = 0, VarIntValue = (long)equipmentBonus });
                            }
                            
                            field.BytesValue = statMsg.ToByteArray();
                        }
                    }
                }

                anyValueField.BytesValue = kriMsg.ToByteArray();
                wrapperField.BytesValue = anyMsg.ToByteArray();
                rootField.BytesValue = wrapperMsg.ToByteArray();

                CharacterSelectionHandler.originalKriPayload = rootMsg.ToByteArray();
                return CharacterSelectionHandler.originalKriPayload;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error building updated kri: {ex.Message}");
                return null;
            }
        }

        private static void LogDebug(string msg)
        {
            Program.LogDebug(msg);
        }
    }
}
