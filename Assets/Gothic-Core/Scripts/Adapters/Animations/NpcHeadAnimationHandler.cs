using Gothic.Core.Adapters.Npc;
using UnityEngine;

namespace Gothic.Core.Animations
{
    /// <summary>
    /// Turns the NPC's head towards a target on top of the animated pose.
    /// Two sources: Daedalus (AI_LookAt/AI_LookAtNpc/AI_StopLookAt) and the dialog itself. The dialog only fills in
    /// when Daedalus isn't looking at something already, and stopping it never ends what Daedalus started.
    /// </summary>
    public class NpcHeadAnimationHandler : BasePlayerBehaviour
    {
        private Transform _scriptTarget;

        /// <summary>
        /// What the scripts made the NPC look at (Npc_GetLookAtTarget), null if nothing.
        /// </summary>
        public Transform ScriptTarget => _scriptTarget;
        private Transform _dialogTarget;
        // Current head offset against the body's forward direction, eased towards the target angles.
        private float _yaw;
        private float _pitch;

        private const float _headLookYawMax = 80f;
        private const float _headLookPitchMax = 30f;
        private const float _headLookRotateSpeed = 120f;

        protected override void Awake()
        {
            base.Awake();

            // Cached object which will be used later.
            NpcData.PrefabProps.AnimationHeadHandler = this;
        }

        /// <summary>
        /// Daedalus: AI_LookAt / AI_LookAtNpc.
        /// </summary>
        public void StartLookAt(Transform destinationTransform)
        {
            _scriptTarget = destinationTransform;
        }

        /// <summary>
        /// Daedalus: AI_StopLookAt. The head eases back into the animation.
        /// </summary>
        public void StopLookAt()
        {
            _scriptTarget = null;
        }

        public void StartDialogLookAt(Transform destinationTransform)
        {
            _dialogTarget = destinationTransform;
        }

        public void StopDialogLookAt()
        {
            _dialogTarget = null;
        }

        /// <summary>
        /// The Animator poses the head after Update(), so the offset is added in LateUpdate().
        /// Only active during dialogs for now: Daedalus' look-ats outside of them would also turn monster heads
        /// (the Troll did an awkward head rotation).
        /// Outside the yaw range (e.g. hero behind the NPC) or without a target, the head eases back to the animation.
        /// </summary>
        private void LateUpdate()
        {
            if (PrefabProps.Head == null)
                return;

            var target = _dialogTarget == null ? null : _scriptTarget != null ? _scriptTarget : _dialogTarget;

            var targetYaw = 0f;
            var targetPitch = 0f;
            if (target != null)
                GetLookAngles(target.position, out targetYaw, out targetPitch);

            var step = _headLookRotateSpeed * Time.deltaTime;
            _yaw = Mathf.MoveTowards(_yaw, targetYaw, step);
            _pitch = Mathf.MoveTowards(_pitch, targetPitch, step);

            if (_yaw == 0f && _pitch == 0f)
                return;

            // Rotating around the body's axes instead of the head bone's keeps it independent of how the bone is
            // oriented in the model.
            var body = Go.transform;
            var offset = Quaternion.AngleAxis(_yaw, Vector3.up) * Quaternion.AngleAxis(_pitch, body.right);
            PrefabProps.Head.rotation = offset * PrefabProps.Head.rotation;
        }

        private void GetLookAngles(Vector3 targetPosition, out float yaw, out float pitch)
        {
            yaw = 0f;
            pitch = 0f;

            var direction = targetPosition - PrefabProps.Head.position;
            var flatDirection = new Vector3(direction.x, 0f, direction.z);
            if (flatDirection.sqrMagnitude < 0.0001f)
                return;

            var bodyForward = Go.transform.forward;
            bodyForward.y = 0f;
            var angle = Vector3.SignedAngle(bodyForward, flatDirection, Vector3.up);
            if (Mathf.Abs(angle) > _headLookYawMax)
                return;

            yaw = angle;
            // Positive pitch around the body's right axis looks down.
            pitch = Mathf.Clamp(-Mathf.Atan2(direction.y, flatDirection.magnitude) * Mathf.Rad2Deg,
                -_headLookPitchMax, _headLookPitchMax);
        }
    }
}
