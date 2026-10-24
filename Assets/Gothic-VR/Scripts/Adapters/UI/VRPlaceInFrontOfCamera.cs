using Gothic.Core.Services.Context;
using Gothic.VR.Services.Context;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Places this object once in front of the VR camera, upright at eye height and facing it, when enabled (e.g. main
    /// menu, video cinema screen). It doesn't follow head turns or movement afterwards. Only a teleport places it anew.
    /// For UI that follows the head, use VRFollowCamera.
    /// </summary>
    public class VRPlaceInFrontOfCamera : MonoBehaviour
    {
        // A bigger jump of the camera is a teleport (e.g. the hero is moved to the chapter start) - face it anew.
        private const float _teleportDistance = 5f;

        [Tooltip("Distance in front of the camera in meters.")]
        [SerializeField] private float _distance = 1.5f;

        [Inject] private readonly ContextInteractionService _contextInteractionService;

        private Transform _cameraTransform;
        private Vector3 _lastCameraPosition;
        private bool _isPlaced;

        private void OnEnable()
        {
            _isPlaced = false;
            TryPlace();
        }

        /// <summary>
        /// For objects created at runtime (no injection, no serialized values). Places it in front of the camera.
        /// </summary>
        public void Configure(Transform cameraTransform, float distance)
        {
            _cameraTransform = cameraTransform;
            _distance = distance;
            _isPlaced = false;
            TryPlace();
        }

        private void LateUpdate()
        {
            // The camera might not exist (or we're not injected yet) when enabled. Place it once we have it.
            if (_isPlaced && _cameraTransform != null &&
                Vector3.Distance(_cameraTransform.position, _lastCameraPosition) <= _teleportDistance)
                return;

            TryPlace();
        }

        private void TryPlace()
        {
            if (!TryResolveCamera())
                return;

            // Only left/right (yaw). Looking straight up/down has no horizontal direction.
            var forward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
            if (forward == Vector3.zero)
                forward = Vector3.forward;

            _lastCameraPosition = _cameraTransform.position;
            transform.SetPositionAndRotation(_lastCameraPosition + forward * _distance,
                Quaternion.LookRotation(forward, Vector3.up));
            _isPlaced = true;
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
