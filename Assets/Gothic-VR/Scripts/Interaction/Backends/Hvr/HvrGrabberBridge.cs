#if GOTHIC_HVR_INSTALLED
using System;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using UnityEngine;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Sits next to an HVR grabber (hand or force grabber) and forwards its events synchronously as IVrGrabber events.
    /// BeforeGrabbed is also forwarded to the grabbed VrInteractable (HVRGrabbable has no such event).
    /// </summary>
    public class HvrGrabberBridge : MonoBehaviour, IVrGrabber
    {
        public event Action<VrGrabArgs> BeforeGrabbed;
        public event Action<VrGrabArgs> Grabbed;
        public event Action<VrGrabArgs> Released;

        private HVRGrabberBase _grabber;
        private bool _isSubscribed;

        protected HVRGrabberBase Grabber
        {
            get
            {
                if (_grabber == null)
                    _grabber = GetComponent<HVRGrabberBase>();
                return _grabber;
            }
        }

        public VrGrabberKind Kind => Grabber.ToVrKind();
        public VrHandSide HandSide => Grabber.ToVrHandSide();

        public VrInteractable Held => Grabber.IsGrabbing
            ? HvrGrabArgsFactory.GetInteractable(Grabber.GrabbedTarget)
            : null;

        protected virtual void Awake()
        {
            if (Grabber == null)
                return;

            Grabber.BeforeGrabbed.AddListener(OnBeforeGrabbed);
            Grabber.Grabbed.AddListener(OnGrabbed);
            Grabber.Released.AddListener(OnReleased);
            _isSubscribed = true;
        }

        protected virtual void OnDestroy()
        {
            if (!_isSubscribed || _grabber == null)
                return;

            _grabber.BeforeGrabbed.RemoveListener(OnBeforeGrabbed);
            _grabber.Grabbed.RemoveListener(OnGrabbed);
            _grabber.Released.RemoveListener(OnReleased);
        }

        public void ForceRelease()
        {
            Grabber.ForceRelease();
        }

        private void OnBeforeGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            var args = HvrGrabArgsFactory.Create(grabber, grabbable);
            BeforeGrabbed?.Invoke(args);
            if (args.Interactable != null)
                args.Interactable.RaiseBeforeGrabbed(args);
        }

        private void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            Grabbed?.Invoke(HvrGrabArgsFactory.Create(grabber, grabbable));
        }

        private void OnReleased(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            Released?.Invoke(HvrGrabArgsFactory.Create(grabber, grabbable));
        }
    }
}
#endif
