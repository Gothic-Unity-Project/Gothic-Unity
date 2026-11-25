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
        // Below this normal.y a floor triangle is sloped: its cells also mark the buckets above and below.
        private const float _levelFloorNormalY = 0.98f;
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
        /// World load (background thread, only touches floorCells): marks the floor cells a room polygon covers with
        /// its sector if it is level - floors (and ceilings, never at the feet). A cell counts when its center lies in
        /// one of the polygon's triangles: the polygon's bounding box also took the street next to a slanted or
        /// L-shaped floor (G2 Khorinis: standing at Harad's forge counted as in his house, he sent the hero out).
        /// </summary>
        public static void MarkRoomFloor(ZenKit.IMesh mesh, int sector, IList<int> positionIndices,
            Dictionary<long, int> floorCells)
        {
            if (positionIndices.Count < 3)
                return;

            var corners = new Vector3[positionIndices.Count];
            for (var i = 0; i < corners.Length; i++)
            {
                var p = mesh.GetPosition(positionIndices[i]);
                corners[i] = new Vector3(p.X, p.Y, p.Z) / 100f;
            }

            var normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
            if (normal.sqrMagnitude <= 0f || Mathf.Abs(normal.normalized.y) <= _floorMinNormalY)
                return;

            // A slope changes the height within a cell: a standing point can land in the bucket above or below.
            var isSloped = Mathf.Abs(normal.normalized.y) < _levelFloorNormalY;

            var min = corners[0];
            var max = corners[0];
            foreach (var corner in corners)
            {
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner);
            }

            var minX = Mathf.FloorToInt(min.x / FloorCellSize);
            var minZ = Mathf.FloorToInt(min.z / FloorCellSize);
            var maxX = Mathf.Min(Mathf.FloorToInt(max.x / FloorCellSize), minX + _maxFloorCellsPerAxis);
            var maxZ = Mathf.Min(Mathf.FloorToInt(max.z / FloorCellSize), minZ + _maxFloorCellsPerAxis);
            for (var x = minX; x <= maxX; x++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    var center = new Vector2((x + 0.5f) * FloorCellSize, (z + 0.5f) * FloorCellSize);
                    // Polygons are convex fans around their first corner.
                    for (var i = 1; i + 1 < corners.Length; i++)
                    {
                        if (!TryGetHeightInTriangle(center, corners[0], corners[i], corners[i + 1], out var y))
                            continue;

                        floorCells[ToFloorCell(new Vector3(center.x, y, center.y))] = sector;
                        if (isSloped)
                        {
                            floorCells[ToFloorCell(new Vector3(center.x, y + FloorCellSize, center.y))] = sector;
                            floorCells[ToFloorCell(new Vector3(center.x, y - FloorCellSize, center.y))] = sector;
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The point (x, z) lies in the triangle seen from above: its height on the triangle's plane.
        /// </summary>
        private static bool TryGetHeightInTriangle(Vector2 point, Vector3 a, Vector3 b, Vector3 c, out float height)
        {
            height = 0f;
            var v0 = new Vector2(b.x - a.x, b.z - a.z);
            var v1 = new Vector2(c.x - a.x, c.z - a.z);
            var v2 = new Vector2(point.x - a.x, point.y - a.z);
            var denominator = v0.x * v1.y - v1.x * v0.y;
            if (Mathf.Abs(denominator) < 1e-6f)
                return false;

            var u = (v2.x * v1.y - v1.x * v2.y) / denominator;
            var v = (v0.x * v2.y - v2.x * v0.y) / denominator;
            if (u < 0f || v < 0f || u + v > 1f)
                return false;

            height = a.y + u * (b.y - a.y) + v * (c.y - a.y);
            return true;
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
