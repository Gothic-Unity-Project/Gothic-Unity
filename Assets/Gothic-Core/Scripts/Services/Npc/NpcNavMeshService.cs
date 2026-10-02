using System.Collections.Generic;
using System.Diagnostics;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.AI;
using ZenKit.Daedalus;
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
    /// Not saved - rebuilt after loading.
    /// </summary>
    public class NpcNavMeshService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;

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


        public NpcNavMeshService()
        {
            // A new world: the old NavMesh belongs to the old one.
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(Reset);
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
                if (!_hasLoggedBuildDone)
                {
                    _hasLoggedBuildDone = true;
                    Logger.Log($"[NpcNavMesh] Built around {_center} in {_buildWatch.ElapsedMilliseconds} ms " +
                               $"({_sources.Count} colliders).", LogCat.Ai);
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
            if (_instance.valid)
                _instance.Remove();
            _instance = default;
            _data = null;
        }
    }
}
