using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// Thrown away by a DAM_FLY hit (Windfist, Stormfist, a troll's blow), like the engine (OpenGothic
    /// MoveAlgo::accessDamFly): the NPC flies off the attacker at 7.5 m/s, a bit upwards, in its fall animation
    /// (S_FALL, or S_FALLB when hit from the front), lands lying (T_FALL_2_FALLEN) and gets up (T_FALLEN_2_STAND).
    /// Landing from high up hurts like any fall. Found on BoroBongo's branch.
    /// </summary>
    public class FlyBack : AbstractAnimationAction
    {
        // OpenGothic: 0.75 cm/ms along (dx, len / 2, dz).
        private const float _speed = 7.5f;
        // The NPC is in the air at least this long, at most this long before it counts as landed.
        private const float _minFlightSeconds = 0.25f;
        private const float _maxFlightSeconds = 4f;
        // Landed: back at the standing height above the ground (m) and no longer rising.
        private const float _landedHeightTolerance = 0.2f;
        private const float _lieSeconds = 1f;

        private readonly Vector3 _away;
        private string _suffix;
        private float _phaseTime;
        private int _phase;
        private float _getUpSeconds;
        private float _standingHeight;

        /// <param name="away">Horizontal direction from the attacker to the victim.</param>
        public FlyBack(AnimationAction action, NpcContainer npcContainer, Vector3 away) : base(action, npcContainer)
        {
            _away = new Vector3(away.x, 0f, away.z).normalized;
        }

        public override void Start()
        {
            base.Start();

            // Hit from the front, it falls backwards.
            _suffix = Vector3.Dot(NpcGo.transform.forward, _away) < 0f ? "B" : "";
            if (_away.sqrMagnitude < 0.0001f || !PrefabProps.AnimationSystem.PlayAnimation($"S_FALL{_suffix}"))
            {
                IsFinishedFlag = true;
                return;
            }

            _standingHeight = GetHeightAboveGround() ?? 1f;
            Props.BodyState = VmGothicEnums.BodyState.BsFall;
            PhysicsService.EnablePhysicsForNpc(PrefabProps);
            var body = PrefabProps.ColliderRootMotion.GetComponent<Rigidbody>();
            body.linearVelocity = (_away + Vector3.up * 0.5f).normalized * _speed;
        }

        public override void Tick()
        {
            base.Tick();
            if (IsFinishedFlag)
                return;

            _phaseTime += Time.deltaTime;
            switch (_phase)
            {
                case 0 when IsLanded() || _phaseTime >= _maxFlightSeconds:
                    PrefabProps.AnimationSystem.StopAnimation($"S_FALL{_suffix}");
                    PrefabProps.AnimationSystem.PlayAnimation($"T_FALL{_suffix}_2_FALLEN{_suffix}");
                    Props.BodyState = VmGothicEnums.BodyState.BsLie;
                    NextPhase();
                    break;
                case 1 when _phaseTime >= _lieSeconds:
                    var getUp = $"T_FALLEN{_suffix}_2_STAND";
                    _getUpSeconds = PrefabProps.AnimationSystem.PlayAnimation(getUp)
                        ? PrefabProps.AnimationSystem.GetAnimationDuration(getUp)
                        : 0f;
                    NextPhase();
                    break;
                case 2 when _phaseTime >= _getUpSeconds:
                    Props.BodyState = VmGothicEnums.BodyState.BsStand;
                    PrefabProps.AnimationSystem.PlayIdleAnimation();
                    IsFinishedFlag = true;
                    break;
            }
        }

        public override void StopImmediately()
        {
            if (Props.BodyState is VmGothicEnums.BodyState.BsFall or VmGothicEnums.BodyState.BsLie)
                Props.BodyState = VmGothicEnums.BodyState.BsStand;
            base.StopImmediately();
        }

        private bool IsLanded()
        {
            if (_phaseTime < _minFlightSeconds)
                return false;

            var body = PrefabProps.ColliderRootMotion.GetComponent<Rigidbody>();
            if (body.isKinematic)
                return true;

            var height = GetHeightAboveGround();
            return body.linearVelocity.y <= 0.1f && height != null &&
                   height.Value <= _standingHeight + _landedHeightTolerance;
        }

        private float? GetHeightAboveGround()
        {
            return Physics.Raycast(NpcGo.transform.position + Vector3.up * 0.1f, Vector3.down, out var hit, 50f,
                1 << Constants.DefaultLayer)
                ? hit.distance - 0.1f
                : null;
        }

        private void NextPhase()
        {
            _phase++;
            _phaseTime = 0f;
        }
    }
}
