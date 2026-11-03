using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vob.WayNet;
using Gothic.Core.Models.WayNet;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
using Material = UnityEngine.Material;
using Mesh = UnityEngine.Mesh;
using WayPoint = Gothic.Core.Models.Vob.WayNet.WayPoint;

namespace Gothic.Core.Creator
{
    public class WayNetService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        // Reused by FindFastestPath() (main thread only).
        private readonly List<(double Distance, DijkstraWaypoint Waypoint)> _openWaypoints = new();
        private readonly Dictionary<DijkstraWaypoint, DijkstraWaypoint> _previousWaypoints = new();
        private readonly HashSet<DijkstraWaypoint> _visitedWaypoints = new();

        public void Create(DeveloperConfig config, WorldContainer world)
        {
            var waynetObj = new GameObject("WayNet");

            SetWayPointCache(world.WayNet);
            CreateWaypoints(waynetObj, world, config.ShowWayPoints);
            CreateDijkstraWaypoints(world.WayNet);
            CreateWaypointEdges(waynetObj, world, config.ShowWayEdges);
        }

        private void SetWayPointCache(IWayNet wayNet)
        {
            _gameStateService.WayPoints.Clear();

            foreach (var wp in wayNet.Points)
            {
                _gameStateService.WayPoints.Add(wp.Name, new WayPoint
                {
                    Name = wp.Name,
                    Position = wp.Position.ToUnityVector(),
                    Direction = wp.Direction.ToUnityVector()
                });
            }
        }

        private void CreateDijkstraWaypoints(IWayNet wayNet)
        {
            CreateDijkstraWaypointEntries(wayNet);
            AttachWaypointPositionToDijkstraEntries();
            CalculateDijkstraNeighbourDistances();
        }

        private void CreateDijkstraWaypointEntries(IWayNet wayNet)
        {
            Dictionary<string, DijkstraWaypoint> dijkstraWaypoints = new();
            var wayEdges = wayNet.Edges;
            var wayPoints = wayNet.Points;

            // Using LINQ to transform wayEdges into DijkstraWaypoints.
            dijkstraWaypoints = wayEdges.SelectMany(edge => new[]
                {
                    // For each edge, create two entries: one for each direction of the edge.
                    // 'a' is the source waypoint, 'b' is the destination waypoint.
                    new { a = edge.A, b = edge.B },
                    new { a = edge.B, b = edge.A }
                })
                .GroupBy(x => x.a.Name) // Group the entries by the name of the source waypoint.
                .ToDictionary(g => g.Key, g =>
                    new DijkstraWaypoint(g.Key) // Transform each group into a DijkstraWaypoint.
                    {
                        // The neighbors of the DijkstraWaypoint are the names of the destination waypoints in the group.
                        Neighbors = g.Select(x => x.b.Name).ToList()
                    });

            _gameStateService.DijkstraWaypoints = dijkstraWaypoints;
        }

        private void AttachWaypointPositionToDijkstraEntries()
        {
            foreach (var waypoint in _gameStateService.DijkstraWaypoints)
            {
                var result = _gameStateService.WayPoints.First(i => i.Key == waypoint.Key).Value.Position;
                waypoint.Value.Position = result;
            }
        }

        /// <summary>
        /// Needed for future calculations.
        /// </summary>
        private void CalculateDijkstraNeighbourDistances()
        {
            foreach (var waypoint in _gameStateService.DijkstraWaypoints.Values)
            {
                foreach (var neighbour in waypoint.Neighbors)
                {
                    if (waypoint.DistanceToNeighbors.ContainsKey(neighbour))
                    {
                        continue;
                    }

                    waypoint.DistanceToNeighbors.Add(neighbour,
                        Vector3.Distance(waypoint.Position, _gameStateService.DijkstraWaypoints[neighbour].Position));
                }
            }
        }

