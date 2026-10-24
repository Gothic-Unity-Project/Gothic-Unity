#if GOTHIC_HVR_INSTALLED
using System.Linq;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Core.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    public class HvrUiPointers : IVrUiPointers
    {
        public void RegisterCanvas(Canvas canvas)
        {
            if (HVRInputModule.Instance != null)
                HVRInputModule.Instance.AddCanvas(canvas);
        }

        public void UnregisterCanvas(Canvas canvas)
        {
            if (HVRInputModule.Instance != null)
                HVRInputModule.Instance.RemoveCanvas(canvas);
        }

        public void RegisterAllCanvases()
        {
            // WARNING: FindObjectsByType looks through all opened scene trees, this can become quite slow.
            var allCanvases =
                Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            HVRInputModule.Instance.UICanvases = allCanvases.ToList();
        }

        public bool TryGetHandSide(PointerEventData eventData, out VrHandSide side)
        {
            var pointer = HVRInputModule.Instance.Pointers.FirstOrDefault(i => i.PointerEventData == eventData);
            side = pointer == null ? default : pointer.HandSide.ToVr();
            return pointer != null;
        }
    }
}
#endif
