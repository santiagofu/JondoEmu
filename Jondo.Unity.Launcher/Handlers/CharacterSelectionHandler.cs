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
    public static class CharacterSelectionHandler
    {
        public static byte[]? originalKriPayload;
        public static byte[]? originalImdPayload;

        private static readonly Dictionary<int, (int OfficialGid, long ClientUid)> OldGidToNewGidAndUid = new Dictionary<int, (int, long)>
        {
            { 1021, (10784, 10699035) }, // Amuleto del intrépido
            { 1043, (10800, 10699036) }, // Capa del intrépido
            { 785,  (10785, 10699037) }, // Anillo del intrépido
            { 1040, (10797, 10699038) }, // Espada Nsiosa
            { 1041, (10799, 10699039) }, // Cinturón del intrépido
            { 799,  (10794, 10699040) }, // Botas del intrépido
            { 1011, (10798, 10699041) }, // Escudo del intrépido
            { 1042, (10801, 10699042) }, // Sombrero del intrépido
            { 809,  (19622, 10699043) }  // Anillo del audaz
        };

        public static readonly Dictionary<int, Dictionary<int, int>> ItemStatsByGid = new Dictionary<int, Dictionary<int, int>>
        {
            // Anillo del audaz (GID 19622) -> +3 Potencia (Stat ID 25)
            { 19622, new Dictionary<int, int> { { 25, 3 } } },
            // Sombrero del intrépido (GID 10801) -> +1 Vitalidad (Stat ID 11), +1 Prospección (Stat ID 48)
            { 10801, new Dictionary<int, int> { { 11, 1 }, { 48, 1 } } },
            // Capa del intrépido (GID 10800) -> +1 Vitalidad (Stat ID 11), +1 Prospección (Stat ID 48)
            { 10800, new Dictionary<int, int> { { 11, 1 }, { 48, 1 } } },
            // Escudo del intrépido (GID 10798) -> +3 Vitalidad (Stat ID 11)
            { 10798, new Dictionary<int, int> { { 11, 3 } } },
            // Espada Nsiosa (GID 10797) -> +1 Vitalidad (Stat ID 11), +1 Prospección (Stat ID 48)
            { 10797, new Dictionary<int, int> { { 11, 1 }, { 48, 1 } } },
            // Amuleto del intrépido (GID 10784) -> +1 Vitalidad (Stat ID 11), +1 Prospección (Stat ID 48)
            { 10784, new Dictionary<int, int> { { 11, 1 }, { 48, 1 } } },
            // Anillo del intrépido (GID 10785) -> +1 Vitalidad (Stat ID 11), +1 Prospección (Stat ID 48)
            { 10785, new Dictionary<int, int> { { 11, 1 }, { 48, 1 } } },
            // Cinturón del intrépido (GID 10799) -> +1 Vitalidad (Stat ID 11), +1 Prospección (Stat ID 48)
            { 10799, new Dictionary<int, int> { { 11, 1 }, { 48, 1 } } },
            // Botas del intrépido (GID 10794) -> +1 Vitalidad (Stat ID 11), +1 Prospección (Stat ID 48)
            { 10794, new Dictionary<int, int> { { 11, 1 }, { 48, 1 } } }
        };

        public static async Task HandleAuthRequest(NetworkStream stream, byte[] payload, string payloadStr)
        {
            if (payloadStr.Contains("type.ankama.com/jtk"))
            {
                Console.WriteLine("[Game Node] Received New Auth Request (jtk)");
                // Send jtm (new auth accepted, active subscription)
                byte[] jtmFrame = NetworkEnvelope.ConvertHexStringToByteArray("33-0A-31-12-2F-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6A-74-6D-12-18-08-01-12-14-32-30-33-35-2D-30-31-2D-30-31-54-30-30-3A-30-30-3A-30-30-5A");
                await stream.WriteAsync(jtmFrame, 0, jtmFrame.Length);
                Console.WriteLine("[Game Node] Sent New Auth Accepted (jtm)");
            }
            else if (payloadStr.Contains("type.ankama.com/knx"))
            {
                Console.WriteLine("[Game Node] Received New Auth Request (knx) [3.6]");
                // Send kof + lor + hnp + knr + mfa + mez + hnv in one write (from Frame 557)
                byte[] frame557 = NetworkEnvelope.ConvertHexStringToByteArray("19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6B-6F-66-24-1A-22-0A-20-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6C-6F-72-12-09-08-78-10-DC-BC-D5-D9-05-19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-68-6E-70-19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6B-6E-72-19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6D-66-61-19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6D-65-7A-19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-68-6E-76");
                await stream.WriteAsync(frame557, 0, frame557.Length);
                Console.WriteLine("[Game Node] Sent Auth Accepted and Handshake Packets (frame557)");

                // Send klp (character list - empty)
                byte[] klpFrame = NetworkEnvelope.ConvertHexStringToByteArray("1D-1A-1B-0A-19-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6B-6C-70-12-02-10-00");
                await stream.WriteAsync(klpFrame, 0, klpFrame.Length);
                Console.WriteLine("[Game Node] Sent Character List (klp) - Empty [New Build]");
            }
            else
            {
                Console.WriteLine("[Game Node] Received Auth Request (ise)");
                // Send iua (auth accepted)
                byte[] iuaFrame = NetworkEnvelope.ConvertHexStringToByteArray("28-0A-26-12-24-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-69-75-61-12-0D-0A-02-14-23-10-06-18-A2-82-D8-B0-AF-1A");
                await stream.WriteAsync(iuaFrame, 0, iuaFrame.Length);
                Console.WriteLine("[Game Node] Sent Auth Accepted (iua) [Old Build]");
                
                // Send isj (character list - empty)
                byte[] isjFrame = NetworkEnvelope.ConvertHexStringToByteArray("19-0A-17-12-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-69-75-6A");
                await stream.WriteAsync(isjFrame, 0, isjFrame.Length);
                Console.WriteLine("[Game Node] Sent Character List (isj) - Empty [Old Build]");
            }
        }

        public static async Task HandleCharacterListRequest(NetworkStream stream, byte[] payload, string payloadStr)
        {
            if (payloadStr.Contains("type.ankama.com/kpc"))
            {
                Console.WriteLine("[Game Node] Received Ticket/Ping Request (kpc) [3.6]");
                // Send kos (from Frame 558)
                byte[] frame558 = NetworkEnvelope.ConvertHexStringToByteArray("19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6B-6F-73");
                await stream.WriteAsync(frame558, 0, frame558.Length);
                Console.WriteLine("[Game Node] Sent Server Selection Status (frame558) in response to kpc");
            }
            else if (payloadStr.Contains("type.ankama.com/ksx"))
            {
                Console.WriteLine("[Game Node] Received Character List Request (ksx) [3.6] - Waiting for kpa");
            }
            else if (payloadStr.Contains("type.ankama.com/kpa"))
            {
                Console.WriteLine("[Game Node] Received Character List Request (kpa) [3.6] - Sending Character List");
                
                var dbChars = DatabaseManager.GetCharactersByAccountId(188940901);
                string activeCharName = "CADERNIS";
                long activeCharId = 13825558L;
                int level = 2;
                string lookHex = "080118032218A28B9B0FCBE5F615A4E1B91992A6C820888CA028F5B7CB342A035BE410420134320220013809";
                
                if (dbChars.Count > 0)
                {
                    activeCharName = dbChars[0].Name;
                    activeCharId = dbChars[0].Id;
                    level = dbChars[0].Level;
                    lookHex = dbChars[0].LookHex;
                }

                // Send mes (from Frame 562)
                byte[] frame562 = NetworkEnvelope.ConvertHexStringToByteArray("19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6D-65-73");
                await stream.WriteAsync(frame562, 0, frame562.Length);
                
                // Send knv 1 (from Frame 563)
                byte[] frame563 = NetworkEnvelope.ConvertHexStringToByteArray("1F-1A-1D-0A-1B-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6B-6E-76-12-04-08-01-10-01");
                await stream.WriteAsync(frame563, 0, frame563.Length);
                
                // Send knv 2 (from Frame 564)
                byte[] frame564 = NetworkEnvelope.ConvertHexStringToByteArray("1D-1A-1B-0A-19-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6B-6E-76-12-02-08-01");
                await stream.WriteAsync(frame564, 0, frame564.Length);
                
                // Send knv 3 (from Frame 565)
                byte[] frame565 = NetworkEnvelope.ConvertHexStringToByteArray("19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6B-6E-76");
                await stream.WriteAsync(frame565, 0, frame565.Length);
                
                // Send ksq (character list containing active character name/ID)
                byte[] frame566 = BuildKsqPacket(activeCharName, activeCharId, level, lookHex);
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame566);
                
                // Send jrf (from Frame 568)
                byte[] frame568 = NetworkEnvelope.ConvertHexStringToByteArray("19-1A-17-0A-15-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6A-72-66");
                await stream.WriteAsync(frame568, 0, frame568.Length);
                
                Console.WriteLine("[Game Node] Sent Character List (ksq) and World Ready (jrf)");
            }
            else
            {
                // Old protocol Character List Request (jto)
                Console.WriteLine("[Game Node] Received Character List Request (jto)");
                // Send ldt (character list - empty)
                byte[] ldtFrame = NetworkEnvelope.ConvertHexStringToByteArray("1B-0A-19-12-17-0A-13-74-79-70-65-2E-61-6E-6B-61-6D-61-2E-63-6F-6D-2F-6C-64-74-12-00");
                await stream.WriteAsync(ldtFrame, 0, ldtFrame.Length);
                Console.WriteLine("[Game Node] Sent Character List (ldt) - Empty");
            }
        }

        public static void HandleCharacterSelectionRequest()
        {
            Console.WriteLine("[Game Node] Received Character Selection Request (ksl) [3.6]");
            
            long characterIdToLoad = 13825558L;
            DatabaseManager.LoadCharacter(characterIdToLoad);
            
            // Extract player actor details from our standalone jpv_packet.bin template
            string jpvPath = NetworkEnvelope.ResolvePacketPath("jpv_packet.bin");
            if (File.Exists(jpvPath))
            {
                try
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
                        byte[] jpvPayload = new byte[jpvLength];
                        Array.Copy(jpvFileBytes, jpvPos, jpvPayload, 0, jpvLength);
                        ExtractPlayerActorDetails(jpvPayload);
                        Program.LogDebug("[Stats/Inventory] Successfully extracted and patched player actor details from jpv_packet.bin.");
                    }
                }
                catch (Exception ex)
                {
                    Program.LogDebug($"[-] Error extracting player actor details from jpv_packet.bin: {ex.Message}");
                }
            }
            
            // 1. Initialize original templates directly from BasePayloads
            originalKriPayload = BasePayloads.OriginalKri;
            originalImdPayload = SynchronizeIcwUids(BasePayloads.OriginalImd);
            InitializeStatsFromOriginalKri();

            // 2. Load inventory from database
            var dbInventory = DatabaseManager.LoadInventory(characterIdToLoad);
            if (dbInventory.Count == 0)
            {
                Program.LogDebug("[Inventory Seed] Database inventory is empty! Seeding starting inventory...");
                
                // Populate GameState with default synchronized items from OldGidToNewGidAndUid mapping
                var defaultItems = new List<PlayerItem>
                {
                    new PlayerItem { Uid = 10699035, ItemId = 10784, Quantity = 1, Position = 0 },  // Amuleto del intrépido
                    new PlayerItem { Uid = 10699036, ItemId = 10800, Quantity = 1, Position = 7 },  // Capa del intrépido
                    new PlayerItem { Uid = 10699037, ItemId = 10785, Quantity = 1, Position = 2 },  // Anillo del intrépido
                    new PlayerItem { Uid = 10699038, ItemId = 10797, Quantity = 1, Position = 1 },  // Espada Nsiosa
                    new PlayerItem { Uid = 10699039, ItemId = 10799, Quantity = 1, Position = 3 },  // Cinturón del intrépido
                    new PlayerItem { Uid = 10699040, ItemId = 10794, Quantity = 1, Position = 5 },  // Botas del intrépido
                    new PlayerItem { Uid = 10699041, ItemId = 10798, Quantity = 1, Position = 15 }, // Escudo del intrépido
                    new PlayerItem { Uid = 10699042, ItemId = 10801, Quantity = 1, Position = 6 },  // Sombrero del intrépido
                    new PlayerItem { Uid = 10699043, ItemId = 19622, Quantity = 1, Position = 4 }   // Anillo del audaz
                };
                GameState.SetInventory(defaultItems);
                
                DatabaseManager.SeedInventory(characterIdToLoad, defaultItems);
            }
            else
            {
                Program.LogDebug($"[Inventory Load] Loaded {dbInventory.Count} items from database. Setting as active inventory.");
                GameState.SetInventory(dbInventory);
            }

            // 3. Reconstruct equipped items cache and synchronize originalImdPayload
            GameState.ClearEquippedItems();
            foreach (var item in GameState.GetInventoryCopy())
            {
                if (item.Position >= 0 && item.Position < 63)
                {
                    var equipped = new EquippedItemInfo { Slot = item.Position };
                    if (ItemStatsByGid.TryGetValue(item.ItemId, out var stats))
                    {
                        foreach (var kvp in stats)
                        {
                            equipped.Stats[kvp.Key] = kvp.Value;
                        }
                    }
                    GameState.SetEquippedItem(item.Uid, equipped);
                    
                    // Synchronize the position in originalImdPayload
                    BuildUpdatedImdPacket(item.Uid, item.Position);
                }
            }
        }

        private static byte[] BuildKsqPacket(string characterName, long characterId, int level, string lookHex)
        {
            // 1. Build character details (lgz.lgy.lgx)
            using var detailsMs = new MemoryStream();
            {
                var output = new CodedOutputStream(detailsMs);
                
                // Parse the look hex from database/fallback
                byte[] lookRawBytes = NetworkEnvelope.ConvertHexStringToByteArray(lookHex);
                
                byte[] lookBytes;
                try
                {
                    var dbLookMsg = ProtoMessage.Parse(lookRawBytes);
                    var entityLook = new ProtoMessage();
                    var lookMsg = new ProtoMessage();
                    
                    // Copy fields 1, 3, 4, 5, 8 to entityLook
                    foreach (var field in dbLookMsg.Fields)
                    {
                        if (field.FieldNumber == 1 || field.FieldNumber == 3 || field.FieldNumber == 4 || field.FieldNumber == 5 || field.FieldNumber == 8)
                        {
                            entityLook.Fields.Add(field);
                        }
                    }
                    
                    // Add entityLook to lookMsg under Field 2
                    lookMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 2, BytesValue = entityLook.ToByteArray() });
                    
                    // Copy fields 6, 7 to lookMsg
                    foreach (var field in dbLookMsg.Fields)
                    {
                        if (field.FieldNumber == 6 || field.FieldNumber == 7)
                        {
                            lookMsg.Fields.Add(field);
                        }
                    }
                    
                    // Add defaults for fields 6 and 7 if missing
                    if (!lookMsg.Fields.Any(f => f.FieldNumber == 6))
                    {
                        lookMsg.Fields.Add(new ProtoField { FieldNumber = 6, WireType = 2, BytesValue = new byte[] { 0x20, 0x01 } });
                    }
                    if (!lookMsg.Fields.Any(f => f.FieldNumber == 7))
                    {
                        lookMsg.Fields.Add(new ProtoField { FieldNumber = 7, WireType = 0, VarIntValue = 9 });
                    }
                    
                    lookBytes = lookMsg.ToByteArray();
                }
                catch (Exception ex)
                {
                    Program.LogDebug($"[BuildKsqPacket] Error parsing/wrapping look: {ex.Message}. Using fallback wrapping.");
                    // Fallback wrapping: wrap raw bytes directly
                    using var wrapMs = new MemoryStream();
                    var wrapOut = new CodedOutputStream(wrapMs);
                    wrapOut.WriteTag((uint)((2 << 3) | 2));
                    wrapOut.WriteBytes(ByteString.CopyFrom(lookRawBytes));
                    wrapOut.Flush();
                    lookBytes = wrapMs.ToArray();
                }
                
                // Tag 2 (wire type 2): Look
                output.WriteTag((uint)((2 << 3) | 2));
                output.WriteBytes(ByteString.CopyFrom(lookBytes));

                // Tag 3 (wire type 2): Name
                output.WriteTag((uint)((3 << 3) | 2));
                output.WriteString(characterName);

                // Tag 6 (wire type 0): Level
                output.WriteTag((uint)((6 << 3) | 0));
                output.WriteInt32(level);

                output.Flush();
            }
            byte[] detailsBytes = detailsMs.ToArray();

            // 2. Build character (lgz)
            using var characterMs = new MemoryStream();
            {
                var output = new CodedOutputStream(characterMs);
                // Tag 1 (wire type 2): details
                output.WriteTag((uint)((1 << 3) | 2));
                output.WriteBytes(ByteString.CopyFrom(detailsBytes));

                // Tag 2 (wire type 0): character ID
                output.WriteTag((uint)((2 << 3) | 0));
                output.WriteInt64(characterId);

                output.Flush();
            }
            byte[] characterBytes = characterMs.ToArray();

            // 3. Build ksq
            using var ksqMs = new MemoryStream();
            {
                var output = new CodedOutputStream(ksqMs);
                // Tag 1 (wire type 2): repeated character
                output.WriteTag((uint)((1 << 3) | 2));
                output.WriteBytes(ByteString.CopyFrom(characterBytes));
                output.Flush();
            }
            byte[] ksqBytes = ksqMs.ToArray();

            return NetworkEnvelope.BuildGameNodePacket("type.ankama.com/ksq", ksqBytes);
        }

        public static void ExtractPlayerActorDetailsFromTemplate(byte[] templateBytes)
        {
            try
            {
                using var ms = new MemoryStream(templateBytes);
                while (ms.Position < ms.Length)
                {
                    int length = 0;
                    int shift = 0;
                    while (true)
                    {
                        int b = ms.ReadByte();
                        if (b == -1) break;
                        length |= (b & 0x7F) << shift;
                        if ((b & 0x80) == 0) break;
                        shift += 7;
                    }

                    if (length <= 0 || ms.Position + length > ms.Length) break;

                    byte[] packetPayload = new byte[length];
                    ms.Read(packetPayload, 0, length);

                    byte[] targetJpvBytes = System.Text.Encoding.UTF8.GetBytes("type.ankama.com/jpv");
                    if (NetworkEnvelope.ContainsSequence(packetPayload, targetJpvBytes))
                    {
                        var rootMsg = ProtoMessage.Parse(packetPayload);
                        var rootField = rootMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 2);
                        if (rootField == null) continue;

                        var wrapperMsg = ProtoMessage.Parse(rootField.BytesValue);
                        var wrapperField = wrapperMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                        if (wrapperField == null) continue;

                        var anyMsg = ProtoMessage.Parse(wrapperField.BytesValue);
                        var anyValueField = anyMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                        if (anyValueField == null) continue;

                        var jpvMsg = ProtoMessage.Parse(anyValueField.BytesValue);

                        var actorFields = jpvMsg.Fields.Where(f => f.FieldNumber == 15 && f.WireType == 2).ToList();
                        foreach (var actorField in actorFields)
                        {
                            var actorMsg = ProtoMessage.Parse(actorField.BytesValue);
                            var contextualIdField = actorMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 0);
                            if (contextualIdField != null && (contextualIdField.VarIntValue == 670668947750L || contextualIdField.VarIntValue == 13825558L || contextualIdField.VarIntValue == 906071769378L))
                            {
                                var detailsField = actorMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                                if (detailsField != null)
                                {
                                    byte[] templateDetailsBytes = detailsField.BytesValue;
                                    var detailsMsg = ProtoMessage.Parse(templateDetailsBytes);

                                    // 1. Look: detailsMsg.Field 1 (EntityLook)
                                    var lookField = detailsMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                                    if (lookField != null && GameState.LookBytes != null && GameState.LookBytes.Length > 0)
                                    {
                                        lookField.BytesValue = GameState.LookBytes;
                                        Program.LogDebug("[Stats/Inventory] Swapped EntityLook (Field 1) in player details with custom GameState.LookBytes.");
                                    }

                                    // 2. Name: detailsMsg.Field 2 (HumanoidOption) -> Field 2 (HumanInformations) -> Field 3 (Name)
                                    var minimalInfoField = detailsMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                                    if (minimalInfoField != null)
                                    {
                                        var humanoidOptionMsg = ProtoMessage.Parse(minimalInfoField.BytesValue);
                                        var humanInfosField = humanoidOptionMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                                        if (humanInfosField != null)
                                        {
                                            var humanInfosMsg = ProtoMessage.Parse(humanInfosField.BytesValue);
                                            var nameField = humanInfosMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 2);
                                            if (nameField != null)
                                            {
                                                nameField.BytesValue = System.Text.Encoding.UTF8.GetBytes(GameState.CharacterName);
                                                humanInfosField.BytesValue = humanInfosMsg.ToByteArray();
                                                minimalInfoField.BytesValue = humanoidOptionMsg.ToByteArray();
                                                Program.LogDebug($"[Stats/Inventory] Patched character name to: {GameState.CharacterName} inside HumanInformations (Field 2 -> Field 2 -> Field 3).");
                                            }
                                        }
                                    }

                                    if (GameState.PlayerActorDetails == null)
                                    {
                                        GameState.PlayerActorDetails = detailsMsg.ToByteArray();
                                        Program.LogDebug($"[Game Node] Dynamically initialized and patched GameState.PlayerActorDetails from template (length: {GameState.PlayerActorDetails.Length} bytes).");
                                    }
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("[-] Error in ExtractPlayerActorDetailsFromTemplate: " + ex.Message);
            }
        }

        private static void ExtractPlayerActorDetails(byte[] jpvPacket)
        {
            try
            {
                var rootMsg = ProtoMessage.Parse(jpvPacket);
                var rootField = rootMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 2);
                if (rootField == null) return;
                
                var wrapperMsg = ProtoMessage.Parse(rootField.BytesValue);
                var wrapperField = wrapperMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                if (wrapperField == null) return;
                
                var anyMsg = ProtoMessage.Parse(wrapperField.BytesValue);
                var anyValueField = anyMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                if (anyValueField == null) return;
                
                var innerMsg = ProtoMessage.Parse(anyValueField.BytesValue);
                
                // Extract initial Map ID from Field 4 of the world entering jpv (informational only, position is persisted in SQLite)
                var mapIdField = innerMsg.Fields.FirstOrDefault(f => f.FieldNumber == 4 && f.WireType == 0);
                if (mapIdField != null)
                {
                    Program.LogDebug($"[Stats/Inventory] Template Map ID from world entering jpv is: {mapIdField.VarIntValue} (ignored in favor of SQLite value {GameState.MapId})");
                }
                
                var actorFields = innerMsg.Fields.Where(f => f.FieldNumber == 15 && f.WireType == 2).ToList();
                foreach (var actorField in actorFields)
                {
                    var actorMsg = ProtoMessage.Parse(actorField.BytesValue);
                    var contextualIdField = actorMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 0);
                    if (contextualIdField != null && (contextualIdField.VarIntValue == GameState.CharacterId || contextualIdField.VarIntValue == 670668947750L || contextualIdField.VarIntValue == 906071769378L || contextualIdField.VarIntValue == 13825558L))
                    {
                        var detailsField = actorMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                        if (detailsField != null)
                        {
                            byte[] detailsBytes = detailsField.BytesValue;
                            try
                            {
                                var detailsMsg = ProtoMessage.Parse(detailsBytes);
                                
                                // 1. Look: detailsMsg.Field 1 (EntityLook)
                                var lookField = detailsMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                                if (lookField != null && GameState.LookBytes != null && GameState.LookBytes.Length > 0)
                                {
                                    lookField.BytesValue = GameState.LookBytes;
                                    Program.LogDebug("[Stats/Inventory] Swapped EntityLook (Field 1) in player details with custom GameState.LookBytes.");
                                }

                                // 2. Name: detailsMsg.Field 2 (HumanoidOption) -> Field 2 (HumanInformations) -> Field 3 (Name)
                                var minimalInfoField = detailsMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                                if (minimalInfoField != null)
                                {
                                    var humanoidOptionMsg = ProtoMessage.Parse(minimalInfoField.BytesValue);
                                    var humanInfosField = humanoidOptionMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                                    if (humanInfosField != null)
                                    {
                                        var humanInfosMsg = ProtoMessage.Parse(humanInfosField.BytesValue);
                                        var nameField = humanInfosMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 2);
                                        if (nameField != null)
                                        {
                                            nameField.BytesValue = System.Text.Encoding.UTF8.GetBytes(GameState.CharacterName);
                                            humanInfosField.BytesValue = humanInfosMsg.ToByteArray();
                                            minimalInfoField.BytesValue = humanoidOptionMsg.ToByteArray();
                                            Program.LogDebug($"[Stats/Inventory] Patched character name to: {GameState.CharacterName} inside HumanInformations (Field 2 -> Field 2 -> Field 3).");
                                        }
                                    }
                                }

                                detailsBytes = detailsMsg.ToByteArray();
                            }
                            catch (Exception ex)
                            {
                                Program.LogDebug("[Stats/Inventory] Error patching look/name in extracted actor details: " + ex.Message);
                            }
                            
                            if (GameState.PlayerActorDetails == null)
                            {
                                GameState.PlayerActorDetails = detailsBytes;
                            }
                            Program.LogDebug($"[Stats/Inventory] Successfully extracted active player actor details (Field 2) for Character ID {GameState.CharacterId} (details length: {detailsBytes.Length} bytes).");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug("[Stats/Inventory] Error extracting player actor details: " + ex.Message);
            }
        }

        private static byte[] SynchronizeIcwUids(byte[] icwPayload)
        {
            try
            {
                var rootMsg = ProtoMessage.Parse(icwPayload);
                var rootField = rootMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 2);
                if (rootField == null) return icwPayload;

                var wrapperMsg = ProtoMessage.Parse(rootField.BytesValue);
                var wrapperField = wrapperMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                if (wrapperField == null) return icwPayload;

                var anyMsg = ProtoMessage.Parse(wrapperField.BytesValue);
                var anyValueField = anyMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                if (anyValueField == null) return icwPayload;

                var icwMsg = ProtoMessage.Parse(anyValueField.BytesValue);
                
                int updatedCount = 0;
                foreach (var field in icwMsg.Fields)
                {
                    if (field.FieldNumber == 1 && field.WireType == 2)
                    {
                        var lifMsg = ProtoMessage.Parse(field.BytesValue);
                        var gidField = lifMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 0);
                        var uidField = lifMsg.Fields.FirstOrDefault(f => f.FieldNumber == 5 && f.WireType == 0);
                        
                        if (gidField != null && uidField != null)
                        {
                            int oldGid = (int)gidField.VarIntValue;
                            if (OldGidToNewGidAndUid.TryGetValue(oldGid, out var mapping))
                            {
                                long oldUid = uidField.VarIntValue;
                                gidField.VarIntValue = mapping.OfficialGid;
                                uidField.VarIntValue = mapping.ClientUid;
                                field.BytesValue = lifMsg.ToByteArray();
                                updatedCount++;
                                Program.LogDebug($"[Inventory Sync] Synchronized Old GID {oldGid} -> New GID {mapping.OfficialGid} | Old UID {oldUid} -> New UID {mapping.ClientUid}");
                            }
                        }
                    }
                }
                
                if (updatedCount > 0)
                {
                    anyValueField.BytesValue = icwMsg.ToByteArray();
                    wrapperField.BytesValue = anyMsg.ToByteArray();
                    rootField.BytesValue = wrapperMsg.ToByteArray();
                    return rootMsg.ToByteArray();
                }
            }
            catch (Exception ex)
            {
                Program.LogDebug($"[-] Error in SynchronizeIcwUids: {ex.Message}");
            }
            return icwPayload;
        }

        private static void InitializeStatsFromOriginalKri()
        {
            if (originalKriPayload == null) return;
            try
            {
                var rootMsg = ProtoMessage.Parse(originalKriPayload);
                var rootField = rootMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 2);
                if (rootField == null) return;

                var wrapperMsg = ProtoMessage.Parse(rootField.BytesValue);
                var wrapperField = wrapperMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                if (wrapperField == null) return;

                var anyMsg = ProtoMessage.Parse(wrapperField.BytesValue);
                var anyValueField = anyMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                if (anyValueField == null) return;

                var kriMsg = ProtoMessage.Parse(anyValueField.BytesValue);
                var larField = kriMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                if (larField == null) return;

                var larMsg = ProtoMessage.Parse(larField.BytesValue);
                var remainingField = larMsg.Fields.FirstOrDefault(f => f.FieldNumber == 7 && f.WireType == 0);
                if (remainingField != null)
                {
                    GameState.CharacterRemainingPoints = (int)remainingField.VarIntValue;
                    Console.WriteLine($"[Stats Init] Loaded Remaining Points (capital): {GameState.CharacterRemainingPoints}");
                }

                foreach (var field in larMsg.Fields)
                {
                    if (field.FieldNumber == 3 && field.WireType == 2)
                    {
                        var statMsg = ProtoMessage.Parse(field.BytesValue);
                        var statIdField = statMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 0);
                        if (statIdField != null)
                        {
                            int statId = (int)statIdField.VarIntValue;
                            var baseValField = statMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 0);
                            int baseVal = 0;
                            if (baseValField != null)
                            {
                                baseVal = (int)baseValField.VarIntValue;
                            }
                            
                            if (statId == 11) { GameState.StatVitality = baseVal; Console.WriteLine($"[Stats Init] Vitality = {baseVal}"); }
                            else if (statId == 12) { GameState.StatWisdom = baseVal; Console.WriteLine($"[Stats Init] Wisdom = {baseVal}"); }
                            else if (statId == 10) { GameState.StatStrength = baseVal; Console.WriteLine($"[Stats Init] Strength = {baseVal}"); }
                            else if (statId == 15) { GameState.StatIntelligence = baseVal; Console.WriteLine($"[Stats Init] Intelligence = {baseVal}"); }
                            else if (statId == 13) { GameState.StatChance = baseVal; Console.WriteLine($"[Stats Init] Chance = {baseVal}"); }
                            else if (statId == 14) { GameState.StatAgility = baseVal; Console.WriteLine($"[Stats Init] Agility = {baseVal}"); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error in InitializeStatsFromOriginalKri: {ex.Message}");
            }
        }

        public static byte[]? BuildUpdatedImdPacket(long itemUid, int newPosition)
        {
            if (originalImdPayload == null)
            {
                Console.WriteLine("[-] Error: originalImdPayload is null!");
                return null;
            }

            try
            {
                var rootMsg = ProtoMessage.Parse(originalImdPayload);
                var rootField = rootMsg.Fields.FirstOrDefault(f => f.FieldNumber == 3 && f.WireType == 2);
                if (rootField == null) return null;

                var wrapperMsg = ProtoMessage.Parse(rootField.BytesValue);
                var wrapperField = wrapperMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                if (wrapperField == null) return null;

                var anyMsg = ProtoMessage.Parse(wrapperField.BytesValue);
                var anyValueField = anyMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 2);
                if (anyValueField == null) return null;

                var icwMsg = ProtoMessage.Parse(anyValueField.BytesValue);

                bool found = false;
                foreach (var field in icwMsg.Fields)
                {
                    if (field.FieldNumber == 1 && field.WireType == 2)
                    {
                        var lifMsg = ProtoMessage.Parse(field.BytesValue);
                        var uidField = lifMsg.Fields.FirstOrDefault(f => f.FieldNumber == 5 && f.WireType == 0);
                        if (uidField != null && uidField.VarIntValue == itemUid)
                        {
                            var lktField = lifMsg.Fields.FirstOrDefault(f => f.FieldNumber == 1 && f.WireType == 2);
                            if (lktField != null)
                            {
                                var lktMsg = ProtoMessage.Parse(lktField.BytesValue);
                                var positionField = lktMsg.Fields.FirstOrDefault(f => f.FieldNumber == 2 && f.WireType == 0);
                                if (positionField != null)
                                {
                                    positionField.VarIntValue = newPosition;
                                }
                                else
                                {
                                    lktMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 0, VarIntValue = newPosition });
                                }
                                lktField.BytesValue = lktMsg.ToByteArray();
                            }
                            else
                            {
                                var lktMsg = new ProtoMessage();
                                lktMsg.Fields.Add(new ProtoField { FieldNumber = 1, WireType = 0, VarIntValue = 1 });
                                lktMsg.Fields.Add(new ProtoField { FieldNumber = 2, WireType = 0, VarIntValue = newPosition });
                                lifMsg.Fields.Add(new ProtoField { FieldNumber = 1, WireType = 2, BytesValue = lktMsg.ToByteArray() });
                            }

                            field.BytesValue = lifMsg.ToByteArray();
                            found = true;
                            Console.WriteLine($"[Inventory] Updated Item UID {itemUid} in icw payload to position {newPosition}.");
                            break;
                        }
                    }
                }

                if (!found)
                {
                    Console.WriteLine($"[-] Warning: Item UID {itemUid} not found in icw payload!");
                    return null;
                }

                anyValueField.BytesValue = icwMsg.ToByteArray();
                wrapperField.BytesValue = anyMsg.ToByteArray();
                rootField.BytesValue = wrapperMsg.ToByteArray();

                originalImdPayload = rootMsg.ToByteArray();
                return originalImdPayload;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error building updated imd/icw: {ex.Message}");
                return null;
            }
        }
    }
}