        private void CreateWaypoints(GameObject parent, WorldContainer world, bool debugDraw)
        {
            var waypointsObj = new GameObject("WayPoints");
            waypointsObj.SetParent(parent);

            foreach (var waypoint in world.WayNet.Points)
            {
                var wpObject = _resourceCacheService.TryGetPrefabObject(PrefabType.WayPoint)!;

                if (debugDraw)
                {
                    var rend = wpObject.AddComponent<MeshRenderer>();
                    var filter = wpObject.AddComponent<MeshFilter>();
                    filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                    rend.sharedMaterial = Constants.DebugMaterial;
                }

                wpObject.name = waypoint.Name;
                wpObject.transform.position = waypoint.Position.ToUnityVector();

                wpObject.SetParent(waypointsObj);
            }
        }

        private void CreateWaypointEdges(GameObject parent, WorldContainer world, bool debugDraw)
        {
            var waypointEdgesObj = new GameObject("WayPointEdges");
            waypointEdgesObj.SetParent(parent);

            if (!debugDraw)
            {
                return;
            }

            foreach (var edge in world.WayNet.Edges)
            {
                var startPos = edge.A.Position.ToUnityVector();
                var endPos = edge.B.Position.ToUnityVector();
                var lineObj = new GameObject();

                lineObj.AddComponent<LineRenderer>();
                var lr = lineObj.GetComponent<LineRenderer>();
                lr.material = new Material(Constants.ShaderStandard);
                lr.startWidth = 0.1f;
                lr.endWidth = 0.1f;
                lr.SetPosition(0, startPos);
                lr.SetPosition(1, endPos);

                lineObj.name = $"{edge.A.Name}->{edge.B.Name}";
                lineObj.transform.position = startPos;
                lineObj.transform.parent = waypointEdgesObj.transform;
            }
        }

        /// <summary>
        /// Check within WayPoints and FreePoints if an entry exists.
        /// </summary>
        /// <param name="pointName"></param>
        /// <returns></returns>
        [CanBeNull]
        public WayNetPoint GetWayNetPoint(string pointName)
        {
            var wayPoint = _gameStateService.WayPoints
                .FirstOrDefault(item => item.Key.Equals(pointName, StringComparison.OrdinalIgnoreCase))
                .Value;
            if (wayPoint != null)
            {
                return wayPoint;
            }

            var freePoint = _gameStateService.FreePoints
                .FirstOrDefault(pair => pair.Key.Equals(pointName, StringComparison.OrdinalIgnoreCase))
                .Value;
            return freePoint;
        }

        public List<FreePoint> FindFreePointsWithName(Vector3 lookupPosition, string namePart, float maxDistance)
        {
            var matchingFreePoints = _gameStateService.FreePoints
                .Where(pair => pair.Key.Contains(namePart))
                .Where(pair => Vector3.Distance(lookupPosition, pair.Value.Position) <= maxDistance) // PF is in range
                .OrderBy(pair =>
                    Vector3.Distance(lookupPosition, pair.Value.Position)) // order from nearest to farthest
                .Select(pair => pair.Value);

            return matchingFreePoints.ToList();
        }

        public WayPoint FindNearestWayPoint(Vector3 lookupPosition, bool findSecondNearest = false)
        {
            var wayPoint = _gameStateService.WayPoints
                .OrderBy(pair => Vector3.Distance(pair.Value.Position, lookupPosition))
                .Skip(findSecondNearest ? 1 : 0)
                .FirstOrDefault();

            return wayPoint.Value;
        }

        /// <summary>
        /// npcOnFp -> If the NPC already stands on an FP, we will use this one as return value. (e.g., after ZS_TALK goes back to ZS_SMALLTALK)
        /// </summary>
        [CanBeNull]
        public FreePoint FindNearestFreePoint(Vector3 lookupPosition, string fpNamePart, FreePoint npcOnFp)
        {
            return _gameStateService.FreePoints
                .Where(pair =>
                    pair.Value.Name.ContainsIgnoreCase(fpNamePart) && (pair.Value == npcOnFp || !pair.Value.IsLocked))
                .OrderBy(pair => Vector3.Distance(pair.Value.Position, lookupPosition))
                .Select(pair => pair.Value)
                .FirstOrDefault();
        }


