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
    }
}
