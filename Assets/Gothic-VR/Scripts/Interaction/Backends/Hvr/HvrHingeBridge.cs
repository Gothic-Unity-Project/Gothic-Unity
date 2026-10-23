#if GOTHIC_HVR_INSTALLED
using System;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Components;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Sits next to an HVRPhysicsDoor (doors, and chest lids via HvrPhysicsChest). Switches the door off if another
    /// VR backend runs. Raises IVrHinge.Opened/Closed from HVRPhysicsDoor.Opened/Closed (polled in Update).
    /// </summary>
    public class HvrHingeBridge : MonoBehaviour, IVrHinge
    {
        [Inject] private readonly IVrBackend _vrBackend;

        // Flags to ensure we raise Opened/Closed once, every time the door's open/close status changes.
        private bool _isOpenedRaised;
        private bool _isClosedRaised;
        private HVRPhysicsDoor _door;

        private HVRPhysicsDoor Door
        {
            get
            {
                if (_door == null)
                    _door = GetComponent<HVRPhysicsDoor>();
                return _door;
            }
        }

        public event Action Opened;
        public event Action Closed;

        public bool IsActive => _vrBackend == null || _vrBackend is HvrBackend;
        public Transform Transform => transform;
        public Rigidbody Rigidbody => GetComponent<Rigidbody>();

        private void Awake()
        {
            // The door would set up its own joint in Start and fight the hinge of another backend (VrHinge).
            if (!IsActive && Door != null)
                Door.enabled = false;
        }

        private void Update()
        {
            if (!IsActive || Door == null)
                return;

            if (Door.Opened && !_isOpenedRaised)
            {
                _isOpenedRaised = true;
                _isClosedRaised = false;
                Opened?.Invoke();
            }

            if (Door.Closed && !_isClosedRaised)
            {
                _isClosedRaised = true;
                _isOpenedRaised = false;
                Closed?.Invoke();
            }
        }

        public void Lock()
        {
            Door.Lock();
        }

        public void Unlock()
        {
            Door.Unlock();
        }

        public void SetSounds(AudioClip opened, AudioClip closed)
        {
            Door.SFXOpened = opened;
            Door.SFXClosed = closed;
        }
    }
}
#endif