        /// <summary>
        /// The shortest way over the waynet, as waypoints from the end back to the start (GoToWp stacks them, so the
        /// start comes out first). Unreachable: only the end. Runs whenever an NPC starts walking, so it allocates
        /// nothing but the result.
        /// </summary>
        public DijkstraWaypoint[] FindFastestPath(string startWaypoint, string endWaypoint)
        {
            if (!_gameStateService.DijkstraWaypoints.TryGetValue(startWaypoint, out var start))
            {
                Logger.LogWarning($"FindFastestPath: start waypoint '{startWaypoint}' not found in WayNet.", LogCat.Npc);
                return null;
            }
            if (!_gameStateService.DijkstraWaypoints.TryGetValue(endWaypoint, out var end))
            {
                Logger.LogWarning($"FindFastestPath: end waypoint '{endWaypoint}' not found in WayNet.", LogCat.Npc);
                return null;
            }

            foreach (var waypoint in _gameStateService.DijkstraWaypoints.Values)
                waypoint.SummedDistance = double.MaxValue;

            _openWaypoints.Clear();
            _previousWaypoints.Clear();
            _visitedWaypoints.Clear();

            // Dijkstra with a binary heap. A shorter way to a waypoint adds it again instead of updating it, the
            // outdated entry is skipped when it comes up. The first time the end comes up, its way is the shortest.
            start.SummedDistance = 0;
            PushOpenWaypoint(0, start);
            while (_openWaypoints.Count > 0)
            {
                var (distance, current) = PopOpenWaypoint();
                if (!_visitedWaypoints.Add(current))
                    continue;

                if (current == end)
                    break;

                foreach (var neighbor in current.DistanceToNeighbors)
                {
                    if (!_gameStateService.DijkstraWaypoints.TryGetValue(neighbor.Key, out var next) ||
                        _visitedWaypoints.Contains(next))
                        continue;

                    var nextDistance = distance + neighbor.Value;
                    if (nextDistance >= next.SummedDistance)
                        continue;

                    next.SummedDistance = nextDistance;
                    _previousWaypoints[next] = current;
                    PushOpenWaypoint(nextDistance, next);
                }
            }

            var path = new List<DijkstraWaypoint>();
            for (var waypoint = end; waypoint != null;
                 waypoint = _previousWaypoints.TryGetValue(waypoint, out var previous) ? previous : null)
                path.Add(waypoint);

            return path.ToArray();
        }

        private void PushOpenWaypoint(double distance, DijkstraWaypoint waypoint)
        {
            _openWaypoints.Add((distance, waypoint));

            var index = _openWaypoints.Count - 1;
            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (_openWaypoints[parent].Distance <= _openWaypoints[index].Distance)
                    break;

                (_openWaypoints[parent], _openWaypoints[index]) = (_openWaypoints[index], _openWaypoints[parent]);
                index = parent;
            }
        }

        private (double Distance, DijkstraWaypoint Waypoint) PopOpenWaypoint()
        {
            var top = _openWaypoints[0];
            var lastIndex = _openWaypoints.Count - 1;
            _openWaypoints[0] = _openWaypoints[lastIndex];
            _openWaypoints.RemoveAt(lastIndex);

            var index = 0;
            while (true)
            {
                var left = index * 2 + 1;
                if (left >= _openWaypoints.Count)
                    break;

                var right = left + 1;
                var smaller = right < _openWaypoints.Count &&
                              _openWaypoints[right].Distance < _openWaypoints[left].Distance
                    ? right
                    : left;
                if (_openWaypoints[index].Distance <= _openWaypoints[smaller].Distance)
                    break;

                (_openWaypoints[index], _openWaypoints[smaller]) = (_openWaypoints[smaller], _openWaypoints[index]);
                index = smaller;
            }

            return top;
        }
    }
}
