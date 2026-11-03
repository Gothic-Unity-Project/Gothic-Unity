using Gothic.Core.Models.Movement;
using Gothic.VR.Domain.Player;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// The rig's locomotion near ledges (IVrLedgeHandler, Docs/ledge-climbing.md):
    /// - Jump: in front of a lip above the reach of the hands (Gothic: jump up and hang), the jump lifts the hands
    ///   just above it. In comfort mode, a jump facing a climbable lip climbs it instead (VrLedgeClimb's blink).
    /// - Teleport: a target on top of a ledge the player can climb is valid, also above a jump's reach.
    /// Answers only while VrLedgeReach and VrLedgeClimb work: without grabbing, no comfort jump or ledge teleport.
    /// </summary>
    [RequireComponent(typeof(VrLedgeClimb))]
    public class VrLedgeLocomotion : MonoBehaviour, IVrLedgeHandler
    {
        [Tooltip("Reach in m of the hands above the head (arms up).")] [SerializeField]
        private float _armReach = 0.6f;

        [Tooltip("A jump towards a high lip lifts the hands this far (m) above it.")] [SerializeField]
        private float _jumpReachMargin = 0.15f;

        private VrLedgeClimber _climber;
        private VrLedgeReach _reach;
        private VrLedgeClimb _climb;

        private bool IsWorking => isActiveAndEnabled && _reach.isActiveAndEnabled && _climb.isActiveAndEnabled;

        private void Start()
        {
            _reach = GetComponent<VrLedgeReach>();
            _climb = GetComponent<VrLedgeClimb>();
            _climber = _reach.Climber;
            if (_climber == null)
            {
                enabled = false;
                return;
            }

            _climber.Rig.SetLedgeHandler(this);
        }

        private void OnDestroy()
        {
            _climber?.Rig.SetLedgeHandler(null);
        }

        public bool TryHandleJump()
        {
            if (!IsWorking || !_climb.IsComfortMode || _climb.IsBlinking)
                return false;

            var info = _climber.ProbeAhead();
            return info.IsClimbable && _climber.IsFacing(info) && _climb.TryBlinkOnto(info);
        }

        public float GetJumpRise()
        {
            if (!IsWorking || _climb.IsBlinking)
                return 0f;

            var info = _climber.ProbeAhead();
            if (info.Band != LedgeBand.High || !_climber.IsFacing(info))
                return 0f;

            var handsTop = _climber.Rig.Head.position.y + _armReach;
            var rise = info.EdgePoint.y + _jumpReachMargin - handsTop;
            return Mathf.Clamp(rise, 0f, _climber.Rules.JumpUpHeight);
        }

        public bool IsClimbableTop(Vector3 point)
        {
            return IsWorking && _climber.IsClimbableTop(point);
        }
    }
}
