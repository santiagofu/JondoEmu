using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Google.Protobuf;
using Jondo.Unity.Protocol.Messages;

namespace Jondo.Unity.Launcher
{
    public static class DatabaseManager
    {
        private static readonly string AuthConnectionString = "Data Source=auth.db";
        private static readonly string WorldConnectionString = "Data Source=world.db";

        public static void Initialize()
        {
            Console.WriteLine("[SQLite] Initializing databases...");
            
            // 1. Initialize auth.db
            using (var authConnection = new SqliteConnection(AuthConnectionString))
            {
                authConnection.Open();
                
                using (var pragmaCmd = authConnection.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                    pragmaCmd.ExecuteNonQuery();
                }
                
                var createAccounts = authConnection.CreateCommand();
                createAccounts.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Accounts (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Login TEXT NOT NULL UNIQUE,
                        Password TEXT NOT NULL,
                        Nickname TEXT NOT NULL,
                        GameToken TEXT
                    );
                ";
                createAccounts.ExecuteNonQuery();

                // Seed default account if empty
                var seedAccount = authConnection.CreateCommand();
                seedAccount.CommandText = @"
                    INSERT OR IGNORE INTO Accounts (Id, Login, Password, Nickname)
                    VALUES (188940901, 'jondo@emulator.com', 'password123', 'Jondo');
                ";
                seedAccount.ExecuteNonQuery();
            }

            // 2. Initialize world.db
            using (var worldConnection = new SqliteConnection(WorldConnectionString))
            {
                worldConnection.Open();

                using (var pragmaCmd = worldConnection.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                    pragmaCmd.ExecuteNonQuery();
                }

                var createCharacters = worldConnection.CreateCommand();
                createCharacters.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Characters (
                        Id INTEGER PRIMARY KEY,
                        AccountId INTEGER NOT NULL,
                        Name TEXT NOT NULL,
                        Breed INTEGER NOT NULL,
                        Sex INTEGER NOT NULL,
                        Level INTEGER NOT NULL DEFAULT 1,
                        MapId INTEGER NOT NULL DEFAULT 154010884,
                        CellId INTEGER NOT NULL DEFAULT 315,
                        RemainingPoints INTEGER NOT NULL DEFAULT 0,
                        Vitality INTEGER NOT NULL DEFAULT 0,
                        Wisdom INTEGER NOT NULL DEFAULT 0,
                        Strength INTEGER NOT NULL DEFAULT 0,
                        Intelligence INTEGER NOT NULL DEFAULT 0,
                        Chance INTEGER NOT NULL DEFAULT 0,
                        Agility INTEGER NOT NULL DEFAULT 0,
                        Look TEXT NOT NULL,
                        Orientation INTEGER NOT NULL DEFAULT 1
                    );
                ";
                createCharacters.ExecuteNonQuery();

                // Migration: Ensure Orientation column exists
                try
                {
                    var addColCmd = worldConnection.CreateCommand();
                    addColCmd.CommandText = "ALTER TABLE Characters ADD COLUMN Orientation INTEGER NOT NULL DEFAULT 1;";
                    addColCmd.ExecuteNonQuery();
                    Console.WriteLine("[SQLite] Added Orientation column to Characters table.");
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                    // Column already exists, ignore
                }

                var createItems = worldConnection.CreateCommand();
                createItems.CommandText = @"
                    CREATE TABLE IF NOT EXISTS CharacterItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        CharacterId INTEGER NOT NULL,
                        Uid INTEGER NOT NULL,
                        Gid INTEGER NOT NULL,
                        Quantity INTEGER NOT NULL DEFAULT 1,
                        Position INTEGER NOT NULL DEFAULT 63
                    );
                ";
                createItems.ExecuteNonQuery();

                // Seed default character if empty
                var checkChar = worldConnection.CreateCommand();
                checkChar.CommandText = "SELECT COUNT(*) FROM Characters WHERE Id = 13825558;";
                long count = (long)checkChar.ExecuteScalar();
                if (count == 0)
                {
                    var seedChar = worldConnection.CreateCommand();
                    seedChar.CommandText = @"
                        INSERT INTO Characters (
                            Id, AccountId, Name, Breed, Sex, Level, MapId, CellId, 
                            RemainingPoints, Vitality, Wisdom, Strength, Intelligence, Chance, Agility, Look
                        ) VALUES (
                            13825558, 188940901, $name, 8, 1, 2, 154011397, 386, 
                            5, 0, 0, 0, 0, 0, 0, $look
                        );
                    ";
                    seedChar.Parameters.AddWithValue("$name", "CADERNIS");
                    seedChar.Parameters.AddWithValue("$look", "080118032218A28B9B0FCBE5F615A4E1B91992A6C820888CA028F5B7CB342A035BE410420134320220013809");
                    seedChar.ExecuteNonQuery();
                    Console.WriteLine("[SQLite] Seeded default character CADERNIS.");
                }
                else
                {
                    // Migration: Update name if it is "[#CADERNIS#]" or "#CADERNIS#"
                    using (var updateCmd = worldConnection.CreateCommand())
                    {
                        updateCmd.CommandText = "UPDATE Characters SET Name = 'CADERNIS' WHERE Name = '[#CADERNIS#]' OR Name = '#CADERNIS#';";
                        int affected = updateCmd.ExecuteNonQuery();
                        if (affected > 0)
                        {
                            Console.WriteLine("[SQLite] Migration: Updated character name to 'CADERNIS'.");
                        }
                    }
                }
            }

