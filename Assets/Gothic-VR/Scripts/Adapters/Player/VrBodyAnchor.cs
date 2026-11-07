using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Keeps body-worn things (hip holsters, shoulder slots) at the player's body: below the head, turned with the
    /// head's yaw, but not with its pitch or roll. Looking down at the hip doesn't move the holsters away.
    /// </summary>
    public class VrBodyAnchor : MonoBehaviour
    {
        [Tooltip("The player's head (camera).")]
        [SerializeField] private Transform _head;

        [Tooltip("Meters below the head.")]
        [SerializeField] private float _heightBelowHead = 0.6f;

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

        /// <summary>
        /// Always the head's yaw at once, also looking down at the hips - the anchor lagged behind the head (slow
        /// catch-up), the holsters had to be "turned in" with the head and the shoulder backpack came into view.
        /// </summary>
        private Quaternion GetTargetRotation()
        {
            var yaw = GetHeadYawDirection();
            return yaw.sqrMagnitude < 0.0001f ? transform.rotation : Quaternion.LookRotation(yaw, Vector3.up);
        }

        /// <summary>
        /// Where the face points, flat: perpendicular to the head's right axis, which stays level when looking up or
        /// down (the forward is unreliable there). Flipping the head's up at the horizon made a tilted head jitter.
        /// </summary>
        private Vector3 GetHeadYawDirection()
        {
            var direction = Vector3.Cross(_head.right, Vector3.up);
            return direction.sqrMagnitude < 0.0001f ? Vector3.zero : direction.normalized;
        }
    }
}
