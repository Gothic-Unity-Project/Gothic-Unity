using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Gothic's portal rooms (huts, houses): the world's BSP sectors. Scripts give a room to a guild with
    /// Wld_AssignRoomToGuild (G1 INIT_SUB_* - runs on every world load, nothing to save), PERC_ASSESSENTERROOM and
    /// Wld_GetPlayerPortalGuild tell NPCs whose room the hero walked into. Like OpenGothic, a position's room is the
    /// sector of the world polygons around it.
    /// </summary>
    public class RoomService
    {
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly ConfigService _configService;

        /// <summary>
        /// Grid size (m) of the sector lookup.
        /// </summary>
        public const float CellSize = 1f;

        /// <summary>
        /// Grid size (m) of the room floor lookup - finer, a door's step outside must not count.
        /// </summary>
        public const float FloorCellSize = 0.5f;

        private const int _guildNone = 0;

        private Dictionary<long, int> _sectorCells = new();
        private Dictionary<long, int> _floorCells = new();
        private readonly Dictionary<string, int> _roomGuilds = new(StringComparer.OrdinalIgnoreCase);
        private List<string> _sectorNames;

        public string HeroRoom { get; private set; } = string.Empty;
        public string HeroFormerRoom { get; private set; } = string.Empty;

        public static long ToCell(Vector3 position)
        {
            return ToCell(position, CellSize);
        }

        public static long ToFloorCell(Vector3 position)
        {
            return ToCell(position, FloorCellSize);
        }

        private static long ToCell(Vector3 position, float size)
        {
            var x = (long)Mathf.FloorToInt(position.x / size) & 0x1FFFFF;
            var y = (long)Mathf.FloorToInt(position.y / size) & 0x1FFFFF;
            var z = (long)Mathf.FloorToInt(position.z / size) & 0x1FFFFF;
            return (x << 42) | (y << 21) | z;
        }

        /// <summary>
        /// Cells (ToCell) of the indoor world polygons and their sector. Set by the world mesh builder on every world load.
        /// </summary>
        public void SetSectorCells(Dictionary<long, int> cells, Dictionary<long, int> floorCells)
        {
            _sectorCells = cells ?? new Dictionary<long, int>();
            _floorCells = floorCells ?? new Dictionary<long, int>();
            HeroRoom = string.Empty;
            HeroFormerRoom = string.Empty;
            Logger.Log($"[Rooms] {_sectorCells.Count} indoor cells, {_floorCells.Count} room floor cells", LogCat.Vob);
        }

        /// <summary>
        /// Per world material: the BSP sector its polygons belong to, -1 for none. Like OpenGothic, a room's materials
        /// are named "X:ROOM_..." - the part after ':' (up to '_', or all of it) is the sector's name.
        /// </summary>
        public int[] GetMaterialSectors(ZenKit.IMesh mesh)
        {
            _sectorNames = null;
            var names = GetSectorNames();
            var sectorByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Count; i++)
                sectorByName.TryAdd(names[i], i);

            var result = new int[mesh.MaterialCount];
            var matched = 0;
            var samples = new List<string>();
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = -1;
                var name = mesh.GetMaterial(i).Name;
                var colon = name?.IndexOf(':') ?? -1;
                if (colon < 0)
                    continue;

                if (samples.Count < 8)
                    samples.Add(name);

                var afterColon = name.Substring(colon + 1);
                var underscore = afterColon.IndexOf('_');
                if (sectorByName.TryGetValue(afterColon, out var sector) ||
                    (underscore > 0 && sectorByName.TryGetValue(afterColon.Substring(0, underscore), out sector)))
                {
                    result[i] = sector;
                    matched++;
                }
            }

            Logger.Log($"[Rooms] {names.Count} BSP sectors, {matched} room materials; e.g. " +
                       $"{string.Join(", ", samples)} / sectors {string.Join(", ", names.GetRange(0, Math.Min(8, names.Count)))}",
                LogCat.Vob);
            return result;
        }

        public void AssignRoomToGuild(string room, int guild)
        {
            if (string.IsNullOrEmpty(room))
                return;
            _roomGuilds[room] = guild;
        }

        /// <summary>
        /// The room (sector name) at a feet position, empty outdoors.
        /// </summary>
        public string GetRoomAt(Vector3 feetPosition)
        {
            // DeveloperConfig.EnableFloorRoomLookup: like OpenGothic (MoveAlgo: the sector of the polygon the ground ray
            // hits) - the room floor right under the feet. Walls and roofs don't count, platforms overhead neither.
            if (_configService.Dev.EnableFloorRoomLookup && _floorCells.Count > 0)
            {
                foreach (var height in new[] { 0.1f, -0.2f, -0.45f })
                {
                    if (_floorCells.TryGetValue(ToFloorCell(feetPosition + Vector3.up * height), out var floorSector))
                        return GetSectorName(floorSector);
                }
                return string.Empty;
            }

            if (_sectorCells.Count == 0)
                return string.Empty;

            // Floor, body and ceiling around the feet - a hut is a sector of floor, wall and roof polygons.
            foreach (var height in new[] { 0.2f, 1f, -0.5f, 2f })
            {
                if (_sectorCells.TryGetValue(ToCell(feetPosition + Vector3.up * height), out var sector))
                    return GetSectorName(sector);
            }
            return string.Empty;
        }

        public int GuildOfRoom(string room)
        {
            if (string.IsNullOrEmpty(room))
                return _guildNone;
            return _roomGuilds.TryGetValue(room, out var guild) ? guild : _guildNone;
        }

        /// <summary>
        /// True when the hero walked into another room (or out of one).
        /// </summary>
        public bool UpdateHeroRoom(Vector3 feetPosition)
        {
            var room = GetRoomAt(feetPosition);
            if (room.Equals(HeroRoom, StringComparison.OrdinalIgnoreCase))
                return false;

            HeroFormerRoom = HeroRoom;
            HeroRoom = room;
            Logger.Log($"[Rooms] Hero: '{HeroFormerRoom}' -> '{HeroRoom}' (guild {GuildOfRoom(HeroRoom)})", LogCat.Vob);
            return true;
        }

        private string GetSectorName(int sector)
        {
            var names = GetSectorNames();
            return sector >= 0 && sector < names.Count ? names[sector] : string.Empty;
        }

        private List<string> GetSectorNames()
        {
            if (_sectorNames == null)
            {
                _sectorNames = new List<string>();
                try
                {
                    var bsp = _saveGameService.CurrentWorldData?.BspTree;
                    if (bsp != null)
                        foreach (var s in bsp.Sectors)
                            _sectorNames.Add(s.Name ?? string.Empty);
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"[Rooms] No BSP sectors: {e.Message}", LogCat.Vob);
                }
            }

            return _sectorNames;
        }
    }
}
