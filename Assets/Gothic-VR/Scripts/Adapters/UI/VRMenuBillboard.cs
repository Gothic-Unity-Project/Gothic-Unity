#if GUZ_HVR_INSTALLED
using GUZ.Core;
using GUZ.Core.Services.Context;
using GUZ.VR.Adapters.HVROverrides;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.VR.Adapters.UI
{
    public class VRMenuBillboard : MonoBehaviour
    {
        [Header("Position Settings")]
        // Y offset will be ignored as we use the player height
        [SerializeField]
        private Vector3 _positionOffset = new(0, 0, 5f);

        private Vector3 _finalPositionOffset = Vector3.zero;

        [SerializeField] private float _positionSmoothTime = 0.3f;

        [Inject] private readonly ContextInteractionService _contextInteractionService;
        
        private Transform _playerTransform;
        private VRPlayerController _playerController;
        private Vector3 _moveVelocity;
        private float _currentYaw;

        private void OnEnable()
        {
            if (_playerTransform == null)
            {
                _playerTransform = _contextInteractionService.GetCurrentPlayerController().transform;
                _playerController = _playerTransform.GetComponent<VRPlayerController>();
            }

            RecalculatePositionOffset();
            UpdateOffset(true);
            transform.rotation = _playerTransform.rotation;
        }

        private void Update()
        {
            if (_playerTransform == null) return;

            UpdateOffset();
        }

        private void RecalculatePositionOffset()
        {
            // We want the position offset on the x axis to be recalculated only when turning on the menu
            Vector3 flatForward = Vector3.ProjectOnPlane(_playerTransform.forward, Vector3.up).normalized;
            _finalPositionOffset = flatForward * _positionOffset.z;
        }

        private void UpdateOffset(bool immediate = false)
        {
            // Calculate target position with proper height tracking
            Vector3 targetPosition = _playerTransform.position + _finalPositionOffset +
                                     Vector3.up * _playerController.CameraHeight;

            if (immediate)
            {
                transform.position = targetPosition;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    targetPosition,
                    ref _moveVelocity,
                    _positionSmoothTime
                );
            }
        }
    }
}
#endif
