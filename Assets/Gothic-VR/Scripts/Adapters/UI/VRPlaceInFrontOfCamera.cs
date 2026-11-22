using Gothic.Core.Adapters.UI.Menus;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Places this object once in front of the VR camera, upright at eye height and facing it, when enabled (e.g. main
    /// menu, video cinema screen). It doesn't follow head turns or movement afterwards. Only a teleport places it anew.
    /// For UI that follows the head, use VRFollowCamera.
    /// On Gothic's menu (MenuHandler), the turning hand's stick zooms it: down pulls it closer, up pushes it away
    /// (1-8 m) - far away the save list was hard to read. Teleporting is off meanwhile, the same stick would aim it.
    /// The distance is kept in Gothic.ini (menuDistance).
    /// </summary>
    public class VRPlaceInFrontOfCamera : MonoBehaviour
    {
        // A bigger jump of the camera is a teleport (e.g. the hero is moved to the chapter start) - face it anew.
        private const float _teleportDistance = 5f;

        private const float _minZoomDistance = 1f;
        private const float _maxZoomDistance = 8f;
        // Meters per second at a fully pushed stick.
        private const float _zoomSpeed = 2.5f;
        private const float _zoomStickDeadZone = 0.2f;

        [Tooltip("Distance in front of the camera in meters.")]
        [SerializeField] private float _distance = 1.5f;

        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly ConfigService _configService;

        private Transform _cameraTransform;
        private Vector3 _lastCameraPosition;
        private Vector3 _placedForward = Vector3.forward;
        private bool _isPlaced;
        private bool _isZoomable;
        private bool _isZoomDirty;
        private IVrRig _blockedTeleportRig;
        private bool _wasTeleportEnabled;

        private void Awake()
        {
            // Only Gothic's menu zooms - not the video cinema's screen.
            _isZoomable = GetComponent<MenuHandler>() != null;
        }

        private void OnEnable()
        {
            _isPlaced = false;
            if (_isZoomable)
                ReadZoomDistance();
            TryPlace();
        }

        private void OnDisable()
        {
            if (!_isZoomable)
                return;

            PersistZoomDistance();
            RestoreTeleport();
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
            if (_isZoomable)
                UpdateZoom();

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
            _placedForward = forward;
            transform.SetPositionAndRotation(_lastCameraPosition + forward * _distance,
                Quaternion.LookRotation(forward, Vector3.up));
            _isPlaced = true;

            if (_isZoomable)
                BlockTeleport();
        }

        private void UpdateZoom()
        {
            if (_vrInput == null || !_isPlaced)
                return;

            var stick = _vrInput.GetThumbstick(_vrInput.ControlLayout.TurnSide);
            if (Mathf.Abs(stick.y) < _zoomStickDeadZone)
            {
                // The stick is released: keep what was set.
                PersistZoomDistance();
                return;
            }

            // Up pushes the menu away, down pulls it closer.
            _distance = Mathf.Clamp(_distance + stick.y * _zoomSpeed * Time.unscaledDeltaTime, _minZoomDistance,
                _maxZoomDistance);
            transform.position = _lastCameraPosition + _placedForward * _distance;
            _isZoomDirty = true;
        }

        private void ReadZoomDistance()
        {
            if (_configService?.Gothic == null)
                return;

            _distance = Mathf.Clamp(_configService.Gothic.GetFloat(VRConstants.IniNames.MenuDistance, _distance),
                _minZoomDistance, _maxZoomDistance);
        }

        private void PersistZoomDistance()
        {
            if (!_isZoomDirty)
                return;

            _isZoomDirty = false;
            _configService?.Gothic?.SetFloat(VRConstants.IniSectionAccessibility, VRConstants.IniNames.MenuDistance,
                _distance);
            Logger.Log($"[Menu] Zoomed to {_distance:0.0} m.", LogCat.VR);
        }

        /// <summary>
        /// The zooming stick is the teleporting one - while the menu is open it only zooms. Restored when it closes.
        /// </summary>
        private void BlockTeleport()
        {
            if (_blockedTeleportRig != null)
                return;

            var rig = _contextInteractionService?.GetImpl<VRContextInteractionService>()?.Rig;
            if (rig == null)
                return;

            _blockedTeleportRig = rig;
            _wasTeleportEnabled = rig.TeleportEnabled;
            rig.TeleportEnabled = false;
        }

        private void RestoreTeleport()
        {
            if (_blockedTeleportRig == null)
                return;

            _blockedTeleportRig.TeleportEnabled = _wasTeleportEnabled;
            _blockedTeleportRig = null;
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
