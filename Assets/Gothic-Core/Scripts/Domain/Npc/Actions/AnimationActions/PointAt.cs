using System;
using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_PointAt / AI_PointAtNpc, like the engine (OpenGothic Npc::implPointAt): the NPC turns to the waypoint or NPC
    /// and points at it (T_POINT) - a guard showing the hero the way out, Guy his hut. AI_StopPointAt lowers the arm.
    /// </summary>
    public class PointAt : AbstractRotateAnimationAction
    {
        public const string PointAnimation = "T_POINT";

        private readonly Func<Vector3?> _getTarget;
        private bool _isPointing;

        /// <param name="getTarget">Where to point - null if the target is gone.</param>
        public PointAt(AnimationAction action, NpcContainer npcContainer, Func<Vector3?> getTarget)
            : base(action, npcContainer)
        {
            _getTarget = getTarget;
        }

        public override void Start()
        {
            base.Start();

            if (IsFinishedFlag)
                Point();
        }

        public override void Tick()
        {
            base.Tick();

            if (IsFinishedFlag && !_isPointing)
                Point();
        }

        protected override Quaternion GetRotationDirection()
        {
            var target = _getTarget();
            if (target == null)
                return NpcGo.transform.rotation;

            var direction = target.Value - NpcGo.transform.position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up)
                : NpcGo.transform.rotation;
        }

        private void Point()
        {
            _isPointing = true;
            PrefabProps.AnimationSystem.PlayAnimation(PointAnimation);
        }
    }
}
