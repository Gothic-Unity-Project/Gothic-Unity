using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Vector3 = UnityEngine.Vector3;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Gothic's rooms (huts, houses, castle halls) are the world's BSP sectors. Like the engine (OpenGothic MoveAlgo:
    /// the sector of the ground polygon under the feet), the room of a position is the sector of the floor below it -
    /// outdoors there is none. A room's polygons have materials named "S:ROOM_TEXTURE". The scripts give rooms to guilds (Wld_AssignRoomToGuild, run by every world's startup,
    /// so nothing is saved). When the hero walks into another room or out of one, the NPCs around perceive
    /// PERC_ASSESSENTERROOM, which makes owners chase intruders out.
    /// </summary>
    public class RoomService
    {
        // Wld_GetPlayerPortalGuild outdoors, like OpenGothic's guildOfRoom without a room.
        public const int NoRoomGuild = -1;

        private const float _cellSize = 0.5f;
        private const float _heroCheckSeconds = 0.25f;
        // A floor polygon counts for feet up to this far above it (stairs, uneven floors) or below it.
        private const float _floorReachAbove = 0.6f;
        private const float _floorReachBelow = 0.3f;
        // A polygon is floor if its normal points this much up (or down: winding differs).
        private const float _floorNormalY = 0.6f;
        private const int _guildNone = 0;

        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        // NpcAiService leads back to this service through its own dependencies - resolved on first use.
        private NpcAiService _npcAiService;
        private NpcAiService NpcAi => _npcAiService ??= ReflexProjectInstaller.DIContainer.Resolve<NpcAiService>();

        private readonly Dictionary<string, int> _roomGuilds = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<long, List<(int Sector, float MinY, float MaxY)>> _floorCells;
        private List<string> _sectorNames;
        private Coroutine _heroTracking;

        public string HeroRoom { get; private set; } = string.Empty;
        public string HeroFormerRoom { get; private set; } = string.Empty;

        public RoomService()
        {
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldLoaded);
        }

        public void AssignRoomToGuild(string room, int guild)
        {
            if (!string.IsNullOrEmpty(room))
                _roomGuilds[room] = guild;
        }

        /// <summary>
        /// The guild owning the room: GIL_NONE for a room nobody owns, NoRoomGuild outdoors.
        /// </summary>
        public int GetGuildOfRoom(string room)
        {
            if (string.IsNullOrEmpty(room))
                return NoRoomGuild;

            return _roomGuilds.TryGetValue(room, out var guild) ? guild : _guildNone;
        }

        /// <summary>
        /// The room (sector name) of the floor below these feet, empty outdoors.
        /// </summary>
        public string GetRoomAt(Vector3 feet)
        {
            BuildFloorCells();
            if (!_floorCells.TryGetValue(ToCell(feet.x, feet.z), out var floors))
                return string.Empty;

            var bestSector = -1;
            var bestHeight = float.MinValue;
            foreach (var (sector, minY, maxY) in floors)
            {
                // The highest floor the feet stand on, not one of a storey above.
                if (feet.y < minY - _floorReachBelow || feet.y > maxY + _floorReachAbove || maxY <= bestHeight)
                    continue;

                bestSector = sector;
                bestHeight = maxY;
            }

            return bestSector >= 0 && bestSector < _sectorNames.Count ? _sectorNames[bestSector] : string.Empty;
        }

        public string GetRoomOf(NpcContainer npc)
        {
            return npc?.Go != null ? GetRoomAt(npc.Go.transform.position) : string.Empty;
        }

        private void OnWorldLoaded()
        {
            _floorCells = null;
            _sectorNames = null;
            HeroRoom = string.Empty;
            HeroFormerRoom = string.Empty;
            _heroTracking ??= _unityMonoService.StartCoroutine(TrackHero());
        }

        private IEnumerator TrackHero()
        {
            var wait = new WaitForSeconds(_heroCheckSeconds);
            while (true)
            {
                yield return wait;

                var hero = (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
                if (hero?.Go == null || _saveGameService.CurrentWorldName == null)
                    continue;

                var room = GetRoomAt(hero.Go.transform.position);
                if (room.EqualsIgnoreCase(HeroRoom))
                    continue;

                HeroFormerRoom = HeroRoom;
                HeroRoom = room;
                Logger.Log($"[Rooms] The hero went from '{HeroFormerRoom}' to '{HeroRoom}' " +
                           $"(guild {GetGuildOfRoom(HeroRoom)})", LogCat.Npc);

                // Like the engine, a sneaking hero isn't noticed.
                if (hero.Props.BodyState != VmGothicEnums.BodyState.BsSneak)
                    NpcAi.Npc_SendPassivePerc(hero.Instance, VmGothicEnums.PerceptionType.AssessEnterRoom,
                        hero.Instance, hero.Instance);
            }
        }

        /// <summary>
        /// Every floor polygon of a room into a grid of cells, with its height range. Built once per world, on first
        /// use (the world mesh is already loaded then).
        /// </summary>
        private void BuildFloorCells()
        {
            if (_floorCells != null)
                return;

            _floorCells = new Dictionary<long, List<(int, float, float)>>();
            _sectorNames = new List<string>();
            var world = _saveGameService.CurrentWorldName != null ? _saveGameService.CurrentWorldData : null;
            if (world?.Mesh == null || world.BspTree == null)
                return;

            var stopwatch = Stopwatch.StartNew();
            var sectorByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var sector in world.BspTree.Sectors)
            {
                sectorByName.TryAdd(sector.Name ?? string.Empty, _sectorNames.Count);
                _sectorNames.Add(sector.Name ?? string.Empty);
            }

            var mesh = world.Mesh;
            var materialSectors = GetMaterialSectors(mesh, sectorByName);
            var positions = mesh.Positions;
            var floorCount = 0;
            for (var i = 0; i < mesh.PolygonCount; i++)
            {
                var polygon = mesh.GetPolygon(i);
                var sector = polygon.IsSector ? materialSectors[polygon.MaterialIndex] : -1;
                if (sector < 0)
                    continue;

                var indices = polygon.PositionIndices;
                if (indices.Count < 3 || !IsFloor(positions, indices))
                    continue;

                AddFloor(sector, positions, indices);
                floorCount++;
            }

            Logger.Log($"[Rooms] {_sectorNames.Count} rooms, {floorCount} floor polygons in {_floorCells.Count} cells " +
                       $"({stopwatch.ElapsedMilliseconds} ms)", LogCat.Npc);
        }

        /// <summary>
        /// The room of each material: "S:NLHU25_OCWOODFLOOR" belongs to NLHU25, like OpenGothic's guildOfRoom reads it.
        /// The G1 mesh leaves the polygons' own sector index empty.
        /// </summary>
        private static int[] GetMaterialSectors(IMesh mesh, Dictionary<string, int> sectorByName)
        {
            var result = new int[mesh.MaterialCount];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = -1;
                var name = mesh.GetMaterial(i).Name;
                if (name == null || !name.StartsWith("S:", StringComparison.OrdinalIgnoreCase))
                    continue;

                var room = name.Substring(2);
                var end = room.IndexOf('_');
                if (end > 0)
                    room = room.Substring(0, end);
                if (sectorByName.TryGetValue(room, out var sector))
                    result[i] = sector;
            }

            return result;
        }

        private static bool IsFloor(List<System.Numerics.Vector3> positions, List<int> indices)
        {
            var a = positions[indices[0]];
            var normal = System.Numerics.Vector3.Cross(positions[indices[1]] - a, positions[indices[2]] - a);
            var length = normal.Length();
            return length > 0f && Mathf.Abs(normal.Y / length) >= _floorNormalY;
        }

        private void AddFloor(int sector, List<System.Numerics.Vector3> positions, List<int> indices)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var index in indices)
            {
                // Gothic's centimeters to meters, same axes (NumericsExtension.ToUnityVector).
                var position = positions[index].ToUnityVector();
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            for (var x = Mathf.FloorToInt(min.x / _cellSize); x <= Mathf.FloorToInt(max.x / _cellSize); x++)
            {
                for (var z = Mathf.FloorToInt(min.z / _cellSize); z <= Mathf.FloorToInt(max.z / _cellSize); z++)
                {
                    var key = ToCell(x, z);
                    if (!_floorCells.TryGetValue(key, out var floors))
                        _floorCells[key] = floors = new List<(int, float, float)>(1);
                    floors.Add((sector, min.y, max.y));
                }
            }
        }

        private static long ToCell(float x, float z)
        {
            return ToCell(Mathf.FloorToInt(x / _cellSize), Mathf.FloorToInt(z / _cellSize));
        }

        private static long ToCell(int x, int z)
        {
            return ((long)x << 32) | (uint)z;
        }
    }
}
