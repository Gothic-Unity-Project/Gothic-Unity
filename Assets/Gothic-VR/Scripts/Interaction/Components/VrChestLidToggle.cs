using System.Collections;
using Gothic.Core.Logging;
using Gothic.Core.Models.Context;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Interim chest lid for the XRI backend until Phase 4 (VrHinge): using the lid (select-only grab, like a mob)
    /// swings it open and shows the chest's slots (VrChestSlots), using it again closes it. The lid is kinematic,
    /// there is no physical pushing/pulling yet. Respects Lock()/Unlock() of the lock picking and plays the
    /// open/close sounds of VRVobContainer.
    /// Inactive on HVR, where HvrPhysicsChest (HVR physics door) is the lid.
    /// </summary>
    public class VrChestLidToggle : MonoBehaviour, IVrHinge
    {
        [Tooltip("Rotation around the lid's local X axis when open (degrees).")]
        [SerializeField] private float _openAngle = 110f;

        [Tooltip("Seconds the lid needs to swing open or closed.")]
        [SerializeField] private float _swingTime = 0.4f;

        [Inject] private readonly IVrBackend _vrBackend;
        [Inject] private readonly IVrSfxPlayer _sfxPlayer;

        private VrInteractable _interactable;
        private VrChestSlots _chestSlots;
        private Rigidbody _rigidbody;
        private Quaternion _closedRotation;
        private AudioClip _openedClip;
        private AudioClip _closedClip;
        private Coroutine _swingRoutine;
        private bool _isSubscribed;
        private bool _isLocked;
        private bool _isOpen;

        public bool IsActive => _vrBackend != null && _vrBackend.Kind == VrInteractionBackend.Xri;
        public bool IsOpen => _isOpen;
        public bool IsLocked => _isLocked;
        public Transform Transform => transform;
        public Rigidbody Rigidbody => GetComponent<Rigidbody>();

        private void Start()
        {
            if (!IsActive)
            {
                enabled = false;
                return;
            }

            // No joint holds the lid (HvrPhysicsChest doesn't set up its door on XRI).
            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody != null)
                _rigidbody.isKinematic = true;

            _closedRotation = transform.localRotation;
            _chestSlots = GetComponentInParent<VrChestSlots>();
            _interactable = GetComponent<VrInteractable>();
            if (_interactable == null || _chestSlots == null)
            {
                Logger.LogError($"[Chest] {name} needs a VrInteractable and a VrChestSlots in its parents.",
                    LogCat.VR);
                return;
            }

            _interactable.Grabbed += OnGrabbed;
            _isSubscribed = true;
        }

        private void OnDestroy()
        {
            if (_isSubscribed && _interactable != null)
                _interactable.Grabbed -= OnGrabbed;
        }

        public void Lock()
        {
            _isLocked = true;
        }

        public void Unlock()
        {
            _isLocked = false;
        }

        public void SetSounds(AudioClip opened, AudioClip closed)
        {
            _openedClip = opened;
            _closedClip = closed;
        }

        public void Toggle()
        {
            if (_isOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            if (_isOpen || !isActiveAndEnabled)
                return;

            if (_isLocked)
            {
                Logger.Log($"[Chest] {name} is locked.", LogCat.VR);
                return;
            }

            _isOpen = true;
            Swing(_closedRotation * Quaternion.Euler(_openAngle, 0, 0), _openedClip);
            _chestSlots.Open();
        }

        public void Close()
        {
            if (!_isOpen || !isActiveAndEnabled)
                return;

            _isOpen = false;
            _chestSlots.Close();
            Swing(_closedRotation, _closedClip);
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            // Hands "use" the lid (XRSimpleInteractable). Sockets never hold it.
            if (!args.IsHandOrDistance)
                return;

            Toggle();
        }

        private void Swing(Quaternion target, AudioClip clipAtEnd)
        {
            if (_swingRoutine != null)
                StopCoroutine(_swingRoutine);
            _swingRoutine = StartCoroutine(SwingTo(target, clipAtEnd));
        }

        private IEnumerator SwingTo(Quaternion target, AudioClip clipAtEnd)
        {
            var start = transform.localRotation;
            for (var time = 0f; time < _swingTime; time += Time.deltaTime)
            {
                transform.localRotation = Quaternion.Slerp(start, target, time / _swingTime);
                yield return null;
            }

            transform.localRotation = target;
            _swingRoutine = null;

            if (clipAtEnd != null && _sfxPlayer != null)
                _sfxPlayer.Play(clipAtEnd, transform.position);
        }
    }
}
