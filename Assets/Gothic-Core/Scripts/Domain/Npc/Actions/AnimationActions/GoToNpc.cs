using System.Collections.Generic;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob.WayNet;
using Gothic.Core.Models.WayNet;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Manager;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class GoToNpc : AbstractWalkAnimationAction2
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly NpcAiService _npcAiService;

        private Transform _destinationTransform;
        private Stack<DijkstraWaypoint> _route;
        private Vector3? _approachDirection;

        public GoToNpc(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            _destinationTransform = Action.Instance0.GetUserData().Go.transform;

            if (TryWarnBeforePursuit())
                return;

            // Refresh to the actually-nearest WP instead of trusting the cached one: for NPCs that loop
            // continuously without ever going through StartNextRoutine (e.g. ZS_MM_SummonedByPC_Loop
            // re-issuing AI_GotoNpc(self, hero) every tick), Props.CurrentWayPoint is never refreshed
            // and can be stale from spawn time — computing the route from it walks the NPC back toward
            // that old spot first instead of toward its actual current position.
            var currentWp = WayNetService.FindNearestWayPoint(PrefabProps.Bip01.position);
            if (currentWp != null)
                Props.CurrentWayPoint = currentWp;

            // Already close enough for a direct approach — don't bother routing through the waynet at
            // all (see the matching bail-out in GetWalkDestination for why this matters: the hand-off
            // out of route mode requires reaching the *exact* nearest-to-target waypoint, which a freely
            // moving target may never stand on).
            var directDistance = Vector3.Distance(PrefabProps.Bip01.position, _destinationTransform.position);
            var targetWp = directDistance <= _configService.Dev.NpcDialogStopDistance * 4f
                ? null
                : WayNetService.FindNearestWayPoint(_destinationTransform.position);

            if (currentWp != null && targetWp != null && currentWp.Name != targetWp.Name)
            {
                var path = WayNetService.FindFastestPath(currentWp.Name, targetWp.Name);
                if (path != null && path.Length > 0)
                {
                    _route = new Stack<DijkstraWaypoint>(path);
                    Logger.Log($"[GoToNpc] {NpcInstance.GetName(NpcNameSlot.Slot0)}: WP route {currentWp.Name}→{targetWp.Name} steps={path.Length}", LogCat.Ai);
                }
            }

            base.Start();
        }

        protected override Vector3 GetWalkDestination()
        {
            // Bail out of waypoint-routing the moment a direct approach becomes safe, checked every
            // tick instead of only at hop boundaries (OnDestinationReached) — waiting for a full leg
            // to complete before reconsidering left the NPC visibly stuck walking to/settling near a
            // waypoint for that whole leg. Two ways out: close enough to the target directly, or a
            // clear line of sight to it — the entire reason to route via waypoints in the first place
            // is to avoid walking into walls, so once nothing is in the way there's no reason to keep
            // hopping through the waynet at all.
            if (_route != null && _route.Count > 0)
            {
                var directDist = Vector3.Distance(NpcGo.transform.position, _destinationTransform.position);
                if (directDist <= _configService.Dev.NpcDialogStopDistance * 4f ||
                    _npcHelperService.CanSeeNpc(NpcInstance, Action.Instance0, true))
                {
                    // Sync CurrentWayPoint to wherever we actually are before dropping the route —
                    // otherwise it's left stuck at the last hop OnDestinationReached happened to reach,
                    // which can be several hops behind the direct-approach walking that follows. Anyone
                    // reading CurrentWayPoint later (GoToWp resuming the routine, another GoToNpc) would
                    // otherwise compute a route from that stale spot instead of from the NPC's real
                    // position, producing a needlessly long detour.
                    var syncedWp = WayNetService.FindNearestWayPoint(NpcGo.transform.position);
                    if (syncedWp != null)
                        Props.CurrentWayPoint = syncedWp;

                    Logger.Log($"[GoToNpc] {NpcInstance.GetName(NpcNameSlot.Slot0)}: bailing out of WP route (dist={directDist:F1}m) — CurrentWayPoint synced to {syncedWp?.Name}", LogCat.Ai);
                    _route = null;
                }
            }

            if (_route != null && _route.Count > 0)
                return _route.Peek().Position;

            var targetPos = _destinationTransform.position;
            var toTargetNow = targetPos - NpcGo.transform.position;

            // Recomputing the approach direction fresh every tick turns this into a pure-pursuit
            // feedback loop: any small heading lag shifts the aim point sideways, which the NPC then
            // chases, shifting it again — orbiting the target instead of converging. But freezing it
            // forever after the first snapshot (the previous fix) fails just as badly against a target
            // that moves along a curve rather than a straight line (e.g. a VR player circle-strafing
            // during melee): the frozen direction keeps tracing the same curve at a fixed offset, so the
            // NPC chases a point that's itself still circling instead of closing distance. Slerping the
            // direction toward the live bearing gets both: no tight per-tick feedback loop, but it still
            // adapts over roughly a second as the target's path curves.
            if (toTargetNow.sqrMagnitude > 0.001f)
            {
                var liveDirection = toTargetNow.normalized;
                _approachDirection = _approachDirection == null
                    ? liveDirection
                    : Vector3.Slerp(_approachDirection.Value, liveDirection, Time.deltaTime);
            }
            else if (_approachDirection == null)
            {
                _approachDirection = NpcGo.transform.forward;
            }

            return targetPos - _approachDirection.Value * _configService.Dev.NpcDialogStopDistance;
        }

        protected override void OnDestinationReached()
        {
            if (_route != null && _route.Count > 0)
            {
                var reached = _route.Pop();
                var reachedWp = WayNetService.GetWayNetPoint(reached.Name) as WayPoint;
                if (reachedWp != null)
                    Props.CurrentWayPoint = reachedWp;

                // Note: the close-enough/LOS bail-out lives in GetWalkDestination() now, checked every
                // tick — if it had fired, _route would already be null and this whole branch wouldn't
                // run. This block only handles genuinely continuing the route to a further-away target.

                // Re-evaluate the remaining route from here instead of blindly continuing the stale
                // stack: for a moving target (e.g. ZS_FollowPC_Loop re-issuing AI_GotoNpc(self, hero)
                // every ~1s), the destination may have moved to a different waypoint mid-route.
                // Without this, a follower walks the whole originally-computed path to a now-outdated
                // spot instead of tracking the target hop by hop.
                var targetWp = reachedWp != null ? WayNetService.FindNearestWayPoint(_destinationTransform.position) : null;
                if (reachedWp != null && targetWp != null && reachedWp.Name != targetWp.Name)
                {
                    var path = WayNetService.FindFastestPath(reachedWp.Name, targetWp.Name);
                    _route = path != null && path.Length > 0 ? new Stack<DijkstraWaypoint>(path) : null;
                }
                else
                {
                    _route = null;
                }

                IsDestReached = false;
                return;
            }

            base.OnDestinationReached();
            IsFinishedFlag = true;
        }

        public override void Tick()
        {
            base.Tick();
            if (IsFinishedFlag || !IsPursuitOver())
                return;

            // ZS_ProclaimAndPunish_Loop gives up after HAI_TIME_PURSUIT (AI_ContinueRoutine), but it only runs again
            // once this AI_GotoNpc is done - which was on arrival: guards/orcs chased the hero all the way and put their
            // weapon away right next to him. Stop here, the loop gives up mid-chase like in the engine.
            Logger.Log($"[GoToNpc] {NpcInstance.GetName(NpcNameSlot.Slot0)}: pursuit over after {Props.StateTime:F0} s " +
                       "- stopping so ZS_ProclaimAndPunish gives up", LogCat.Ai);
            StopWalk();
            IsFinishedFlag = true;

            // A goodbye like a fighter giving up (ZS_Attack: "$RUNCOWARD") - plays before the loop's AI_ContinueRoutine,
            // the loop only runs again once the queue is empty. Orc voices (17) have no $RUNCOWARD - "$GETOUTOFHERE".
            var giveUpSvm = HasSvm(_giveUpSvm) ? _giveUpSvm : _giveUpFallbackSvm;
            _dialogService.ExtAiOutputSvm(NpcInstance, (NpcInstance)GameStateService.GothicVm.GlobalHero, giveUpSvm);

            // DeveloperConfig.EnableChaseGiveUpCooldown: no PERC_ASSESSENEMY right away - it restarted the chase at once.
            if (_configService.Dev.EnableChaseGiveUpCooldown)
                Props.HeroChaseGivenUpTime = Time.time;
        }

        private bool IsPursuitOver()
        {
            if (!IsInPursuitLoop() || !Props.IsStateTimeActive)
                return false;

            _pursuitSeconds ??= GameStateService.GothicVm.GetSymbolByName(_pursuitTimeName)?.GetInt(0) ??
                                _defaultPursuitSeconds;
            return Props.StateTime > _pursuitSeconds;
        }

        private bool IsInPursuitLoop()
        {
            if (Props.CurrentLoopState != NpcProperties.LoopState.Loop)
                return false;

            _pursuitLoopIndex ??= GameStateService.GothicVm.GetSymbolByName(_pursuitLoopName)?.Index ?? -1;
            return Props.StateLoop == _pursuitLoopIndex;
        }

        /// <summary>
        /// Orcs threaten before they run at the hero (T_WARN like in ZS_Orc_TA/ZS_MM_*, and their war cry
        /// "$DIEMORTALENEMY") - vanilla ZS_ProclaimAndPunish only shouts on arrival, the orcs just started running.
        /// Once per chase: this AI_GotoNpc turns into turn + warn, the loop's next AI_GotoNpc runs.
        /// </summary>
        private bool TryWarnBeforePursuit()
        {
            if (!_configService.Dev.EnablePursuitWarn || NpcInstance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_ORC ||
                !IsInPursuitLoop() || Time.time - Props.LastPursuitWarnTime < _pursuitWarnRepeatSeconds)
                return false;

            Props.LastPursuitWarnTime = Time.time;
            var hero = (NpcInstance)GameStateService.GothicVm.GlobalHero;
            if (HasSvm(_warnSvm))
                _dialogService.ExtAiOutputSvmOverlay(NpcInstance, hero, _warnSvm);
            _npcAiService.ExtAiTurnToNpc(NpcInstance, hero);
            Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(_warnAnimName), NpcContainer));

            Logger.Log($"[GoToNpc] {NpcInstance.GetName(NpcNameSlot.Slot0)}: warns the hero before the chase", LogCat.Ai);
            IsFinishedFlag = true;
            return true;
        }

        private bool HasSvm(string svmName)
        {
            return !string.IsNullOrEmpty(VmCacheService.TryGetSvmData(NpcInstance.Voice)?.GetAudioName(svmName));
        }

        private const string _pursuitLoopName = "ZS_PROCLAIMANDPUNISH_LOOP";
        private const string _giveUpSvm = "$RUNCOWARD";
        private const string _giveUpFallbackSvm = "$GETOUTOFHERE";
        private const string _warnSvm = "$DIEMORTALENEMY";
        private const string _warnAnimName = "T_WARN";
        private const float _pursuitWarnRepeatSeconds = 15f;
        private const string _pursuitTimeName = "HAI_TIME_PURSUIT";
        private const int _defaultPursuitSeconds = 20;
        private int? _pursuitLoopIndex;
        private int? _pursuitSeconds;
    }
}
