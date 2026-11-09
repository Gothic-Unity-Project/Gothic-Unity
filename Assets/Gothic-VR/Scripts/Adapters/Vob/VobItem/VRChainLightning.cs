using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Models;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// G1's chain lightning while the hero holds it (VRRuneCaster invests).
    /// - The first invest level (after time_per_mana, 0.75 s of charging): the NPC most directly in front of the eyes
    ///   (TARGET_COLLECT range 15 m) is zapped - a lightning from the rune hand to him.
    /// - Every _levelsPerJump levels (Spell_Logic_ChainLightning returns SPL_NEXTLEVEL every time_per_mana) the lightning
    ///   jumps on to the nearest NPC around the last victim. Nobody left: it turns back and zaps the hero himself
    ///   (engine: "hit yourself") - hold it a while, not too long.
    /// - The victims get PERC_ASSESSMAGIC: the scripts' ZS_Zapped hurts them (B_MagicHurtNpc, SPL_ZAPPED_DAMAGE_PER_SEC)
    ///   until the release sends PERC_ASSESSSTOPMAGIC (B_StopZapped). The hero is hurt here by the same amount.
    /// </summary>
    public class VRChainLightning : MonoBehaviour
    {
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;

        private const float _defaultRangeMeters = 15f;
        private const float _firstTargetAngle = 30f;
        private const float _jumpRangeMeters = 5f;
        private const float _bodyHeight = 1.1f;
        private const float _heroChestBelowEyes = 0.4f;
        private const int _defaultSelfDamagePerSecond = 25;
        // Invest levels between two jumps (0.75 s each in G1) - it jumped every level, back to the hero at once.
        private const int _levelsPerJump = 3;
        private int _levelsSinceJump;
        private const int _segmentsPerLink = 6;
        private const float _jitter = 0.12f;
        private const float _rejitterSeconds = 0.05f;
        private const float _beamWidth = 0.025f;
        private static readonly Color _beamColor = new(0.75f, 0.9f, 1f);

        private readonly List<NpcContainer> _victims = new();
        private VrHandSide _handSide;
        private bool _isHeroZapped;
        private float _heroZapTimer;
        private float _nextJitter;
        private LineRenderer _line;
        private Material _lineMaterial;
        private bool _isStopped;
        private Camera _camera;
        private bool _hasWarnedNoHand;
        // Reused every re-jitter (every 0.05 s) to avoid GC allocations.
        private readonly List<Vector3> _points = new();
        private Vector3[] _positions = new Vector3[0];

        public void Begin(VrHandSide handSide)
        {
            this.Inject();
            _handSide = handSide;
            CreateLine();
        }

        /// <summary>
        /// One more invest level: the first zaps the NPC in front, then every _levelsPerJump-th jumps on - or back to the
        /// hero.
        /// </summary>
        public void AddLink()
        {
            if (_isStopped || _isHeroZapped)
                return;
            if (_victims.Count > 0 && ++_levelsSinceJump < _levelsPerJump)
                return;
            _levelsSinceJump = 0;

            var next = _victims.Count == 0 ? FindInFrontOfEyes() : FindNextAround(_victims[^1]);
            if (next == null)
            {
                _isHeroZapped = true;
                _heroZapTimer = 0f;
                Logger.Log($"[ChainLightning] nobody {(_victims.Count == 0 ? "in front" : "around")} - zaps the hero",
                    LogCat.VR);
                return;
            }

            _victims.Add(next);
            var hero = _npcService.GetHeroContainer();
            _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessMagic, next.Props, next.Instance,
                next.Instance, hero.Instance);
            Logger.Log($"[ChainLightning] zaps {next.Instance.GetName(NpcNameSlot.Slot0)} (link {_victims.Count})",
                LogCat.VR);
        }

        /// <summary>
        /// Released (or out of mana, rune gone): PERC_ASSESSSTOPMAGIC frees the victims (B_StopZapped).
        /// </summary>
        public void Stop()
        {
            if (_isStopped)
                return;
            _isStopped = true;

            var hero = _npcService.GetHeroContainer();
            foreach (var victim in _victims)
            {
                if (victim?.Props == null || victim.IsDown())
                    continue;
                _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssesssTopMagic, victim.Props,
                    victim.Instance, victim.Instance, hero?.Instance);
            }
            Logger.Log($"[ChainLightning] released - {_victims.Count} victim(s) freed", LogCat.VR);
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (!_isStopped)
                Stop();
            if (_line != null)
                Destroy(_line.gameObject);
            if (_lineMaterial != null)
                Destroy(_lineMaterial);
        }

        private void Update()
        {
            if (_isStopped)
                return;

            var hero = _npcService.GetHeroContainer();
            foreach (var victim in _victims)
            {
                // ZS_Zapped hurts by script - the engine kills at 0 hit points.
                if (victim?.Props != null && !victim.IsDown() &&
                    victim.Vob.GetAttribute((int)NpcAttribute.HitPoints) <= 0)
                    _fightService.OnScriptDamageDepleted(victim, hero);
            }

            if (_isHeroZapped && hero != null)
            {
                _heroZapTimer -= Time.deltaTime;
                if (_heroZapTimer <= 0f)
                {
                    _heroZapTimer = 1f;
                    var damage = _gameStateService.GothicVm.GetSymbolByName("SPL_ZAPPED_DAMAGE_PER_SEC")?.GetInt(0) ??
                                 _defaultSelfDamagePerSecond;
                    _fightService.ApplySelfSpellDamage(hero, damage);
                }
            }

            UpdateLine();
        }

        private Camera GetCamera()
        {
            if (_camera == null)
                _camera = Camera.main;
            return _camera;
        }

        private NpcContainer FindInFrontOfEyes()
        {
            var head = GetCamera();
            if (head == null)
            {
                Logger.LogWarning("[ChainLightning] no main camera - can't find a target in front of the eyes",
                    LogCat.VR);
                return null;
            }

            var range = GetRangeMeters();
            NpcContainer best = null;
            var bestAngle = _firstTargetAngle;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (!IsZappable(npc))
                    continue;
                var toNpc = GetBodyPosition(npc) - head.transform.position;
                if (toNpc.magnitude > range)
                    continue;
                var angle = Vector3.Angle(head.transform.forward, toNpc);
                if (angle >= bestAngle)
                    continue;
                bestAngle = angle;
                best = npc;
            }
            return best;
        }

        private NpcContainer FindNextAround(NpcContainer from)
        {
            if (from?.Go == null)
                return null;

            NpcContainer best = null;
            var bestDistance = _jumpRangeMeters;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (!IsZappable(npc) || _victims.Contains(npc))
                    continue;
                var distance = Vector3.Distance(npc.Go.transform.position, from.Go.transform.position);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                best = npc;
            }
            return best;
        }

        private bool IsZappable(NpcContainer npc)
        {
            return npc?.Go != null && npc.Go.activeInHierarchy && npc.Props != null && npc.Instance != null &&
                   !(npc.PrefabProps != null && npc.PrefabProps.IsHero()) && !npc.IsDown();
        }

        private float GetRangeMeters()
        {
            var spellId = _gameStateService.GothicVm.GetSymbolByName("SPL_CHAINLIGHTNING")?.GetInt(0) ?? -1;
            var range = spellId >= 0 ? _vmCacheService.TryGetSpellData(spellId)?.TargetCollectRange ?? 0 : 0;
            return range > 0 ? range / 100f : _defaultRangeMeters;
        }

        private static Vector3 GetBodyPosition(NpcContainer npc)
        {
            return npc.Go.transform.position + Vector3.up * _bodyHeight;
        }

        private void CreateLine()
        {
            var lineGo = new GameObject("ChainLightning");
            _line = lineGo.AddComponent<LineRenderer>();
            _lineMaterial = new Material(Gothic.Core.Const.Constants.ShaderUnlit);
            _lineMaterial.SetColor("_BaseColor", _beamColor);
            _line.sharedMaterial = _lineMaterial;
            _line.startWidth = _beamWidth;
            _line.endWidth = _beamWidth;
            _line.positionCount = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
        }

        /// <summary>
        /// A jagged line hand -> victim -> victim ... (-> back to the hero), new zigzags every few frames.
        /// </summary>
        private void UpdateLine()
        {
            if (_line == null || Time.time < _nextJitter)
                return;
            _nextJitter = Time.time + _rejitterSeconds;

            var hand = _vrPlayerService.GetHandModelGo(_handSide);
            if (hand == null)
            {
                // Throttled by _nextJitter already, but warn once only.
                if (!_hasWarnedNoHand)
                    Logger.LogWarning($"[ChainLightning] no hand model for {_handSide} - line not drawn", LogCat.VR);
                _hasWarnedNoHand = true;
                return;
            }

            _points.Clear();
            _points.Add(hand.transform.position);
            foreach (var victim in _victims)
            {
                if (victim?.Go != null)
                    _points.Add(GetBodyPosition(victim));
            }
            if (_isHeroZapped && GetCamera() != null)
                _points.Add(GetCamera().transform.position + Vector3.down * _heroChestBelowEyes);

            var count = (_points.Count - 1) * _segmentsPerLink + 1;
            if (_points.Count < 2)
            {
                _line.positionCount = 0;
                return;
            }
            if (_positions.Length != count)
                _positions = new Vector3[count];

            var index = 0;
            for (var i = 0; i < _points.Count - 1; i++)
            {
                for (var s = 0; s < _segmentsPerLink; s++)
                {
                    var t = (float)s / _segmentsPerLink;
                    var point = Vector3.Lerp(_points[i], _points[i + 1], t);
                    if (s > 0)
                        point += Random.insideUnitSphere * _jitter;
                    _positions[index++] = point;
                }
            }
            _positions[index] = _points[^1];

            _line.positionCount = count;
            _line.SetPositions(_positions);
        }
    }
}
