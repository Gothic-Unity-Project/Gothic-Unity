using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Keeps body-worn things (hip holsters, shoulder slots) at the player's body: below the head, turned with the
    /// head's yaw, but not with its pitch or roll. Looking down at the hip doesn't move the holsters away.
    /// While the head is pitched far up or down, its yaw is unreliable, so the anchor only follows when the head
    /// turned far away from it, and then slowly.
    /// </summary>
    public class VrBodyAnchor : MonoBehaviour
    {
        [Tooltip("The player's head (camera).")]
        [SerializeField] private Transform _head;

        [Tooltip("Meters below the head.")]
        [SerializeField] private float _heightBelowHead = 0.6f;

        [Tooltip("Up to this head pitch (degrees, up or down), the anchor turns with the head immediately.")]
        [SerializeField] private float _pitchThreshold = 30f;

        [Tooltip("Above the pitch threshold, the anchor only turns once the head's yaw is this far away (degrees).")]
        [SerializeField] private float _catchUpAngle = 70f;

        [Tooltip("Turn speed (degrees per second) while catching up with the head.")]
        [SerializeField] private float _catchUpSpeed = 90f;

        public Transform Head
        {
            get => _head;
            set => _head = value;
        }

        private void OnEnable()
        {
            if (_head == null)
                return;

            transform.SetPositionAndRotation(GetTargetPosition(), Quaternion.Euler(0f, _head.eulerAngles.y, 0f));
        }

        private void LateUpdate()
        {
            if (_head == null)
                return;

            transform.SetPositionAndRotation(GetTargetPosition(), GetTargetRotation());
        }

        private Vector3 GetTargetPosition()
        {
            return _head.position + Vector3.down * _heightBelowHead;
        }

        private Quaternion GetTargetRotation()
        {
            // DeveloperConfig.EnableBodyAnchorLockedToHead: always the head's yaw at once, also looking down at the
            // hips - the anchor lagged behind the head (slow catch-up) and the holsters had to be "turned in" by head.
            if (IsLockedToHead())
            {
                var lockedYaw = GetHeadYawDirection();
                return lockedYaw.sqrMagnitude < 0.0001f
                    ? transform.rotation
                    : Quaternion.LookRotation(lockedYaw, Vector3.up);
            }

            var headForward = _head.forward;
            var flatForward = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
                return transform.rotation;

            var headYaw = Quaternion.LookRotation(flatForward, Vector3.up);
            var pitch = Vector3.Angle(flatForward, headForward);
            if (pitch <= _pitchThreshold)
                return headYaw;

            var current = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (Quaternion.Angle(current, headYaw) <= _catchUpAngle)
                return current;

            return Quaternion.RotateTowards(current, headYaw, _catchUpSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Where the face points, flat: the forward flattened, plus the head's up flattened - looking down the up points
        /// ahead (looking up, behind), so the direction stays right where the forward alone gets unreliable.
        /// </summary>
        private Vector3 GetHeadYawDirection()
        {
            var flatForward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            var flatUp = Vector3.ProjectOnPlane(_head.up, Vector3.up) * (_head.forward.y < 0f ? 1f : -1f);
            var direction = flatForward + flatUp;
            return direction.sqrMagnitude < 0.0001f ? Vector3.zero : direction.normalized;
        }

        private bool _isLockedToHeadResolved;
        private bool _isLockedToHead;

        private bool IsLockedToHead()
        {
            if (_isLockedToHeadResolved)
                return _isLockedToHead;

            var config = Gothic.Core.ReflexProjectInstaller.DIContainer
                ?.Resolve<Gothic.Core.Services.Config.ConfigService>();
            if (config?.Dev == null)
                return false;
            _isLockedToHead = config.Dev.EnableBodyAnchorLockedToHead;
            _isLockedToHeadResolved = true;
            return _isLockedToHead;
        }
    }
}
