using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Jondo.Unity.Launcher
{
    public class MapInfo
    {
        public long MapId { get; set; }
        public int PosX { get; set; }
        public int PosY { get; set; }
        public int SubAreaId { get; set; }
        public bool Outdoor { get; set; }
        public string Name { get; set; }
    }

    public class MapScrollAction
    {
        public long MapId { get; set; }
        public long RightMapId { get; set; }
        public long BottomMapId { get; set; }
        public long LeftMapId { get; set; }
        public long TopMapId { get; set; }
    }

    public static class MapManager
    {
        public static Dictionary<long, MapInfo> Maps = new Dictionary<long, MapInfo>();
        public static Dictionary<long, MapScrollAction> ScrollActions = new Dictionary<long, MapScrollAction>();

        public static void Initialize()
        {
            string scrollsPath = @"C:\Jondo\map_dump_scrolls.csv";
            string infosPath = @"C:\Jondo\map_dump_infos.csv";

            if (!File.Exists(scrollsPath) || !File.Exists(infosPath))
            {
                Console.WriteLine("[MapManager] Warning: map_dump_scrolls.csv or map_dump_infos.csv not found! Map database will be empty until the client is launched once with the JondoFix mod.");
                return;
            }

            try
            {
                Maps.Clear();
                ScrollActions.Clear();

                // 1. Load Infos
                int infoCount = 0;
                var infoLines = File.ReadLines(infosPath);
                foreach (var line in infoLines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',');
                    if (parts.Length >= 5)
                    {
                        if (long.TryParse(parts[0], out long mapId) &&
                            int.TryParse(parts[1], out int posX) &&
                            int.TryParse(parts[2], out int posY) &&
                            int.TryParse(parts[3], out int subAreaId) &&
                            bool.TryParse(parts[4], out bool outdoor))
                        {
                            if (subAreaId == 444)
                            {
                                subAreaId = 20663;
                            }
                            string name = parts.Length > 5 ? parts[5] : "";
                            var info = new MapInfo
                            {
                                MapId = mapId,
                                PosX = posX,
                                PosY = posY,
                                SubAreaId = subAreaId,
                                Outdoor = outdoor,
                                Name = name
                            };
                            Maps[mapId] = info;
                            infoCount++;
                        }
                    }
                }
                Console.WriteLine($"[MapManager] Loaded {infoCount} map info records successfully.");

                // 2. Load Scrolls
                int scrollCount = 0;
                var scrollLines = File.ReadLines(scrollsPath);
                foreach (var line in scrollLines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',');
                    if (parts.Length >= 5)
                    {
                        if (long.TryParse(parts[0], out long mapId) &&
                            long.TryParse(parts[1], out long rightMapId) &&
                            long.TryParse(parts[2], out long bottomMapId) &&
                            long.TryParse(parts[3], out long leftMapId) &&
                            long.TryParse(parts[4], out long topMapId))
                        {
                            var action = new MapScrollAction
                            {
                                MapId = mapId,
                                RightMapId = rightMapId,
                                BottomMapId = bottomMapId,
                                LeftMapId = leftMapId,
                                TopMapId = topMapId
                            };
                            ScrollActions[mapId] = action;
                            scrollCount++;
                        }
                    }
                }
                Console.WriteLine($"[MapManager] Loaded {scrollCount} map scroll action records successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MapManager] Error loading map CSVs: {ex.Message}");
            }
        }

        public static MapInfo GetMapInfo(long mapId)
        {
            if (Maps.TryGetValue(mapId, out var info))
            {
                return info;
            }
            return null;
        }

        public static MapScrollAction GetScrollAction(long mapId)
        {
            if (ScrollActions.TryGetValue(mapId, out var action))
            {
                return action;
            }
            return null;
        }
    }
}
