using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Gothic's portal rooms (huts, houses): the world's BSP sectors. Scripts give a room to a guild with
    /// Wld_AssignRoomToGuild (G1 INIT_SUB_*, from the world's INIT_ on a new game - a loaded save doesn't run it yet,
    /// see the save/load follow-up), PERC_ASSESSENTERROOM and
    /// Wld_GetPlayerPortalGuild tell NPCs whose room the hero walked into. Like OpenGothic (MoveAlgo: the sector of the
    /// polygon the ground ray hits), a position's room is the room floor right under the feet.
    /// </summary>
    public class RoomService
    {
        [Inject] private readonly SaveGameService _saveGameService;

        /// <summary>
        /// Grid size (m) of the room floor lookup - fine, a door's step outside must not count.
        /// </summary>
        public const float FloorCellSize = 0.5f;

        // A room floor polygon: its normal at most ~45 degrees from up/down.
        private const float _floorMinNormalY = 0.7f;
        // Huge floor polygons are marked up to 20 m per axis.
        private const int _maxFloorCellsPerAxis = 40;

        private const int _guildNone = 0;
        // Npc_GetPortalGuild outdoors: below GIL_NONE - G2's B_AssessEnterRoom took a guard outside (0) for one in
        // another room, nobody came when the hero walked into a house that isn't his.
        private const int _outdoorPortalGuild = -1;

        // Heights around the feet the room floor is looked for.
        private static readonly float[] _floorProbeHeights = { 0.1f, -0.2f, -0.45f };

        private Dictionary<long, int> _floorCells = new();
        private readonly Dictionary<string, int> _roomGuilds = new(StringComparer.OrdinalIgnoreCase);
        private List<string> _sectorNames;

        public string HeroRoom { get; private set; } = string.Empty;
        public string HeroFormerRoom { get; private set; } = string.Empty;

        public static long ToFloorCell(Vector3 position)
        {
            var x = (long)Mathf.FloorToInt(position.x / FloorCellSize) & 0x1FFFFF;
            var y = (long)Mathf.FloorToInt(position.y / FloorCellSize) & 0x1FFFFF;
            var z = (long)Mathf.FloorToInt(position.z / FloorCellSize) & 0x1FFFFF;
            return (x << 42) | (y << 21) | z;
        }

        /// <summary>
        /// World load (background thread, only touches floorCells): marks the floor cells a room polygon covers with its
        /// sector if it is level - floors (and ceilings, never at the feet). Bounding box of the polygon.
        /// </summary>
        public static void MarkRoomFloor(ZenKit.IMesh mesh, int sector, IList<int> positionIndices,
            Dictionary<long, int> floorCells)
        {
            if (positionIndices.Count < 3)
                return;

            var a = mesh.GetPosition(positionIndices[0]);
            var b = mesh.GetPosition(positionIndices[1]);
            var c = mesh.GetPosition(positionIndices[2]);
            var normal = Vector3.Cross(new Vector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z),
                new Vector3(c.X - a.X, c.Y - a.Y, c.Z - a.Z));
            if (normal.sqrMagnitude <= 0f || Mathf.Abs(normal.normalized.y) <= _floorMinNormalY)
                return;

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var index in positionIndices)
            {
                var p = mesh.GetPosition(index);
                var position = new Vector3(p.X, p.Y, p.Z) / 100f;
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var steps = Vector3Int.Min(Vector3Int.CeilToInt((max - min) / FloorCellSize),
                Vector3Int.one * _maxFloorCellsPerAxis);
            for (var x = 0; x <= steps.x; x++)
                for (var y = 0; y <= steps.y; y++)
                    for (var z = 0; z <= steps.z; z++)
                        floorCells[ToFloorCell(min + Vector3.Min(new Vector3(x, y, z) * FloorCellSize, max - min))] =
                            sector;
        }

        /// <summary>
        /// Floor cells (ToFloorCell) of the rooms and their sector. Set by the world mesh builder on every world load.
        /// </summary>
        public void SetFloorCells(Dictionary<long, int> floorCells)
        {
            _floorCells = floorCells ?? new Dictionary<long, int>();
            HeroRoom = string.Empty;
            HeroFormerRoom = string.Empty;
            Logger.Log($"[Rooms] {_floorCells.Count} room floor cells", LogCat.Vob);
        }

        /// <summary>
        /// Per world material: the BSP sector its polygons belong to, -1 for none. Like OpenGothic, a room's materials
        /// are named "X:ROOM_..." - the longest part after ':' (all of it or up to a '_') that names a sector.
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
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = -1;
                var name = mesh.GetMaterial(i).Name;
                var colon = name?.IndexOf(':') ?? -1;
                if (colon < 0)
                    continue;

                // The longest sector name the material name starts with: "S:OC_KITCHEN_WOOD" -> OC_KITCHEN.
                var afterColon = name.Substring(colon + 1);
                var prefix = afterColon;
                while (true)
                {
                    if (sectorByName.TryGetValue(prefix, out var sector))
                    {
                        result[i] = sector;
                        matched++;
                        break;
                    }

                    var underscore = prefix.LastIndexOf('_');
                    if (underscore <= 0)
                        break;
                    prefix = prefix.Substring(0, underscore);
                }
            }

            Logger.Log($"[Rooms] {names.Count} BSP sectors, {matched} room materials", LogCat.Vob);
            return result;
        }

        public void AssignRoomToGuild(string room, int guild)
        {
            if (string.IsNullOrEmpty(room))
                return;
            _roomGuilds[room] = guild;
        }

        /// <summary>
        /// The room (sector name) at a feet position, empty outdoors. Walls and roofs don't count, platforms overhead
        /// neither.
        /// </summary>
        public string GetRoomAt(Vector3 feetPosition)
        {
            foreach (var height in _floorProbeHeights)
            {
                if (_floorCells.TryGetValue(ToFloorCell(feetPosition + Vector3.up * height), out var floorSector))
                    return GetSectorName(floorSector);
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
        /// Npc_GetPortalGuild: the guild of the room at the position, below GIL_NONE outdoors.
        /// </summary>
        public int GetPortalGuildAt(Vector3 feetPosition)
        {
            var room = GetRoomAt(feetPosition);
            return string.IsNullOrEmpty(room) ? _outdoorPortalGuild : GuildOfRoom(room);
        }

        public bool IsInHeroRoom(Vector3 feetPosition)
        {
            return GetRoomAt(feetPosition).Equals(HeroRoom, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The hero walked into another room (or out of one). Only HeroPerceptionService calls it - it decides when a
        /// change counts (doorstep delay); NPC lookups (GetRoomAt) answer right away.
        /// </summary>
        public void SetHeroRoom(string room)
        {
            HeroFormerRoom = HeroRoom;
            HeroRoom = room;
            Logger.Log($"[Rooms] Hero: '{HeroFormerRoom}' -> '{HeroRoom}' (guild {GuildOfRoom(HeroRoom)})", LogCat.Vob);
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
