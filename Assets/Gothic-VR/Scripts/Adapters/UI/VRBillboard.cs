#if GOTHIC_HVR_INSTALLED
using UnityEngine;

namespace Gothic.VR.Adapters.UI
{
    public class VRBillboard : MonoBehaviour
    {
        private Transform _cameraTransform;

        // Inversion is needed for UI elements to show text correctly.
        private Quaternion yAxisInversion = Quaternion.Euler(0f, 180f, 0f);

        private void Start()
        {
            if (_cameraTransform == null)
            {
                _cameraTransform = Camera.main!.transform;
            }
        }

        private void OnEnable()
        {
            Application.onBeforeRender += FaceCamera;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= FaceCamera;
        }

        /// <summary>
        /// After all LateUpdates: dialogs hang below an NPC's BIP01 bone, which AnimationSystem.LateUpdate still turns
        /// (sitting NPCs, _isSittingInverted). Rotated in Update, the dialog was turned away with the bone afterwards.
        /// </summary>
        private void FaceCamera()
        {
            if (_cameraTransform == null)
                return;

            // Calculate the direction to look at
            var directionToLookAt = _cameraTransform.position - transform.position;

            // Create a rotation quaternion that ignores y-axis.
            var rotation = Quaternion.LookRotation(new Vector3(directionToLookAt.x, 0f, directionToLookAt.z));
            transform.rotation = rotation * yAxisInversion;
        }
    }
}
#endif
