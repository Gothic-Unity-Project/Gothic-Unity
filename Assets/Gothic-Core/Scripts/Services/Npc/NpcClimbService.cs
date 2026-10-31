using System.Collections.Generic;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Movement;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Movement;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// NPCs climb ledges like the engine (OpenGothic MoveAlgo::onMoveFailed and startClimb): a walking NPC that runs
    /// head-on into a wall asks Gothic's jump probe (Npc::tryJump) and climbs a ledge below its guild's JUMPLOW_HEIGHT
    /// (a big step up) or JUMPMID_HEIGHT (a pull-up) with the T_STAND_2_JUMPUPLOW / T_STAND_2_JUMPUPMID animations, and
    /// up to JUMPUP_HEIGHT + JUMPLOW_HEIGHT it jumps up, hangs at the lip and pulls itself up (T_STAND_2_JUMPUP, S_HANG,
    /// T_HANG_2_STAND - OpenGothic JumpUp and JumpHang). It rises to the top during the animations and stands 55 cm
    /// behind the lip at their end. Before, chasing NPCs stood at every crate and rock. Found on BoroBongo's branch.
    /// </summary>
    public class NpcClimbService
    {
        // A wall within this angle of the walk direction is head-on (CLIMB_HEADING_ANGLE of the human guild).
        private const float _headOnAngle = 45f;
        // After a wall that can't be climbed, the probe waits this long (seconds) before the NPC tries again.
        private const float _probeCooldown = 1f;
        // Share of the climb animation in which the NPC rises to the top, and from which it moves onto it.
        private const float _riseEnd = 0.6f;
        private const float _moveOnStart = 0.4f;
        // High ledges: how long the NPC hangs at the lip before pulling itself up.
        private const float _hangSeconds = 0.3f;
        private const string _jumpUpAnimation = "T_STAND_2_JUMPUP";
        private const string _hangAnimation = "S_HANG";
        private const string _pullUpAnimation = "T_HANG_2_STAND";

        [Inject] private readonly LedgeService _ledgeService;
        [Inject] private readonly AnimationService _animationService;

        // RiseEnd / MoveOnStart: in s. PullUpTime: when a high climb's hang goes over into the pull-up (0 otherwise).
        private readonly Dictionary<NpcContainer, (Vector3 Start, Vector3 Target, float Time, float Duration,
            float RiseEnd, float MoveOnStart, float HangTime, float PullUpTime, int Step)> _climbs = new();
        private readonly Dictionary<NpcContainer, float> _nextProbeTimes = new();

        public bool IsClimbing(NpcContainer npc)
        {
            return _climbs.ContainsKey(npc);
        }

        /// <summary>
        /// The NPC walks along walkDirection and a wall with this normal blocks it. Starts a climb if the wall is
        /// head-on and a ledge Gothic climbs. feet: the NPC's feet, bodyRadius: its probe radius.
        /// </summary>
        public bool TryStartClimb(NpcContainer npc, Vector3 feet, Vector3 walkDirection, Vector3 wallNormal,
            float bodyRadius, float bodyHeight)
        {
            if (npc?.Go == null || IsClimbing(npc) || npc.Vob?.AiHuman == null)
                return false;
            if (Vector3.Angle(walkDirection, -wallNormal) > _headOnAngle)
                return false;
            if (npc.Vob.AiHuman.WaterLevel == (int)ZenGineConst.WaterLevel.Chest)
                return false;
            if (_nextProbeTimes.TryGetValue(npc, out var nextProbeTime) && Time.time < nextProbeTime)
                return false;

            var rules = _ledgeService.GetRules(npc.Instance.GetValuesGuild());
            var body = new LedgeBody(bodyRadius, bodyHeight, 1 << Constants.DefaultLayer);
            var ledge = _ledgeService.ProbeAhead(feet, walkDirection, body, rules);
            var animationName = ledge.Band switch
            {
                LedgeBand.Low => "T_STAND_2_JUMPUPLOW",
                LedgeBand.Mid => "T_STAND_2_JUMPUPMID",
                LedgeBand.High => _jumpUpAnimation,
                _ => null
            };

            float duration, riseEnd, moveOnStart, hangTime = 0f, pullUpTime = 0f;
            if (ledge.Band == LedgeBand.High)
            {
                // Jump up, hang, pull up: rising until the hang, moving on during the pull-up.
                var jumpUp = GetDuration(npc, _jumpUpAnimation);
                var pullUp = GetDuration(npc, _pullUpAnimation);
                var hasHang = GetDuration(npc, _hangAnimation) > 0f;
                hangTime = jumpUp;
                pullUpTime = jumpUp + _hangSeconds;
                duration = jumpUp <= 0f || pullUp <= 0f || !hasHang ? 0f : pullUpTime + pullUp;
                riseEnd = pullUpTime;
                moveOnStart = pullUpTime;
            }
            else
            {
                duration = GetChainDuration(npc, animationName);
                riseEnd = duration * _riseEnd;
                moveOnStart = duration * _moveOnStart;
            }

            if (duration <= 0f || !npc.PrefabProps.AnimationSystem.PlayAnimation(animationName))
            {
                _nextProbeTimes[npc] = Time.time + _probeCooldown;
                return false;
            }

            var start = npc.Go.transform.position;
            var target = ledge.StandPoint + (start - feet);
            _climbs[npc] = (start, target, 0f, duration, riseEnd, moveOnStart, hangTime, pullUpTime, 0);
            npc.Props.BodyState = VmGothicEnums.BodyState.BsClimb;
            SetKinematic(npc, true);
            Logger.Log($"[NpcClimb] {npc.Instance.GetName(NpcNameSlot.Slot0)} climbs a {ledge.Band} ledge " +
                       $"({ledge.Height:F2} m).", LogCat.Ai);
            return true;
        }

        /// <summary>
        /// Called by the NPC's AnimationSystem each frame while it climbs, instead of its root motion.
        /// </summary>
        public void Tick(NpcContainer npc)
        {
            if (!_climbs.TryGetValue(npc, out var climb))
                return;
            if (npc.Go == null)
            {
                _climbs.Remove(npc);
                return;
            }

            climb.Time += Time.deltaTime;
            if (climb.PullUpTime > 0f)
                PlayHighClimbStep(npc, ref climb);

            var progress = Mathf.Clamp01(climb.Time / climb.Duration);
            var rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(climb.Time / climb.RiseEnd));
            var moveOn = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((climb.Time - climb.MoveOnStart) / (climb.Duration - climb.MoveOnStart)));
            var position = Vector3.Lerp(climb.Start, climb.Target, moveOn);
            position.y = Mathf.Lerp(climb.Start.y, climb.Target.y, rise);
            npc.Go.transform.position = position;

            if (progress < 1f)
            {
                _climbs[npc] = climb;
                return;
            }

            _climbs.Remove(npc);
            npc.Go.transform.position = climb.Target;
            npc.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            SetKinematic(npc, false);
        }

        /// <summary>
        /// High ledges: after the jump up the NPC hangs at the lip, then pulls itself up.
        /// </summary>
        private static void PlayHighClimbStep(NpcContainer npc,
            ref (Vector3 Start, Vector3 Target, float Time, float Duration, float RiseEnd, float MoveOnStart,
                float HangTime, float PullUpTime, int Step) climb)
        {
            var animationSystem = npc.PrefabProps.AnimationSystem;
            if (climb.Step == 0 && climb.Time >= climb.HangTime)
            {
                animationSystem.StopAnimation(_jumpUpAnimation);
                animationSystem.StopAnimation("S_JUMPUP");
                animationSystem.PlayAnimation(_hangAnimation);
                climb.Step = 1;
            }
            if (climb.Step == 1 && climb.Time >= climb.PullUpTime)
            {
                animationSystem.StopAnimation(_hangAnimation);
                animationSystem.PlayAnimation(_pullUpAnimation);
                climb.Step = 2;
            }
        }

        private float GetDuration(NpcContainer npc, string animationName)
        {
            var track = _animationService.GetTrack(animationName, npc.Props.MdsNameBase, npc.Props.MdsNameOverlay);
            return track?.Duration ?? 0f;
        }

        /// <summary>
        /// The climb animation and the ones following it (T_STAND_2_JUMPUPLOW, S_JUMPUPLOW, T_JUMPUPLOW_2_STAND).
        /// 0 if the model doesn't have it (most monsters can't pull up).
        /// </summary>
        private float GetChainDuration(NpcContainer npc, string animationName)
        {
            var duration = 0f;
            for (var i = 0; i < 3 && !string.IsNullOrEmpty(animationName); i++)
            {
                var track = _animationService.GetTrack(animationName, npc.Props.MdsNameBase, npc.Props.MdsNameOverlay);
                if (track == null)
                    break;
                duration += track.Duration;
                animationName = track.NextAni;
            }
            return duration;
        }

        private static void SetKinematic(NpcContainer npc, bool isKinematic)
        {
            var rootMotion = npc.PrefabProps?.ColliderRootMotion;
            if (rootMotion != null && rootMotion.TryGetComponent<Rigidbody>(out var body))
                body.isKinematic = isKinematic;
        }
    }
}