            Console.WriteLine("[SQLite] Databases initialized successfully.");
        }

        // --- Auth Operations ---

        public static void SetGameToken(long accountId, string token)
        {
            using var connection = new SqliteConnection(AuthConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Accounts
                SET GameToken = $token
                WHERE Id = $id;
            ";
            command.Parameters.AddWithValue("$token", token);
            command.Parameters.AddWithValue("$id", accountId);
            command.ExecuteNonQuery();
        }

        public static bool ValidateGameToken(string token)
        {
            using var connection = new SqliteConnection(AuthConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM Accounts
                WHERE GameToken = $token;
            ";
            command.Parameters.AddWithValue("$token", token);
            return (long)command.ExecuteScalar() > 0;
        }

        public static long GetAccountIdByToken(string token)
        {
            using var connection = new SqliteConnection(AuthConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id FROM Accounts WHERE GameToken = $token;";
            command.Parameters.AddWithValue("$token", token);
            var result = command.ExecuteScalar();
            return result != null ? (long)result : 0;
        }

        // --- Character Operations ---

        public class DbCharacter
        {
            public long Id { get; set; }
            public string Name { get; set; }
            public int Breed { get; set; }
            public int Sex { get; set; }
            public int Level { get; set; }
            public string LookHex { get; set; }
        }

        public static List<DbCharacter> GetCharactersByAccountId(long accountId)
        {
            var list = new List<DbCharacter>();
            using var connection = new SqliteConnection(WorldConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, Breed, Sex, Level, Look FROM Characters WHERE AccountId = $accId;";
            command.Parameters.AddWithValue("$accId", accountId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new DbCharacter
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Breed = reader.GetInt32(2),
                    Sex = reader.GetInt32(3),
                    Level = reader.GetInt32(4),
                    LookHex = reader.GetString(5)
                });
            }
            return list;
        }

        public static bool LoadCharacter(long characterId)
        {
            using var connection = new SqliteConnection(WorldConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Name, Level, MapId, CellId, RemainingPoints, Vitality, Wisdom, Strength, Intelligence, Chance, Agility, Look, Breed, Sex, Orientation
                FROM Characters
                WHERE Id = $charId;
            ";
            command.Parameters.AddWithValue("$charId", characterId);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                GameState.CharacterId = characterId;
                GameState.CharacterName = reader.GetString(0);
                GameState.CharacterLevel = reader.GetInt32(1);
                // Load actual position from the database
                GameState.MapId = reader.GetInt64(2);
                GameState.CellId = reader.GetInt32(3);
                GameState.Orientation = reader.IsDBNull(14) ? 1 : reader.GetInt32(14);
                GameState.CharacterRemainingPoints = reader.GetInt32(4);
                GameState.StatVitality = reader.GetInt32(5);
                GameState.StatWisdom = reader.GetInt32(6);
                GameState.StatStrength = reader.GetInt32(7);
                GameState.StatIntelligence = reader.GetInt32(8);
                GameState.StatChance = reader.GetInt32(9);
                GameState.StatAgility = reader.GetInt32(10);
                GameState.Breed = reader.GetInt32(12);
                GameState.Sex = reader.GetInt32(13);
                
                string lookHex = reader.GetString(11);
                byte[] lookBytes = ConvertHexStringToByteArray(lookHex);
                GameState.LookBytes = lookBytes;
                
                // Reconstruct PlayerActorDetails (detailsMsg with look and humanoid name)
                // detailsMsg has: Field 1 (Look), Field 2 (HumanoidMsg)
                // HumanoidMsg has: Field 2 (HumanInformationsMsg)
                // HumanInformationsMsg has: Field 3 (Name)
                GameState.PlayerActorDetails = ReconstructActorDetails(lookBytes, GameState.CharacterName);
                
                Console.WriteLine($"[SQLite] Successfully loaded character: {GameState.CharacterName} (Level {GameState.CharacterLevel})");
                return true;
            }
            return false;
        }

        public static void SaveCharacterStatsAndPosition(
            long characterId, int remainingPoints, int vitality, int wisdom, int strength, int intelligence, int chance, int agility, long mapId, int cellId, int orientation)
        {
            using var connection = new SqliteConnection(WorldConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Characters
                SET RemainingPoints = $rp, Vitality = $vit, Wisdom = $wis, Strength = $str, Intelligence = $intel, Chance = $cha, Agility = $agi, MapId = $map, CellId = $cell, Orientation = $orient
                WHERE Id = $id;
            ";
            command.Parameters.AddWithValue("$rp", remainingPoints);
            command.Parameters.AddWithValue("$vit", vitality);
            command.Parameters.AddWithValue("$wis", wisdom);
            command.Parameters.AddWithValue("$str", strength);
            command.Parameters.AddWithValue("$intel", intelligence);
            command.Parameters.AddWithValue("$cha", chance);
            command.Parameters.AddWithValue("$agi", agility);
            command.Parameters.AddWithValue("$map", mapId);
            command.Parameters.AddWithValue("$cell", cellId);
            command.Parameters.AddWithValue("$orient", orientation);
            command.Parameters.AddWithValue("$id", characterId);
            command.ExecuteNonQuery();
        }

        public static void SaveCharacterLook(long characterId, byte[] lookBytes)
        {
            using var connection = new SqliteConnection(WorldConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Characters SET Look = $look WHERE Id = $id;";
            command.Parameters.AddWithValue("$look", BitConverter.ToString(lookBytes).Replace("-", ""));
            command.Parameters.AddWithValue("$id", characterId);
            command.ExecuteNonQuery();
        }

        // --- Inventory Operations ---

        public static List<PlayerItem> LoadInventory(long characterId)
        {
            var list = new List<PlayerItem>();
            using var connection = new SqliteConnection(WorldConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Uid, Gid, Quantity, Position FROM CharacterItems WHERE CharacterId = $charId;";
            command.Parameters.AddWithValue("$charId", characterId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new PlayerItem
                {
                    Uid = reader.GetInt64(0),
                    ItemId = reader.GetInt32(1),
                    Quantity = reader.GetInt32(2),
                    Position = reader.GetInt32(3)
                });
            }
            return list;
        }

        public static void SaveInventoryItem(long characterId, PlayerItem item)
        {
            using var connection = new SqliteConnection(WorldConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO CharacterItems (CharacterId, Uid, Gid, Quantity, Position)
                VALUES ($charId, $uid, $gid, $qty, $pos)
                ON CONFLICT(Uid) DO UPDATE SET
                    Gid = $gid,
                    Quantity = $qty,
                    Position = $pos;
            ";
            // Note: Uid is UNIQUE in SQLite to support upsert. Let's make sure we create UNIQUE index on Uid if not exists!
            command.Parameters.AddWithValue("$charId", characterId);
            command.Parameters.AddWithValue("$uid", item.Uid);
            command.Parameters.AddWithValue("$gid", item.ItemId);
            command.Parameters.AddWithValue("$qty", item.Quantity);
            command.Parameters.AddWithValue("$pos", item.Position);
            command.ExecuteNonQuery();
        }

        public static void SaveItemPosition(long uid, int position)
        {
            using var connection = new SqliteConnection(WorldConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "UPDATE CharacterItems SET Position = $pos WHERE Uid = $uid;";
            command.Parameters.AddWithValue("$pos", position);
            command.Parameters.AddWithValue("$uid", uid);
            command.ExecuteNonQuery();
        }

        public static void ClearInventory(long characterId)
        {
            using var connection = new SqliteConnection(WorldConnectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM CharacterItems WHERE CharacterId = $charId;";
            command.Parameters.AddWithValue("$charId", characterId);
            command.ExecuteNonQuery();
        }

        public static void SeedInventory(long characterId, List<PlayerItem> items)
        {
            using var connection = new SqliteConnection(WorldConnectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Create UNIQUE index on Uid to support INSERT ... ON CONFLICT
                var createIndex = connection.CreateCommand();
                createIndex.Transaction = transaction;
                createIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_items_uid ON CharacterItems(Uid);";
                createIndex.ExecuteNonQuery();

                foreach (var item in items)
                {
                    var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT OR REPLACE INTO CharacterItems (CharacterId, Uid, Gid, Quantity, Position)
                        VALUES ($charId, $uid, $gid, $qty, $pos);
                    ";
                    command.Parameters.AddWithValue("$charId", characterId);
                    command.Parameters.AddWithValue("$uid", item.Uid);
                    command.Parameters.AddWithValue("$gid", item.ItemId);
                    command.Parameters.AddWithValue("$qty", item.Quantity);
                    command.Parameters.AddWithValue("$pos", item.Position);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
                Console.WriteLine($"[SQLite] Successfully seeded {items.Count} items into database for Character {characterId}.");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"[-] Error seeding inventory: {ex.Message}");
            }
        }

        // --- Helpers ---

        private static byte[] ConvertHexStringToByteArray(string hex)
        {
            hex = hex.Replace("-", "");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private static byte[] ReconstructActorDetails(byte[] lookBytes, string name)
        {
            byte[] rawDetails = Array.Empty<byte>();
            try
            {
                // 1. Build the social/account details (Field 2 of HumanInformations)
                var socialMsg = new Network.ProtoMessage();
                
                // Tag 1 (Sub-Message)
                var tag1Msg = new Network.ProtoMessage();
                tag1Msg.Fields.Add(new Network.ProtoField { FieldNumber = 2, WireType = 0, VarIntValue = 2 });
                tag1Msg.Fields.Add(new Network.ProtoField { FieldNumber = 4, WireType = 0, VarIntValue = 2 });
                socialMsg.Fields.Add(new Network.ProtoField { FieldNumber = 1, WireType = 2, BytesValue = tag1Msg.ToByteArray() });
                
                // Tag 2 (VarInt)
                socialMsg.Fields.Add(new Network.ProtoField { FieldNumber = 2, WireType = 0, VarIntValue = 1 });
                
                // Tag 3 (Bytes)
                socialMsg.Fields.Add(new Network.ProtoField { FieldNumber = 3, WireType = 2, BytesValue = new byte[] { 0x0B } });
                
                // Tag 4 (VarInt): Account ID (188940901)
                socialMsg.Fields.Add(new Network.ProtoField { FieldNumber = 4, WireType = 0, VarIntValue = 188940901 });
                
                // Tag 5 (Sub-Message)
                var tag5Msg = new Network.ProtoMessage();
                tag5Msg.Fields.Add(new Network.ProtoField { FieldNumber = 6, WireType = 0, VarIntValue = 1 });
                socialMsg.Fields.Add(new Network.ProtoField { FieldNumber = 5, WireType = 2, BytesValue = tag5Msg.ToByteArray() });
                
                // Tag 7 (VarInt)
                socialMsg.Fields.Add(new Network.ProtoField { FieldNumber = 7, WireType = 0, VarIntValue = 1 });

                // 2. Build HumanInformations (which goes to Field 2 inside HumanoidOption)
                // In Dofus 3.6, HumanInformations has:
                // - Field 2 (wire type 2): Social/Account details
                // - Field 3 (wire type 2): Character Name (string)
                var humanInfos = new Network.ProtoMessage();
                humanInfos.Fields.Add(new Network.ProtoField { FieldNumber = 2, WireType = 2, BytesValue = socialMsg.ToByteArray() });
                humanInfos.Fields.Add(new Network.ProtoField { FieldNumber = 3, WireType = 2, BytesValue = System.Text.Encoding.UTF8.GetBytes(name) });
                byte[] humanInfosBytes = humanInfos.ToByteArray();

                // 3. Build HumanoidOption (which goes to Field 2 inside root Details)
                // In Dofus 3.6, HumanoidOption has:
                // - Field 2 (wire type 2): HumanInformations
                var humanoidOption = new Network.ProtoMessage();
                humanoidOption.Fields.Add(new Network.ProtoField { FieldNumber = 2, WireType = 2, BytesValue = humanInfosBytes });
                byte[] humanoidOptionBytes = humanoidOption.ToByteArray();

                // 4. Build detailsMsg (GameRolePlayCharacterInformations, root Details message)
                // In Dofus 3.6, GameRolePlayCharacterInformations has:
                // - Field 1 (wire type 2): EntityLook
                // - Field 2 (wire type 2): HumanoidOption
                var detailsMsg = new Network.ProtoMessage();
                if (lookBytes != null && lookBytes.Length > 0)
                {
                    detailsMsg.Fields.Add(new Network.ProtoField { FieldNumber = 1, WireType = 2, BytesValue = lookBytes });
                }
                detailsMsg.Fields.Add(new Network.ProtoField { FieldNumber = 2, WireType = 2, BytesValue = humanoidOptionBytes });

                rawDetails = detailsMsg.ToByteArray();
                Console.WriteLine($"[ReconstructDetails] Rebuilt PCAP-compliant details for {name} (look length: {lookBytes?.Length ?? 0} bytes).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error in ReconstructActorDetails: {ex.Message}");
                return Array.Empty<byte>();
            }

            return rawDetails;
        }
    }
}
