using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.AI;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableNpcNavMesh): a NavMesh for chasing NPCs, so they run around obstacles and stop at
    /// ledges/deep drops instead of running off the world. The engine does this with its own ledge checks; the waynet
    /// stays the main way to walk (routines, GoToWp/Fp) like in the engine.
    /// - Built at runtime (UnityEngine.AI.NavMeshBuilder, no package) from the colliders on the Default layer (world
    ///   mesh + vobs). VOBs are created lazily near the hero, so the NavMesh covers an area around the hero and is
    ///   rebuilt asynchronously when the hero moved away - chases happen there anyway.
    /// - Agent from the Gothic guild values of GIL_HUMAN: STEP_HEIGHT (climb) and SLIDE_ANGLE (max walkable slope).
    /// - Kept up to date around the hero all the time (checked every second), not only when a chase asks.
    /// - Ladders (oCMobLadder) become NavMesh links: a path may go up/down a ladder, NpcJumpFall climbs it.
    /// Not saved - rebuilt after loading.
    /// </summary>
    public class NpcNavMeshService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        private const float _areaSize = 120f;
        private const float _areaHeight = 80f;
        private const float _rebuildDistance = 30f;
        private const float _agentRadius = 0.35f;
        private const float _agentHeight = 1.8f;
        private const float _defaultStepHeight = 0.6f;
        private const float _defaultSlideAngle = 45f;
        private const float _sampleDistanceNpc = 1.5f;
        private const float _sampleDistanceTarget = 3f;
        private const float _cornerReachedDistance = 0.4f;
        private const float _updateIntervalSeconds = 1f;
        private const float _ladderSampleDistance = 2f;
        private const float _ladderLinkWidth = 0.6f;
        private const float _ladderMatchDistance = 1.5f;

        private NavMeshBuildSettings _settings;
        private bool _hasSettings;
        private NavMeshData _data;
        private NavMeshDataInstance _instance;
        private AsyncOperation _buildOperation;
        private Vector3 _center;
        private bool _isReady;
        private bool _hasLoggedBuildDone;
        private readonly Stopwatch _buildWatch = new();
        private readonly List<NavMeshBuildSource> _sources = new();
        private readonly List<NavMeshBuildMarkup> _markups = new();
        private readonly NavMeshPath _path = new();
        private readonly List<NavMeshLinkInstance> _ladderLinks = new();
        private readonly List<(Vector3 bottom, Vector3 top)> _ladders = new();
        // Per ladder (same index): where an NPC stands on it (in front of the rungs, open side) and where it looks.
        private readonly List<(Vector3 column, Vector3 facing)> _ladderStances = new();
        private const float _ladderStandOff = 0.3f;
        private bool _isUpdateLoopRunning;


        /// <summary>
        /// Called at boot (BootstrapService) - Reflex creates services lazily, the first chase was too late for
        /// WorldSceneLoaded and the NavMesh was only built while fighting.
        /// </summary>
        public void Init()
        {
            // A new world: the old NavMesh belongs to the old one.
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(() =>
            {
                Reset();
                if (!_isUpdateLoopRunning)
                    _unityMonoService.StartCoroutine(UpdateLoop());
            });
        }

        private IEnumerator UpdateLoop()
        {
            _isUpdateLoopRunning = true;
            var wait = new WaitForSecondsRealtime(_updateIntervalSeconds);
            while (true)
            {
                if (_configService.Dev.EnableNpcNavMesh)
                    EnsureNavMesh();
                yield return wait;
            }
        }

        /// <summary>
        /// Where to climb the ladder with these ends (from TryGetLadder/TryFindLadderRoute): column = a point in front of
        /// the rungs (only X/Z matter), facing = towards the rungs.
        /// </summary>
        public bool TryGetLadderStance(Vector3 ladderStart, Vector3 ladderEnd, out Vector3 column, out Vector3 facing)
        {
            for (var i = 0; i < _ladders.Count; i++)
            {
                var (bottom, top) = _ladders[i];
                if ((bottom == ladderStart && top == ladderEnd) || (bottom == ladderEnd && top == ladderStart))
                {
                    (column, facing) = _ladderStances[i];
                    return true;
                }
            }

            column = facing = Vector3.zero;
            return false;
        }

        /// <summary>
        /// The ladder mesh's thinnest horizontal axis is the rungs' normal; the NPC stands on the side of the lower
        /// NavMesh end (the wall is behind the ladder).
        /// </summary>
        private static (Vector3 column, Vector3 facing) GetLadderStance(GameObject ladder, Bounds bounds, Vector3 bottom)
        {
            var center = new Vector3(bounds.center.x, 0f, bounds.center.z);
            var normal = Vector3.zero;
            var meshFilter = ladder.GetComponentInChildren<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var size = meshFilter.sharedMesh.bounds.size;
                var axisTransform = meshFilter.transform;
                var x = axisTransform.TransformVector(new Vector3(size.x, 0f, 0f));
                var y = axisTransform.TransformVector(new Vector3(0f, size.y, 0f));
                var z = axisTransform.TransformVector(new Vector3(0f, 0f, size.z));
                // Of the three local axes, the most horizontal and shortest one.
                var best = float.MaxValue;
                foreach (var axis in new[] { x, y, z })
                {
                    var flat = new Vector3(axis.x, 0f, axis.z);
                    if (flat.magnitude < axis.magnitude * 0.5f)
                        continue; // mostly vertical - the ladder's length
                    if (axis.magnitude < best)
                    {
                        best = axis.magnitude;
                        normal = flat.normalized;
                    }
                }
            }
            if (normal == Vector3.zero)
            {
                normal = new Vector3(bottom.x, 0f, bottom.z) - center;
                normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.forward;
            }
            if (Vector3.Dot(new Vector3(bottom.x, 0f, bottom.z) - center, normal) < 0f)
                normal = -normal;

            return (center + normal * _ladderStandOff, -normal);
        }

        /// <summary>
        /// A ladder link between the NPC and the next path corner (bottom->top or top->bottom).
        /// </summary>
        public bool TryGetLadder(Vector3 from, Vector3 to, out Vector3 ladderStart, out Vector3 ladderEnd)
        {
            foreach (var (bottom, top) in _ladders)
            {
                if (FlatDistance(from, bottom) < _ladderMatchDistance && FlatDistance(to, top) < _ladderMatchDistance &&
                    to.y > from.y)
                {
                    ladderStart = bottom;
                    ladderEnd = top;
                    return true;
                }
                if (FlatDistance(from, top) < _ladderMatchDistance && FlatDistance(to, bottom) < _ladderMatchDistance &&
                    to.y < from.y)
                {
                    ladderStart = top;
                    ladderEnd = bottom;
                    return true;
                }
            }

            ladderStart = ladderEnd = Vector3.zero;
            return false;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private const float _ladderRouteSearchDistance = 8f;
        private const float _ladderEndHeightTolerance = 1.5f;
        private const float _ladderDestinationHeightTolerance = 2.5f;

        /// <summary>
        /// Waynet walking (like the engine): the next waypoint is on another height and a ladder nearby connects the
        /// NPC's height with the waypoint's - returns its end at the NPC's side (walk there) and the other end.
        /// </summary>
        public bool TryFindLadderRoute(Vector3 feetPosition, Vector3 destination, out Vector3 ladderStart,
            out Vector3 ladderEnd)
        {
            ladderStart = ladderEnd = Vector3.zero;
            var bestDistance = _ladderRouteSearchDistance;
            var isUp = destination.y > feetPosition.y;
            foreach (var (bottom, top) in _ladders)
            {
                var near = isUp ? bottom : top;
                var far = isUp ? top : bottom;
                if (Mathf.Abs(near.y - feetPosition.y) > _ladderEndHeightTolerance ||
                    Mathf.Abs(far.y - destination.y) > _ladderDestinationHeightTolerance)
                    continue;

                var distance = FlatDistance(feetPosition, near);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                ladderStart = near;
                ladderEnd = far;
            }
            return bestDistance < _ladderRouteSearchDistance;
        }

        /// <summary>
        /// Where a chasing NPC should run to reach the target: the next corner of the NavMesh path.
        /// isReachable=false: the target can't be reached (on a rock, across deep water, down a cliff) - the path ends at
        /// the closest reachable point, the NPC should stop there.
        /// Returns false if there is no usable NavMesh here (not built yet, NPC off the mesh) - chase directly then.
        /// </summary>
        public bool TryGetSteerPoint(NpcContainer npc, Vector3 target, out Vector3 steerPoint, out bool isReachable)
        {
            steerPoint = target;
            isReachable = true;

            if (!_configService.Dev.EnableNpcNavMesh || npc?.Go == null)
                return false;

            EnsureNavMesh();
            if (!_isReady)
                return false;

            var filter = new NavMeshQueryFilter { agentTypeID = _settings.agentTypeID, areaMask = NavMesh.AllAreas };
            var npcPosition = npc.Go.transform.position;
            if (!NavMesh.SamplePosition(npcPosition, out var startHit, _sampleDistanceNpc, filter))
                return false;

            var end = NavMesh.SamplePosition(target, out var endHit, _sampleDistanceTarget, filter)
                ? endHit.position
                : target;
            if (!NavMesh.CalculatePath(startHit.position, end, filter, _path) || _path.corners.Length < 2)
                return false;

            isReachable = _path.status == NavMeshPathStatus.PathComplete;

            var corners = _path.corners;
            steerPoint = corners[^1];
            for (var i = 1; i < corners.Length; i++)
            {
                var flat = corners[i] - npcPosition;
                flat.y = 0f;
                if (flat.magnitude > _cornerReachedDistance)
                {
                    steerPoint = corners[i];
                    break;
                }
            }
            return true;
        }

        /// <summary>
        /// (Re)builds the NavMesh around the hero asynchronously once the hero left the built area's middle.
        /// </summary>
        private void EnsureNavMesh()
        {
            if (_buildOperation != null)
            {
                if (!_buildOperation.isDone)
                    return;

                _buildOperation = null;
                _isReady = true;
                AddLadderLinks();
                if (!_hasLoggedBuildDone)
                {
                    _hasLoggedBuildDone = true;
                    Logger.Log($"[NpcNavMesh] Built around {_center} in {_buildWatch.ElapsedMilliseconds} ms " +
                               $"({_sources.Count} colliders, {_ladders.Count} ladders).", LogCat.Ai);
                }
            }

            var hero = _gameStateService.GothicVm?.GlobalHero as NpcInstance;
            var heroGo = hero?.GetUserData()?.Go;
            if (heroGo == null)
                return;

            var heroPosition = heroGo.transform.position;
            if (_data != null && Vector3.Distance(heroPosition, _center) < _rebuildDistance)
                return;

            Build(heroPosition);
        }

        private void Build(Vector3 center)
        {
            CreateSettings();

            _center = center;
            var bounds = new Bounds(center, new Vector3(_areaSize, _areaHeight, _areaSize));

            _sources.Clear();
            NavMeshBuilder.CollectSources(bounds, 1 << Constants.DefaultLayer, NavMeshCollectGeometry.PhysicsColliders, 0,
                _markups, _sources);

            if (_data == null)
            {
                _data = new NavMeshData(_settings.agentTypeID);
                _instance = NavMesh.AddNavMeshData(_data);
            }

            _hasLoggedBuildDone = false;
            _buildWatch.Restart();
            _buildOperation = NavMeshBuilder.UpdateNavMeshDataAsync(_data, _settings, _sources, bounds);
        }

        /// <summary>
        /// Every loaded ladder in the built area becomes a two-way link between the NavMesh below and above it.
        /// </summary>
        private void AddLadderLinks()
        {
            RemoveLadderLinks();

            var area = new Bounds(_center, new Vector3(_areaSize, _areaHeight, _areaSize));
            var filter = new NavMeshQueryFilter { agentTypeID = _settings.agentTypeID, areaMask = NavMesh.AllAreas };
            foreach (var vob in _multiTypeCacheService.VobCache)
            {
                if (vob?.Vob == null || vob.Vob.Type != VirtualObjectType.oCMobLadder || vob.Go == null)
                    continue;

                var renderers = vob.Go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                    continue;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                if (!area.Intersects(bounds))
                    continue;

                var bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                var top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                if (!NavMesh.SamplePosition(bottom, out var bottomHit, _ladderSampleDistance, filter) ||
                    !NavMesh.SamplePosition(top, out var topHit, _ladderSampleDistance, filter) ||
                    topHit.position.y - bottomHit.position.y < _settings.agentClimb)
                    continue;

                var link = new NavMeshLinkData
                {
                    startPosition = bottomHit.position,
                    endPosition = topHit.position,
                    width = _ladderLinkWidth,
                    bidirectional = true,
                    agentTypeID = _settings.agentTypeID,
                    area = 0,
                    costModifier = -1
                };
                var instance = NavMesh.AddLink(link);
                if (!instance.valid)
                    continue;

                _ladderLinks.Add(instance);
                _ladders.Add((bottomHit.position, topHit.position));
                _ladderStances.Add(GetLadderStance(vob.Go, bounds, bottomHit.position));
            }
        }

        private void RemoveLadderLinks()
        {
            foreach (var link in _ladderLinks)
            {
                if (link.valid)
                    link.Remove();
            }
            _ladderLinks.Clear();
            _ladders.Clear();
            _ladderStances.Clear();
        }

        /// <summary>
        /// A runtime agent type with the Gothic human values (cm/degrees in Species.d).
        /// </summary>
        private void CreateSettings()
        {
            if (_hasSettings)
                return;

            var guildValues = _gameStateService.GuildValues;
            var human = (int)VmGothicEnums.Guild.GIL_HUMAN;
            var stepHeight = guildValues != null ? guildValues.GetStepHeight(human) / 100f : 0f;
            var slideAngle = guildValues != null ? guildValues.GetSlideAngle(human) : 0f;

            _settings = NavMesh.CreateSettings();
            _settings.agentRadius = _agentRadius;
            _settings.agentHeight = _agentHeight;
            _settings.agentClimb = stepHeight > 0f ? stepHeight : _defaultStepHeight;
            _settings.agentSlope = slideAngle > 0f ? Mathf.Min(slideAngle, 60f) : _defaultSlideAngle;
            _hasSettings = true;

            Logger.Log($"[NpcNavMesh] Agent: climb={_settings.agentClimb:F2} m, slope={_settings.agentSlope:F0} deg " +
                       $"(GIL_HUMAN STEP_HEIGHT/SLIDE_ANGLE).", LogCat.Ai);
        }

        private void Reset()
        {
            _buildOperation = null;
            _isReady = false;
            RemoveLadderLinks();
            if (_instance.valid)
                _instance.Remove();
            _instance = default;
            _data = null;
        }
    }
}
