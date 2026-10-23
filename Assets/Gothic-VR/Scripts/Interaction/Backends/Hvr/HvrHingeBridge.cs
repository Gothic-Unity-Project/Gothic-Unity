#if GOTHIC_HVR_INSTALLED
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Components;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Sits next to an HVRPhysicsDoor (doors, and chest lids via HvrPhysicsChest). Switches the door off if another
    /// VR backend runs.
    /// </summary>
    public class HvrHingeBridge : MonoBehaviour, IVrHinge
    {
        [Inject] private readonly IVrBackend _vrBackend;

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

        public bool IsActive => _vrBackend == null || _vrBackend is HvrBackend;
        public Transform Transform => transform;
        public Rigidbody Rigidbody => GetComponent<Rigidbody>();

        private void Awake()
        {
            // The door would set up its own joint in Start and fight the hinge of another backend (VrHinge).
            if (!IsActive && Door != null)
                Door.enabled = false;
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
