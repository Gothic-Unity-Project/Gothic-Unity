using Gothic.Core.Services.Context;
using Gothic.VR.Services.Context;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Keeps this object in front of the VR camera and facing it, while it's enabled (e.g. PrintScreen, chapter
    /// introduction, in-game menu). By default, it only follows head turns left/right and stays upright at eye height.
    /// It waits until the head turned/moved beyond a threshold. Then it recenters in front of us. Small head wobbles
    /// therefore don't move it, which keeps laser pointing at it steady.
    /// Movement and rotation are damped - it feels less stiff than parenting it to the head.
    /// When enabled, it starts directly in front of the camera. Otherwise, it would fly in from its last position.
    /// </summary>
    public class VRFollowCamera : MonoBehaviour
    {
        // Recentering stops once we're this close to the target.
        private const float _settledDistance = 0.01f;
        private const float _settledAngle = 1f;

        [Tooltip("Distance in front of the camera in meters.")]
        [SerializeField] private float _distance = 1.5f;
        [Tooltip("Seconds the object needs to catch up with head movement. Higher values feel more floaty.")]
        [SerializeField] private float _moveSmoothTime = 0.25f;
        [Tooltip("Speed of the rotation catching up with the head. Higher values follow quicker.")]
        [SerializeField] private float _rotationSpeed = 6f;
        [Tooltip("Also follow looking up/down. Otherwise, it only follows left/right and stays at eye height.")]
        [SerializeField] private bool _isFollowingPitch;
        [Tooltip("Degrees the head needs to turn away before it follows. 0 follows every movement.")]
        [SerializeField] private float _angleThreshold = 15f;
        [Tooltip("Meters the head needs to move (e.g. walking, leaning) before it follows. 0 follows every movement.")]
        [SerializeField] private float _positionThreshold = 0.3f;

        [Inject] private readonly ContextInteractionService _contextInteractionService;

        private Transform _cameraTransform;
        private Vector3 _moveVelocity;
        // Where we place the object: Camera position and direction of the last recentering.
        private Vector3 _targetOrigin;
        private Vector3 _targetForward = Vector3.forward;
        private bool _isRecentering;

        private void OnEnable()
        {
            Follow(true);
        }

        private void LateUpdate()
        {
            Follow(false);
        }

        private void Follow(bool isImmediate)
        {
            // The camera might not exist (or we're not injected yet) when enabled. Snap once we have it.
            var isFirstFollow = _cameraTransform == null;
            if (!TryResolveCamera())
                return;
            isImmediate |= isFirstFollow;

            var cameraPosition = _cameraTransform.position;
            var forward = GetCameraForward();

            if (isImmediate || _isRecentering || IsBeyondThreshold(cameraPosition, forward))
            {
                _targetOrigin = cameraPosition;
                _targetForward = forward;
                _isRecentering = !isImmediate;
            }

            var targetPosition = _targetOrigin + _targetForward * _distance;
            var targetRotation = Quaternion.LookRotation(_targetForward, Vector3.up);

            if (isImmediate)
            {
                _moveVelocity = Vector3.zero;
                transform.SetPositionAndRotation(targetPosition, targetRotation);
                return;
            }

            var pos = Vector3.SmoothDamp(transform.position, targetPosition, ref _moveVelocity, _moveSmoothTime);
            var rotationLerp = 1f - Mathf.Exp(-_rotationSpeed * Time.deltaTime);
            var rot = Quaternion.Slerp(transform.rotation, targetRotation, rotationLerp);
            transform.SetPositionAndRotation(pos, rot);

            // Keep following until we're in front of the head again. Then wait for the next threshold.
            if (_isRecentering && Vector3.Distance(pos, targetPosition) < _settledDistance &&
                Quaternion.Angle(rot, targetRotation) < _settledAngle)
                _isRecentering = false;
        }

        private bool IsBeyondThreshold(Vector3 cameraPosition, Vector3 forward)
        {
            return Vector3.Angle(_targetForward, forward) > _angleThreshold ||
                   Vector3.Distance(_targetOrigin, cameraPosition) > _positionThreshold;
        }

        private Vector3 GetCameraForward()
        {
            // Without the camera's roll - text stays upright when the head tilts sideways.
            if (_isFollowingPitch)
                return _cameraTransform.forward;

            // Only follow left/right (yaw). Looking up or down (e.g. at a lower menu button) doesn't drag it along.
            var flatForward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up);

            // Looking straight up/down has no horizontal direction. Keep the current one.
            return flatForward.sqrMagnitude < 0.0001f ? _targetForward : flatForward.normalized;
        }

        private bool TryResolveCamera()
        {
            if (_cameraTransform != null)
                return true;

            var rig = _contextInteractionService?.GetImpl<VRContextInteractionService>().Rig;
            if (rig == null)
                return false;

            _cameraTransform = rig.Head;
            return _cameraTransform != null;
        }
    }
}
