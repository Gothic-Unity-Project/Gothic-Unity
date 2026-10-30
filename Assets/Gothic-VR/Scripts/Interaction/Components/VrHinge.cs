using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Backend-neutral hinge for doors and chest lids. The leaf is kinematic and rotated by our own angle
    /// solver (no joint: velocity tracking against a HingeJoint/ConfigurableJoint whipped and jammed the leaf):
    /// - While a hand holds the leaf, the hand's angular movement around the hinge axis (projected onto the hinge
    ///   plane) turns the leaf by the same angle, clamped to Min/MaxAngle.
    /// - After letting go, the leaf swings on with the hand's last angular speed and slows down (SwingDamping). It
    ///   stops at the limits without bouncing.
    /// - Angle is the opened angle in degrees (0 = closed).
    /// - Closed below CloseAngle, open above CloseAngle + OpenHysteresis. Opened/Closed are raised once per change.
    ///   The Gothic open/close sound is played once per change, but not more often than SoundTimeout.
    /// - Soft close: released below CloseAngle, the leaf closes completely within CloseOverTime.
    /// - Lock() holds the leaf at 0 degrees, Unlock() frees it with a small push open (lock picking).
    /// - Being kinematic, the player's CharacterController can't push the leaf through its limits.
    /// - Releases the leaf when the hand gets farther than BreakDistance from it (e.g. teleporting while holding).
    /// - A physical hand (VrPhysicsHand) touching the leaf without holding it pushes it: the part of the hand's
    ///   blocked movement along the leaf's swing direction at the contact turns the leaf, at most MaxHandSpeed.
    ///
    /// The grab itself is done by the backend (GothicGrabInteractable + XriInteractableBridge, with tracking off).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VrHinge : MonoBehaviour, IVrHinge
    {
        [Tooltip("Hinge axis in the leaf's local space.")] [SerializeField]
        private Vector3 _localAxis = Vector3.up;

        [Tooltip("The leaf opens against the right-hand rotation around the axis.")] [SerializeField]
        private bool _isOpeningInverted;

        [Tooltip("Smallest opened angle (degrees).")] [SerializeField]
        private float _minAngle;

        [Tooltip("Largest opened angle (degrees).")] [SerializeField]
        private float _maxAngle = 90f;

        [Tooltip("Below this opened angle the hinge counts as closed (degrees).")] [SerializeField]
        private float _closeAngle = 5f;

        [Tooltip("Degrees above CloseAngle until the hinge counts as open again. Prevents repeated events and " +
                 "sounds while the leaf rattles around the threshold.")]
        [SerializeField]
        private float _openHysteresis = 1f;

        [Tooltip("Seconds the leaf needs to close completely after letting go below CloseAngle. 0 = off.")]
        [SerializeField]
        private float _closeOverTime = 0.25f;

        [Tooltip("Minimum seconds between two open/close sounds.")] [SerializeField]
        private float _soundTimeout = 0.5f;

        [Tooltip("Fastest rotation while a hand drags the leaf (degrees/s). Hands can't whip it through the limits.")]
        [SerializeField]
        private float _maxHandSpeed = 360f;

        [Tooltip("How fast a released leaf slows down (1/s). Higher = stops sooner.")] [SerializeField]
        private float _swingDamping = 3f;

        [Tooltip("Fastest rotation of a released leaf (degrees/s).")] [SerializeField]
        private float _maxSwingSpeed = 180f;

        [Tooltip("Rotation speed (degrees/s) the leaf gets when it is unlocked (the push of lock picking).")]
        [SerializeField]
        private float _unlockPushSpeed = 60f;

        [Tooltip("A hand farther than this from the leaf (meters) lets go of it.")] [SerializeField]
        private float _breakDistance = 1f;

        [Inject] private readonly IVrBackend _vrBackend;
        [Inject] private readonly IVrSfxPlayer _sfxPlayer;

        private readonly List<GameObject> _grabbers = new();

        private VrInteractable _interactable;
        private Quaternion _closedRotation;
        private Vector3 _axis;
        private Vector3 _planeReference;
        private AudioClip _openedClip;
        private AudioClip _closedClip;
        private float _angle;
        private float _speed;
        private float _grabHandAngle;
        private float _grabLeafAngle;
        private float _pushAngle;
        private float _lastSoundTime = float.NegativeInfinity;
        private bool _isStarted;
        private bool _isSubscribed;
        private bool _isLocked;
        private bool _isOpen;
        private bool _isClosed = true;
        private bool _isOpenPending;

        public event Action Opened;
        public event Action Closed;

        public bool IsActive => _vrBackend != null;
        public bool IsOpen => _isOpen;
        public bool IsClosed => _isClosed;
        public bool IsLocked => _isLocked;
        public bool IsHeld => _grabbers.Count > 0;
        public Transform Transform => transform;
        public Rigidbody Rigidbody => GetComponent<Rigidbody>();

        /// <summary>
        /// Opened angle in degrees. 0 = closed, MaxAngle = fully open.
        /// </summary>
        public float Angle => _angle;

        private void Start()
        {
            if (!IsActive)
            {
                enabled = false;
                return;
            }

            // Moved by transform only. The Rigidbody is kept for the colliders and the lock picking.
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            _axis = _localAxis.normalized;
            _closedRotation = transform.localRotation;
            var worldAxis = GetWorldAxis();
            var reference = Mathf.Abs(Vector3.Dot(worldAxis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            _planeReference = Vector3.ProjectOnPlane(reference, worldAxis).normalized;
            _isStarted = true;

            if (_isOpenPending)
                OpenInstantly();

            _interactable = GetComponent<VrInteractable>();
            if (_interactable == null)
            {
                Logger.LogWarning($"[Hinge] {name} has no VrInteractable, it can't be opened by hand.", LogCat.VR);
                return;
            }

            _interactable.Grabbed += OnGrabbed;
            _interactable.Released += OnReleased;
            _isSubscribed = true;
        }

        private void OnDestroy()
        {
            if (!_isSubscribed || _interactable == null)
                return;

            _interactable.Grabbed -= OnGrabbed;
            _interactable.Released -= OnReleased;
        }

        private void LateUpdate()
        {
            // After the XR rig has moved the hands this frame.
            BreakIfHandIsFar();

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                return;

            if (_isLocked)
                MoveTo(0f, deltaTime);
            else if (IsHeld)
                FollowHand(deltaTime);
            else if (_pushAngle != 0f)
                Push(deltaTime);
            else
                Swing(deltaTime);

            _pushAngle = 0f;
            UpdateState(_angle);
        }

        /// <summary>
        /// Collects the push of physical hands touching the leaf. Kinematic leaf vs. dynamic hand still reports
        /// contacts. Unity sends this to disabled components as well, hence the _isStarted check.
        /// </summary>
        private void OnCollisionStay(Collision collision)
        {
            if (!_isStarted || _isLocked || IsHeld || !VrPhysicsHand.TryGetByBody(collision.rigidbody, out var hand))
                return;

            var push = hand.PushOffset;
            if (push.sqrMagnitude < 0.000001f)
                return;

            var worldAxis = GetWorldAxis();
            var openingAxis = _isOpeningInverted ? -worldAxis : worldAxis;
            for (var i = 0; i < collision.contactCount; i++)
            {
                var lever = Vector3.ProjectOnPlane(collision.GetContact(i).point - transform.position, worldAxis);
                var radius = lever.magnitude;
                // A contact right at the hinge has no leverage.
                if (radius < 0.05f)
                    continue;

                // Direction a point on the leaf moves when the opened angle grows.
                var swingDirection = Vector3.Cross(openingAxis, lever) / radius;
                var angle = Vector3.Dot(push, swingDirection) / radius * Mathf.Rad2Deg;
                if (Mathf.Abs(angle) > Mathf.Abs(_pushAngle))
                    _pushAngle = angle;
            }
        }

        public void Lock()
        {
            _isLocked = true;
            _speed = 0f;
            if (!_isStarted)
                return;

            _angle = 0f;
            ApplyAngle();
            UpdateState(_angle);
        }

        public void Unlock()
        {
            _isLocked = false;
            if (!_isStarted)
                return;

            // The push open of the lock picking.
            _speed = _unlockPushSpeed;
            RebaselineGrab();
        }

        public void OpenInstantly()
        {
            // Before Start(), the closed rotation isn't known yet.
            _isOpenPending = !_isStarted;
            if (!_isStarted || _isLocked)
                return;

            _speed = 0f;
            _angle = _maxAngle;
            ApplyAngle();
            _isClosed = false;
            _isOpen = true;
        }

        public void SetSounds(AudioClip opened, AudioClip closed)
        {
            _openedClip = opened;
            _closedClip = closed;
        }

        private Vector3 GetWorldAxis()
        {
            return transform.TransformDirection(_localAxis.normalized);
        }

        /// <summary>
        /// Angle of the hand around the hinge axis in the hinge plane, in the opening direction (degrees).
        /// </summary>
        private float GetHandAngle(Vector3 handPosition)
        {
            var worldAxis = GetWorldAxis();
            var fromHinge = Vector3.ProjectOnPlane(handPosition - transform.position, worldAxis);
            var angle = Vector3.SignedAngle(_planeReference, fromHinge, worldAxis);
            return _isOpeningInverted ? -angle : angle;
        }

        private void FollowHand(float deltaTime)
        {
            var hand = _grabbers[0];
            if (hand == null)
                return;

            var handPosition = hand.transform.position;
            var radius = Vector3.ProjectOnPlane(handPosition - transform.position, GetWorldAxis()).magnitude;
            // A hand right at the hinge has no meaningful angle.
            if (radius < 0.05f)
                return;

            var handDelta = Mathf.DeltaAngle(_grabHandAngle, GetHandAngle(handPosition));
            var target = Mathf.Clamp(_grabLeafAngle + handDelta, _minAngle, _maxAngle);
            var previous = _angle;
            MoveTo(target, deltaTime, _maxHandSpeed);

            // Keeps its speed for the swing after letting go.
            _speed = Mathf.Lerp(_speed, (_angle - previous) / deltaTime, 0.5f);
        }

        private void Push(float deltaTime)
        {
            var target = Mathf.Clamp(_angle + _pushAngle, _minAngle, _maxAngle);
            var previous = _angle;
            MoveTo(target, deltaTime, _maxHandSpeed);

            // Swings on when the hand stops touching.
            _speed = Mathf.Lerp(_speed, (_angle - previous) / deltaTime, 0.5f);
        }

        private void Swing(float deltaTime)
        {
            if (_closeOverTime > 0f && _angle < _closeAngle && _angle > _minAngle && Mathf.Abs(_speed) < 5f)
            {
                _speed = 0f;
                MoveTo(_minAngle, deltaTime, _closeAngle / _closeOverTime);
                return;
            }

            if (Mathf.Approximately(_speed, 0f))
                return;

            _speed = Mathf.Clamp(_speed * Mathf.Exp(-_swingDamping * deltaTime), -_maxSwingSpeed, _maxSwingSpeed);
            if (Mathf.Abs(_speed) < 0.5f)
                _speed = 0f;

            var next = Mathf.Clamp(_angle + _speed * deltaTime, _minAngle, _maxAngle);
            if (Mathf.Approximately(next, _minAngle) || Mathf.Approximately(next, _maxAngle))
                _speed = 0f;

            _angle = next;
            ApplyAngle();
        }

        private void MoveTo(float target, float deltaTime, float maxSpeed = float.PositiveInfinity)
        {
            var next = Mathf.MoveTowards(_angle, target, maxSpeed * deltaTime);
            if (Mathf.Approximately(next, _angle))
                return;

            _angle = next;
            ApplyAngle();
        }

        private void ApplyAngle()
        {
            var sign = _isOpeningInverted ? -1f : 1f;
            transform.localRotation = _closedRotation * Quaternion.AngleAxis(sign * _angle, _axis);
        }

        private void UpdateState(float angle)
        {
            if (_isClosed && angle > _closeAngle + _openHysteresis)
            {
                _isClosed = false;
                _isOpen = true;
                PlaySound(_openedClip);
                Opened?.Invoke();
            }
            else if (_isOpen && angle < _closeAngle)
            {
                _isClosed = true;
                _isOpen = false;
                PlaySound(_closedClip);
                Closed?.Invoke();
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null || _sfxPlayer == null || Time.time - _lastSoundTime < _soundTimeout)
                return;

            _lastSoundTime = Time.time;
            _sfxPlayer.Play(clip, transform.position);
        }

        private void BreakIfHandIsFar()
        {
            if (!IsHeld || _interactable == null)
                return;

            foreach (var grabber in _grabbers)
            {
                if (grabber == null || GetDistanceToLeaf(grabber.transform.position) <= _breakDistance)
                    continue;

                Logger.Log($"[Hinge] {name} released: hand is farther than {_breakDistance}m away.", LogCat.VR);
                _interactable.ForceRelease();
                return;
            }
        }

        private float GetDistanceToLeaf(Vector3 position)
        {
            var distance = float.MaxValue;
            foreach (var leafCollider in _interactable.Colliders)
            {
                if (leafCollider == null || !leafCollider.enabled)
                    continue;

                distance = Mathf.Min(distance, Vector3.Distance(leafCollider.ClosestPointOnBounds(position), position));
            }

            return distance;
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            if (!args.IsHandOrDistance || args.GrabberGo == null || _grabbers.Contains(args.GrabberGo))
                return;

            _grabbers.Add(args.GrabberGo);
            _speed = 0f;
            RebaselineGrab();
        }

        private void OnReleased(VrGrabArgs args)
        {
            _grabbers.Remove(args.GrabberGo);
            RebaselineGrab();
        }

        /// <summary>
        /// The leaf follows the first hand from where the leaf and the hand are now.
        /// </summary>
        private void RebaselineGrab()
        {
            if (!IsHeld || _grabbers[0] == null)
                return;

            _grabLeafAngle = _angle;
            _grabHandAngle = GetHandAngle(_grabbers[0].transform.position);
        }
    }
}
