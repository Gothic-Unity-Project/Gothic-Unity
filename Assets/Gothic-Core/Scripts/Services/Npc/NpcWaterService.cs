using System.Collections.Generic;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
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
    /// </summary>
    public class NpcWaterService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly ConfigService _configService;

        private const int _flyOverWaterHint = 999999;
        private const float _rayHeight = 50f;
        private const float _bridgeTolerance = 0.3f;
        // Swimmers: the hips this part of the rest root height below the surface - the head stays above water.
        // The swim animations move the root bone themselves (S_SWIM lowers it a lot) - that offset is compensated.
        private const float _swimRootDepthFactor = 0.25f;
        // Vertical speed (m/s) into the swim/hover height - no jump when the water gets deep.
        private const float _surfaceSpeed = 1.5f;

        private readonly HashSet<NpcContainer> _hoveringOverWater = new();

        private readonly Dictionary<string, bool> _hasSwimAnimations = new();
        private readonly HashSet<NpcContainer> _loggedBarriers = new();
        private readonly HashSet<NpcContainer> _loggedSwimHeights = new();


        /// <summary>
        /// Called by AnimationSystem with this frame's root motion. Returns the corrected movement: no horizontal
        /// movement into deep water for non-swimmers, Y to the swim height for swimmers.
        /// </summary>
        public Vector3 ApplyWater(NpcContainer npc, Vector3 worldMove, float restRootHeight, float rootBoneOffset)
        {
            if (!_configService.Dev.EnableNpcWater || npc?.Vob?.AiHuman == null || npc.Go == null)
                return worldMove;

            var ai = npc.Vob.AiHuman;
            var isMoving = worldMove.x != 0f || worldMove.z != 0f;
            if (!isMoving && ai.WaterLevel == (int)ZenGineConst.WaterLevel.Normal)
                return worldMove;

            var pos = npc.Go.transform.position;
            var next = pos + worldMove;

            GetDepths(npc, out var knee, out var chest, out var isFlying);
            if (isFlying)
                return ApplyFlying(npc, pos, next, worldMove, restRootHeight);

            // Feet clearly above the surface: walking on a bridge/pier whose mesh isn't on the Default layer.
            var feetY = pos.y - restRootHeight;
            if (!TryGetWater(next, pos.y, out var groundY, out var waterY) ||
                (ai.WaterLevel != (int)ZenGineConst.WaterLevel.Chest && feetY > waterY + _bridgeTolerance))
            {
                if (ai.WaterLevel != (int)ZenGineConst.WaterLevel.Normal)
                    SetWaterLevel(npc, ZenGineConst.WaterLevel.Normal);
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
                        Logger.Log($"[NpcWater] {npc.Go.name} can't swim - deep water ({depth:F2} m) blocks it.", LogCat.Ai);
                    worldMove.x = 0f;
                    worldMove.z = 0f;
                }
                return worldMove;
            }

            var level = depth > chest ? ZenGineConst.WaterLevel.Chest
                : depth > knee ? ZenGineConst.WaterLevel.Knee
                : ZenGineConst.WaterLevel.Normal;
            SetWaterLevel(npc, level);

            // Swimming: the animated hips (NPC root + root bone offset) just below the surface.
            if (level == ZenGineConst.WaterLevel.Chest)
            {
                worldMove.y = MoveToHeight(pos.y, waterY - restRootHeight * _swimRootDepthFactor - rootBoneOffset);
                if (_loggedSwimHeights.Add(npc))
                    Logger.Log($"[NpcWater] {npc.Go.name} swims: water={waterY:F2} rootY={pos.y:F2} rest={restRootHeight:F2} " +
                               $"rootBoneOffset={rootBoneOffset:F2}", LogCat.Ai);
            }

            return worldMove;
        }

        /// <summary>
        /// Flying monsters (demon, bloodfly) over water: the surface is their ground - before they flew on the bottom.
        /// </summary>
        private Vector3 ApplyFlying(NpcContainer npc, Vector3 pos, Vector3 next, Vector3 worldMove, float restRootHeight)
        {
            var isOverWater = TryGetWater(next, pos.y, out var groundY, out var waterY) && waterY > groundY;
            if (!isOverWater)
            {
                if (_hoveringOverWater.Remove(npc))
                    SetGravity(npc, true);
                return worldMove;
            }

            if (_hoveringOverWater.Add(npc))
                SetGravity(npc, false);

            var hoverY = waterY + restRootHeight;
            if (pos.y < hoverY)
                worldMove.y = MoveToHeight(pos.y, hoverY);
            return worldMove;
        }

        private static float MoveToHeight(float currentY, float targetY)
        {
            return Mathf.MoveTowards(currentY, targetY, _surfaceSpeed * Time.deltaTime) - currentY;
        }

        private static void SetGravity(NpcContainer npc, bool useGravity)
        {
            var rootMotion = npc.PrefabProps?.ColliderRootMotion;
            if (rootMotion == null || !rootMotion.TryGetComponent<Rigidbody>(out var body) || body.useGravity == useGravity)
                return;

            body.useGravity = useGravity;
            if (!body.isKinematic)
                body.linearVelocity = Vector3.zero;
        }

        private void SetWaterLevel(NpcContainer npc, ZenGineConst.WaterLevel level)
        {
            // Gravity would pull a swimmer to the ground - the swim height is set each frame instead.
            // Checked every call: a loaded save or a culling respawn can restore the level without the body flag.
            SetGravity(npc, level != ZenGineConst.WaterLevel.Chest);

            var ai = npc.Vob.AiHuman;
            if (ai.WaterLevel == (int)level)
                return;

            var previous = (ZenGineConst.WaterLevel)ai.WaterLevel;
            ai.WaterLevel = (int)level;

            // Daedalus checks C_BodyStateContains(self, BS_SWIM) (no sleep/freeze spells, unconscious in water, ...).
            var bodyState = npc.Props.BodyState;
            if (level == ZenGineConst.WaterLevel.Chest && bodyState is VmGothicEnums.BodyState.BsStand
                    or VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun)
                npc.Props.BodyState = VmGothicEnums.BodyState.BsSwim;
            else if (level != ZenGineConst.WaterLevel.Chest && bodyState == VmGothicEnums.BodyState.BsSwim)
                npc.Props.BodyState = VmGothicEnums.BodyState.BsStand;

            Logger.Log($"[NpcWater] {npc.Go.name}: water level {previous} -> {level}.", LogCat.Ai);
        }

        /// <summary>
        /// Water surface and ground at the position (VR transformation puppet: swims at the surface).
        /// </summary>
        public bool TryGetWaterSurface(Vector3 position, out float groundY, out float waterY) =>
            TryGetWater(position, position.y, out groundY, out waterY);

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
            var guild = npc.Instance.Guild;
            var kneeCm = guildValues.GetWaterDepthKnee(guild);
            var chestCm = guildValues.GetWaterDepthChest(guild);

            // Only GIL_HUMAN is filled for humans ("Set Constants for all Human Guilds").
            if (kneeCm <= 0 && chestCm <= 0)
            {
                kneeCm = guildValues.GetWaterDepthKnee((int)VmGothicEnums.Guild.GIL_HUMAN);
                chestCm = guildValues.GetWaterDepthChest((int)VmGothicEnums.Guild.GIL_HUMAN);
            }

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
