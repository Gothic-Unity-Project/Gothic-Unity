using Gothic.Core.Models.Movement;
using Gothic.Core.Services.Movement;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// The VR player as Gothic's climber: the feet, body and view LedgeService's probes need. Owned by VrLedgeReach
    /// (null there while ledge climbing is off), shared with VrLedgeClimb and VrLedgeLocomotion.
    /// A crouch doesn't make the body smaller: the player stands up on top.
    /// </summary>
    public class VrLedgeClimber
    {
        private readonly LedgeService _ledgeService;
        private readonly int _collidingLayers;

        /// <param name="collidingLayers">Layers the player's body collides with.</param>
        public VrLedgeClimber(IVrRig rig, LedgeService ledgeService, int collidingLayers)
        {
            Rig = rig;
            _ledgeService = ledgeService;
            _collidingLayers = collidingLayers;
        }

        public IVrRig Rig { get; }

        public LedgeRules Rules => _ledgeService.HeroRules;

        public Vector3 Feet
        {
            get
            {
                var controller = Rig.CharacterController;
                return controller.transform.TransformPoint(controller.center) -
                       Vector3.up * (controller.height * 0.5f);
            }
        }

        public Quaternion HeadYaw => Quaternion.Euler(0f, Rig.Head.eulerAngles.y, 0f);

        private LedgeBody Body
        {
            get
            {
                var controller = Rig.CharacterController;
                var height = Mathf.Max(controller.height, Rig.StandingCameraHeight);
                return new LedgeBody(controller.radius, height, _collidingLayers);
            }
        }

        /// <summary>
        /// What Gothic's jump does here, looking along the head.
        /// </summary>
        public LedgeInfo ProbeAhead()
        {
            return _ledgeService.ProbeAhead(Feet, Rig.Head.forward, Body);
        }

        /// <summary>
        /// The lip a hand reaches from the head, counted from the floor at baseFeetY.
        /// </summary>
        public LedgeInfo ProbeAtHand(Vector3 hand, float baseFeetY)
        {
            var approach = hand - Rig.Head.position;
            approach.y = 0f;
            if (approach.sqrMagnitude < 0.01f)
                approach = Rig.Head.forward;

            return _ledgeService.ProbeEdge(hand, approach, baseFeetY, Body);
        }

        /// <summary>
        /// The point is on top of a ledge the player can climb from where they stand.
        /// </summary>
        public bool IsClimbableTop(Vector3 point)
        {
            return _ledgeService.ProbeTop(point, Feet, Body).IsClimbable;
        }

        /// <summary>
        /// Gothic's CLIMB_HEADING_ANGLE: the player looks at the wall.
        /// </summary>
        public bool IsFacing(LedgeInfo info)
        {
            var forward = Rig.Head.forward;
            forward.y = 0f;
            return Vector3.Angle(forward, -info.WallNormal) <= Rules.ClimbHeadingAngle;
        }

        /// <summary>
        /// Probes the lip again (things may have moved) for the floor on top where the player fits.
        /// </summary>
        public bool TryGetStandPoint(LedgeInfo info, out Vector3 standPoint)
        {
            var nearLip = info.EdgePoint + info.WallNormal * 0.05f + Vector3.up * 0.05f;
            var baseFeetY = info.EdgePoint.y - info.Height;
            var probe = _ledgeService.ProbeEdge(nearLip, -info.WallNormal, baseFeetY, Body);
            standPoint = probe.StandPoint;
            return probe.IsClimbable;
        }

        public static Vector3 ClosestOnLip(LedgeInfo info, Vector3 point)
        {
            return info.EdgePoint + info.EdgeDirection * Vector3.Dot(point - info.EdgePoint, info.EdgeDirection);
        }
    }
}
