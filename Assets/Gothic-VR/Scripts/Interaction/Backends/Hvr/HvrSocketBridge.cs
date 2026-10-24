#if GOTHIC_HVR_INSTALLED
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Utils;
using UnityEngine;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Sits next to an HVRSocket (or a subclass like HvrSocket). Grabbed/Released are the "placed"/"removed" events.
    /// </summary>
    public class HvrSocketBridge : HvrGrabberBridge, IVrSocket
    {
        private HVRSocket Socket => (HVRSocket)Grabber;

        public Transform Transform => transform;
        public bool IsOccupied => Socket.IsGrabbing;
        public bool IsShoulder => Socket is HVRShoulderSocket or HvrShoulderSocket;

        public AudioClip PlacedSoundFallback
        {
            get => Socket.AudioGrabbedFallback;
            set => Socket.AudioGrabbedFallback = value;
        }

        public AudioClip RemovedSoundFallback
        {
            get => Socket.AudioReleasedFallback;
            set => Socket.AudioReleasedFallback = value;
        }

        public bool TryPlace(VrInteractable interactable, bool silent)
        {
            if (interactable == null || !interactable.TryGetComponent(out HVRGrabbable grabbable))
                return false;

            return Socket.TryGrab(grabbable, true, silent);
        }
    }
}
#endif
