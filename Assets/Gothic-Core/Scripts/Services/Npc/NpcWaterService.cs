using System.Collections.Generic;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Water for NPCs and monsters, like the engine (OpenGothic MoveAlgo::tryMove):
    /// - depth > WATER_DEPTH_KNEE of the guild: wading (WALKW animations, if the model has them).
    /// - depth > WATER_DEPTH_CHEST and S_SWIM + S_SWIMF exist: swimming at the surface (body just below it).
    /// - depth > WATER_DEPTH_CHEST without swim animations (most monsters): the water is a wall. The hero escapes them
    ///   by swimming - ZS_MM_Attack gives up when the hero is BS_SWIM (AIV_MM_FollowInWater).
    /// - Both depths 999999 (Bloodfly, Demon): flying - over water the surface is their ground.
    /// The level is stored in Vob.AiHuman.WaterLevel (the hero uses the same field, see VRSwimDive).
    /// Taken over from BoroBongo's branch. Two rays per moving NPC near the player and frame, four while one that
    /// can't swim pushes against deep water.
    /// </summary>
    public class NpcWaterService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly AudioService _audioService;

        private const string _splashParticles = "PFX_WATERSPLASH";
        private const string _splashSound = "CS_INTRO_WATERSPLASH.WAV";

        private const int _flyOverWaterHint = 999999;
        private const float _rayHeight = 50f;
        private const float _bridgeTolerance = 0.3f;
        // Swimmers: the hips this part of the rest root height below the surface - the head stays above water.
        // The swim animations move the root bone themselves (S_SWIM lowers it a lot) - that offset is compensated.
        private const float _swimRootDepthFactor = 0.25f;
        // Vertical speed (m/s) into the swim/hover height - no jump when the water gets deep.
        private const float _surfaceSpeed = 1.5f;
        // Quest 2: only NPCs this close to the player (m) check the water - the ones he sees. Farther ones keep their
        // last level, gravity and height until he comes closer.
        private const float _maxPlayerDistance = 30f;

        private readonly Dictionary<string, bool> _hasSwimAnimations = new();
        private readonly HashSet<NpcContainer> _loggedBarriers = new();
        private readonly HashSet<NpcContainer> _loggedSwimHeights = new();
        private bool _isMissingSplashSoundLogged;
        // NPCs whose water was checked since they appeared. The first check after a spawn, a loaded world or save, or
        // a culling respawn (back at the routine's waypoint) only learns the water - who appears in it doesn't splash.
        private readonly HashSet<NpcContainer> _knownWaterLevels = new();

        public NpcWaterService()
        {
            // The NPCs belong to the world that is left - don't keep them alive.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(() =>
            {
                _loggedBarriers.Clear();
                _loggedSwimHeights.Clear();
                _knownWaterLevels.Clear();
            });
            GlobalEventDispatcher.NpcMeshCullingChanged.AddListener((npc, _, isInVisibleRange, _) =>
            {
                if (!isInVisibleRange)
                    _knownWaterLevels.Remove(npc);
            });
        }

        /// <summary>
        /// Called by AnimationSystem with this frame's root motion. Returns the corrected movement: no horizontal
        /// movement into deep water for non-swimmers, Y to the swim height for swimmers.
        /// </summary>
        public Vector3 ApplyWater(NpcContainer npc, Vector3 worldMove, float restRootHeight, float rootBoneOffset)
        {
            if (npc?.Vob?.AiHuman == null || npc.Go == null)
                return worldMove;

            var ai = npc.Vob.AiHuman;
            var isMoving = worldMove.x != 0f || worldMove.z != 0f;
            if (!isMoving && ai.WaterLevel == (int)ZenGineConst.WaterLevel.Normal)
                return worldMove;

            var pos = npc.Go.transform.position;
            if (!IsNearPlayer(pos))
                return worldMove;

            var next = pos + worldMove;

            GetDepths(npc, out var knee, out var chest, out var isFlying);
            if (isFlying)
                return ApplyFlying(npc, pos, next, worldMove, restRootHeight);

            var isWaterKnown = !_knownWaterLevels.Add(npc);

            // Feet clearly above the surface: walking on a bridge/pier whose mesh isn't on the Default layer.
            var feetY = pos.y - restRootHeight;
            if (!TryGetWater(next, pos.y, out var groundY, out var waterY) ||
                (ai.WaterLevel != (int)ZenGineConst.WaterLevel.Chest && feetY > waterY + _bridgeTolerance))
            {
                if (ai.WaterLevel != (int)ZenGineConst.WaterLevel.Normal)
                    SetWaterLevel(npc, ZenGineConst.WaterLevel.Normal, waterY, isWaterKnown);
                return worldMove;
            }

            var depth = waterY - groundY;
            if (depth > chest && !HasSwimAnimations(npc))
            {
                // Water is a wall. Moving back into shallower water (or out of it) stays possible.
                var isDeeper = !TryGetWater(pos, pos.y, out var currentGroundY, out var currentWaterY) ||
                               depth > currentWaterY - currentGroundY;
                if (isDeeper)
                {
                    if (_loggedBarriers.Add(npc))
                        Logger.Log($"[NpcWater] {npc.Go.name} can't swim - deep water ({depth:F2} m) blocks it.",
                            LogCat.Ai);
                    worldMove.x = 0f;
                    worldMove.z = 0f;
                }
                return worldMove;
            }

            var level = depth > chest ? ZenGineConst.WaterLevel.Chest
                : depth > knee ? ZenGineConst.WaterLevel.Knee
                : ZenGineConst.WaterLevel.Normal;
            SetWaterLevel(npc, level, waterY, isWaterKnown);

            // Swimming: the animated hips (NPC root + root bone offset) just below the surface.
            if (level == ZenGineConst.WaterLevel.Chest)
            {
                worldMove.y = MoveToHeight(pos.y, waterY - restRootHeight * _swimRootDepthFactor - rootBoneOffset);
                if (_loggedSwimHeights.Add(npc))
                    Logger.Log($"[NpcWater] {npc.Go.name} swims: water={waterY:F2} rootY={pos.y:F2} " +
                               $"rest={restRootHeight:F2} " +
                               $"rootBoneOffset={rootBoneOffset:F2}", LogCat.Ai);
            }

            return worldMove;
        }

        /// <summary>
        /// Flying monsters (demon, bloodfly) over water: the surface is their ground - before they flew on the bottom.
        /// </summary>
        private Vector3 ApplyFlying(NpcContainer npc, Vector3 pos, Vector3 next, Vector3 worldMove,
            float restRootHeight)
        {
            var isOverWater = TryGetWater(next, pos.y, out var groundY, out var waterY) && waterY > groundY;
            // Set every call, like SetWaterLevel: a loaded save or a culling respawn brings a body with gravity.
            SetGravity(npc, !isOverWater);
            if (!isOverWater)
                return worldMove;

            var hoverY = waterY + restRootHeight;
            if (pos.y < hoverY)
                worldMove.y = MoveToHeight(pos.y, hoverY);
            return worldMove;
        }

        private static bool IsNearPlayer(Vector3 position)
        {
            var player = Camera.main;
            return player != null &&
                   (player.transform.position - position).sqrMagnitude <= _maxPlayerDistance * _maxPlayerDistance;
        }

        private static float MoveToHeight(float currentY, float targetY)
        {
            return Mathf.MoveTowards(currentY, targetY, _surfaceSpeed * Time.deltaTime) - currentY;
        }

        private static void SetGravity(NpcContainer npc, bool useGravity)
        {
            var rootMotion = npc.PrefabProps?.ColliderRootMotion;
            if (rootMotion == null || !rootMotion.TryGetComponent<Rigidbody>(out var body) ||
                body.useGravity == useGravity)
                return;

            body.useGravity = useGravity;
            if (!body.isKinematic)
                body.linearVelocity = Vector3.zero;
        }

        /// <summary>
        /// The NPC's water level. waterY is the surface height at the NPC from the depth probe - where a plunge into
        /// deep water splashes. isWaterKnown is false on the NPC's first check after it appeared: no splash then.
        /// </summary>
        private void SetWaterLevel(NpcContainer npc, ZenGineConst.WaterLevel level, float waterY, bool isWaterKnown)
        {
            // Gravity would pull a swimmer to the ground - the swim height is set each frame instead.
            // Checked every call: a loaded save or a culling respawn can restore the level without the body flag.
            SetGravity(npc, level != ZenGineConst.WaterLevel.Chest);

            var ai = npc.Vob.AiHuman;
            if (ai.WaterLevel == (int)level)
                return;

            var previous = (ZenGineConst.WaterLevel)ai.WaterLevel;
            ai.WaterLevel = (int)level;

            // Straight into deep water (a fall, a jump, a step off an edge): it splashes. Walking in passes knee-deep
            // water first.
            if (isWaterKnown && previous == ZenGineConst.WaterLevel.Normal && level == ZenGineConst.WaterLevel.Chest)
            {
                var pos = npc.Go.transform.position;
                EmitSplash(new Vector3(pos.x, waterY, pos.z));
            }

            // Daedalus checks C_BodyStateContains(self, BS_SWIM) (no sleep/freeze spells, unconscious in water, ...).
            var bodyState = npc.Props.BodyState;
            if (level == ZenGineConst.WaterLevel.Chest && bodyState is VmGothicEnums.BodyState.BsStand
                    or VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun)
                npc.Props.BodyState = VmGothicEnums.BodyState.BsSwim;
            else if (level != ZenGineConst.WaterLevel.Chest && bodyState == VmGothicEnums.BodyState.BsSwim)
                npc.Props.BodyState = VmGothicEnums.BodyState.BsStand;

            // Editor only: at a depth threshold the level can flip every frame.
            Logger.LogEditor($"[NpcWater] {npc.Go.name}: water level {previous} -> {level}.", LogCat.Ai);
        }

        /// <summary>
        /// The hero's feet just went below the water surface, coming from dry ground (VRSwimDive). Like an NPC going
        /// straight to swimming, it splashes where the water is deeper than chestDepth (m): a fall, a jump, a step off
        /// an edge. Walking in from the shore enters shallow water. The caller skips a hero who appears in water
        /// (a loaded world or save, a teleport).
        /// </summary>
        public void SplashIfDeep(Vector3 surface, float chestDepth)
        {
            if (TryGetWater(surface, surface.y, out var groundY, out var waterY) && waterY - groundY > chestDepth)
                EmitSplash(new Vector3(surface.x, waterY, surface.z));
        }

        /// <summary>
        /// Something falls into water, like the engine (OpenGothic MoveAlgo::emitWaterSplash): the splash particles and
        /// sound where it hits the surface.
        /// </summary>
        private void EmitSplash(Vector3 position)
        {
            _meshService.CreateVobPfx(_splashParticles, position, Quaternion.identity, destroyAfterPlay: true);
            var clip = _audioService.GetRandomSoundClip(_splashSound);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, position);
            else if (!_isMissingSplashSoundLogged)
            {
                _isMissingSplashSoundLogged = true;
                Logger.LogWarning($"[NpcWater] Splash sound {_splashSound} not found - water splashes stay silent.",
                    LogCat.Audio);
            }
        }

        /// <summary>
        /// Called when the NPC lands from a fall. In water deep enough to swim it swims and the fall doesn't hurt, like
        /// the engine (OpenGothic MoveAlgo: falling into such water switches to swimming). Non-swimmers hit the bottom.
        /// </summary>
        public bool TrySwimAfterFall(NpcContainer npc)
        {
            if (npc?.Vob?.AiHuman == null || npc.Go == null || !HasSwimAnimations(npc))
                return false;

            GetDepths(npc, out _, out var chest, out var isFlying);
            var pos = npc.Go.transform.position;
            if (isFlying || !TryGetWater(pos, pos.y, out var groundY, out var waterY) || waterY - groundY <= chest)
                return false;

            var isWaterKnown = !_knownWaterLevels.Add(npc);
            SetWaterLevel(npc, ZenGineConst.WaterLevel.Chest, waterY, isWaterKnown);
            // Standing NPCs keep their idle animation otherwise - the walk actions only switch it while moving.
            npc.PrefabProps?.AnimationSystem?.PlayIdleAnimation();
            return true;
        }

        /// <summary>
        /// Ground below the position and the water surface above that ground - only if nothing solid lies in between
        /// (caves below lakes).
        /// </summary>
        private static bool TryGetWater(Vector3 position, float rayStartY, out float groundY, out float waterY)
        {
            groundY = 0f;
            waterY = 0f;

            var groundMask = 1 << Constants.DefaultLayer;
            var origin = new Vector3(position.x, rayStartY + 1f, position.z);
            if (!Physics.Raycast(origin, Vector3.down, out var groundHit, _rayHeight, groundMask))
                return false;

            var waterMask = 1 << Constants.WaterLayer;
            if (!Physics.Raycast(groundHit.point + Vector3.up * 0.05f, Vector3.up, out var waterHit, _rayHeight,
                    groundMask | waterMask, QueryTriggerInteraction.Collide))
                return false;

            if (waterHit.collider.gameObject.layer != Constants.WaterLayer)
                return false;

            groundY = groundHit.point.y;
            waterY = waterHit.point.y;
            return true;
        }

        private void GetDepths(NpcContainer npc, out float knee, out float chest, out bool isFlying)
        {
            var guildValues = _gameStateService.GuildValues;
            var guild = npc.Instance.GetValuesGuild();
            var kneeCm = guildValues.GetWaterDepthKnee(guild);
            var chestCm = guildValues.GetWaterDepthChest(guild);

            isFlying = kneeCm == _flyOverWaterHint && chestCm == _flyOverWaterHint;
            knee = kneeCm / 100f;
            chest = chestCm / 100f;
        }

        private bool HasSwimAnimations(NpcContainer npc)
        {
            var mdsBase = npc.Props.MdsNameBase ?? string.Empty;
            if (_hasSwimAnimations.TryGetValue(mdsBase, out var hasSwim))
                return hasSwim;

            hasSwim = _animationService.GetTrack("S_SWIM", npc.Props.MdsNameBase, npc.Props.MdsNameOverlay) != null &&
                      _animationService.GetTrack("S_SWIMF", npc.Props.MdsNameBase, npc.Props.MdsNameOverlay) != null;
            _hasSwimAnimations[mdsBase] = hasSwim;
            return hasSwim;
        }
    }
}
