using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// UI laser pointers of the XRI rig: the far ray of each hand's NearFarInteractor (also used for distance grabs)
    /// and the XRUIInputModule on the player's EventSystem. A canvas is hit only once it has a
    /// TrackedDeviceGraphicRaycaster, which RegisterCanvas() adds.
    /// </summary>
    public class XriUiPointers : IVrUiPointers
    {
        public void RegisterCanvas(Canvas canvas)
        {
            if (canvas == null)
                return;

            if (canvas.TryGetComponent(out TrackedDeviceGraphicRaycaster raycaster))
                raycaster.enabled = true;
            else
                canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        public void UnregisterCanvas(Canvas canvas)
        {
            if (canvas != null && canvas.TryGetComponent(out TrackedDeviceGraphicRaycaster raycaster))
                raycaster.enabled = false;
        }

        public void RegisterAllCanvases()
        {
            // WARNING: FindObjectsByType looks through all opened scene trees, this can become quite slow.
            var allCanvases =
                Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var canvas in allCanvases)
                RegisterCanvas(canvas);

            Logger.Log($"[XRI] {allCanvases.Length} canvases registered for the UI laser pointers.", LogCat.VR);
        }

        public bool TryGetHandSide(PointerEventData eventData, out VrHandSide side)
        {
            side = default;
            if (eventData is not TrackedDeviceEventData trackedEventData ||
                trackedEventData.interactor is not XRBaseInteractor interactor)
                return false;

            switch (interactor.handedness)
            {
                case InteractorHandedness.Left:
                    side = VrHandSide.Left;
                    return true;
                case InteractorHandedness.Right:
                    side = VrHandSide.Right;
                    return true;
                default:
                    return false;
            }
        }
    }
}
