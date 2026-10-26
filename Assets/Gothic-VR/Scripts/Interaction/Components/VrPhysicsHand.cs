using System;
using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Physical hand presence: a rigidbody with one box collider per block of the block hand is driven towards the
    /// tracked controller every FixedUpdate. It stops at walls, tables, chests and NPCs and pushes doors and loose
    /// items with limited strength. A ghost hand shows the real controller pose while the hand is blocked.
    ///
    /// Sits on the visual root of a hand (next to VrBlockHand, child of the tracked controller). The rigidbody lives
    /// on its own GameObject at the root of the player's scene, so that the moving rig (CharacterController) doesn't
    /// drag it kinematically through walls. The visual stays under the controller and is offset by what the physics
    /// step blocked: no interpolation lag while walking, and the late-latched controller pose still applies.
    ///
    /// Off (comfort setting, while climbing): the visual follows the controller directly, the body is inactive.
    ///
    /// Rig jumps (teleport, Marvin goto, world change, ladder exit, snap turn): the hand snaps with the rig and the
    /// held item is moved by the same offset. What they overlap at the new place doesn't push them out, they pass
    /// through until clear (VrCollisionPassThrough).
    /// </summary>
    public class VrPhysicsHand : MonoBehaviour
    {
        [Tooltip("Hand side, for logs. Taken from VrBlockHand on the same GameObject if it has one.")] [SerializeField]
        private VrHandSide _side;

        [Tooltip("Material of the ghost hand (transparent).")] [SerializeField]
        private Material _ghostMaterial;

        [Header("Follow")]
        [Tooltip("Mass in kg. Together with Max Force it sets how strongly the hand pushes other rigidbodies.")]
        [SerializeField]
        private float _mass = 1f;

        [Tooltip("Max. speed in m/s the hand moves towards the controller.")] [SerializeField]
        private float _maxSpeed = 15f;

        [Tooltip(
            "Strength limit: max. force in N the hand applies. Pushes items, doors and chest lids, but not NPCs. " +
            "Following the controller itself isn't limited.")]
        [SerializeField]
        private float _maxForce = 200f;

        [Tooltip("Max. angular speed in rad/s the hand turns towards the controller.")] [SerializeField]
        private float _maxAngularSpeed = 50f;

        [Tooltip("Max. angular acceleration in rad/s^2.")] [SerializeField]
        private float _maxAngularAcceleration = 2500f;

        [Tooltip("Max. speed in m/s with which the hand is pushed out of colliders it overlaps (after a snap).")]
        [SerializeField]
        private float _maxDepenetrationSpeed = 2f;

        [Tooltip("Distance in m between physical hand and controller from which the visual shows the physics pose " +
                 "only. Below, it blends towards the controller pose (no lag while the hand is free).")]
        [SerializeField]
        private float _blockedDistance = 0.005f;

        [Header("Ghost hand")]
        [Tooltip("Distance in m between physical hand and controller above which the ghost hand shows.")]
        [SerializeField]
        private float _ghostShowDistance = 0.05f;

        [Tooltip("Distance in m below which the ghost hand hides again.")] [SerializeField]
        private float _ghostHideDistance = 0.03f;

        [Header("Max distance")]
        [Tooltip("Distance in m between physical hand and controller above which the hand counts as stuck.")]
        [SerializeField]
        private float _maxDistance = 0.5f;

        [Tooltip("Time in s a stuck hand waits before it snaps back to the controller.")] [SerializeField]
        private float _maxDistanceTime = 0.2f;

        [Tooltip("A rig turn larger than this (degrees per frame) is a snap turn: the hand snaps with the rig.")]
        [SerializeField]
        private float _rigSnapAngle = 10f;

        [Tooltip("A rig move larger than this (m per frame) is a teleport: the hand snaps with the rig.")]
        [SerializeField]
        private float _rigSnapDistance = 1f;

        [Tooltip("Margin in m around the hand: collisions with a released item come back once it is further away.")]
        [SerializeField]
        private float _restoreCollisionMargin = 0.02f;

        private static readonly Dictionary<Rigidbody, VrPhysicsHand> _handsByBody = new();

        [Tooltip("Time in s the visual moves back to the controller when the physics hand turns off (ladder grabbed, " +
                 "setting off), instead of popping there. Offsets above Max Distance pop.")]
        [SerializeField] private float _inactiveBlendTime = 0.15f;

        [Header("Held item")]
        [Tooltip("While the held item touches something solid, the hand moves at most this far (m) ahead of where " +
                 "the item holds it. The item follows the hand, so sword and hand stop at a wall together.")]
        [SerializeField]
        private float _heldLeadDistance = 0.02f;

        [Tooltip("Same as Held Lead Distance for the rotation (degrees).")] [SerializeField]
        private float _heldLeadAngle = 5f;

        [Tooltip("The item doesn't block the hand until its grip got this close (m) to the hand: it's still flying " +
                 "to the hand (distance grab, after a snap).")]
        [SerializeField]
        private float _heldMaxGripDistance = 0.15f;

        [Tooltip("Time in s the hand stays held back after the item's last blocking contact. Bridges contacts that " +
                 "break for a physics step while the item is pressed against a wall.")]
        [SerializeField]
        private float _heldBlockedMemory = 0.1f;

        [Tooltip("Dynamic rigidbodies lighter than this (kg) are pushed by the held item, they don't block the hand.")]
        [SerializeField]
        private float _heldBlockingMinMass = 5f;

        [Tooltip("Time in s the held item's visual blends between the hand (free) and its physics pose (blocked, " +
                 "still flying to the hand).")]
        [SerializeField]
        private float _heldVisualBlendTime = 0.15f;

        [Header("Contact haptics")]
        [Tooltip("Min. impact speed (m/s, along the contact normal) that gives a contact haptic.")]
        [SerializeField]
        private float _contactMinSpeed = 0.3f;

        [Tooltip("Min. time in s between two contact haptics of this hand.")] [SerializeField]
        private float _contactCooldown = 0.15f;

        [Tooltip("A new contact gives a haptic only if the hand (or its held item) touched nothing for this long " +
                 "(s). Sliding along a wall or over the edge between two colliders doesn't buzz.")]
        [SerializeField]
        private float _contactRearmTime = 0.1f;

        private readonly List<(Transform Block, Transform Collider)> _blocks = new();
        private readonly List<Collider> _handColliders = new();
        private readonly HashSet<Collider> _ignoredColliders = new();
        private readonly List<Collider> _pendingRestoreColliders = new();
        private readonly List<Collider> _heldColliders = new();
        private readonly VrCollisionPassThrough _passThrough = new();
        private readonly VrCollisionPassThrough _heldPassThrough = new();

        // Distance in m a stuck hand's blocks are cast from behind the hand to find what holds it back.
        private const float _snapCastBackOff = 0.05f;

        // A held item is moved with the rig once per jump, also when both hands hold it.
        private static Rigidbody _lastMovedHeldBody;
        private static int _lastMovedHeldFrame = -1;

        private IVrRig _rig;
        private Transform _target;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private GameObject _body;
        private Rigidbody _rigidbody;
        private VrGhostHand _ghost;
        private PhysicsMaterial _physicsMaterial;

        private bool _isPhysical = true;
        private bool _isActive;
        private bool _isSnapRequested;
        private bool _isGhostVisible;
        private float _stuckTime;
        private float _separation;
        private Vector3 _lastTargetPosition;
        private Quaternion _lastTargetRotation = Quaternion.identity;
        private bool _isTeleportPending;
        private bool _isRigPoseKnown;
        private Vector3 _lastTargetVelocity;
        private Vector3 _lastRigPosition;
        private Quaternion _lastRigRotation = Quaternion.identity;
        private Vector3 _rigVelocity;

        // Inactive hand: offset of the visual from its rest pose (controller space), blended to 0.
        private Vector3 _inactiveOffsetPosition;
        private Quaternion _inactiveOffsetRotation = Quaternion.identity;
        private float _inactiveBlendSpeed;
        private float _inactiveBlendAngularSpeed;

        private Transform _attachPoint;
        private Rigidbody _heldBody;
        private VrPhysicsHandContact _heldContact;
        private VrHeldItemVisual _heldVisual;
        private float _heldVisualBlocked = 1f;
        private Transform _heldGrip;
        private Func<Quaternion> _heldTargetRotation;
        private Vector3 _heldGripLocalPosition;
        private Quaternion _heldGripLocalRotation = Quaternion.identity;
        private bool _isVisualOnGrip;
        private Vector3 _visualLocalPositionBeforeGrip;
        private Quaternion _visualLocalRotationBeforeGrip = Quaternion.identity;
        private float _gripGhostDistance;
        private float _heldBlockedTime = float.NegativeInfinity;
        private bool _isHeldItemSettled;

        private float _lastContactHapticTime = float.NegativeInfinity;
        private float _lastBodyContactTime = float.NegativeInfinity;
        private float _lastHeldContactTime = float.NegativeInfinity;

        /// <summary>
        /// The hand got stuck behind something while holding an item and snapped back to the controller. The holder
        /// releases the item, it would stay stuck otherwise.
        /// </summary>
        public event Action HeldItemStuck;

        /// <summary>
        /// Physical hands (comfort setting). Off: the hand follows the controller directly.
        /// </summary>
        public bool IsPhysical
        {
            get => _isPhysical;
            set => _isPhysical = value;
        }

        /// <summary>
        /// The rigidbody of the physical hand. null before Awake or while the hand was never physical.
        /// Its pose is the pose of the visual root (this transform) as the physics step left it.
        /// </summary>
        public Rigidbody Body => _rigidbody;

        public VrHandSide Side => _side;

        /// <summary>
        /// Distance in m between the physical hand and the controller after the last physics step.
        /// 0 while the hand isn't physical.
        /// </summary>
        public float Separation => _isActive ? _separation : 0f;

        /// <summary>
        /// How far the controller wants the hand to move from where the last physics step left it (world space).
        /// Kinematic objects the hand touches (VrHinge) use it as the push of the hand. Zero while not physical.
        /// </summary>
        public Vector3 PushOffset => _isActive && _rigidbody != null
            ? _lastTargetPosition - _rigidbody.position
            : Vector3.zero;

        /// <summary>
        /// The physical hand whose rigidbody this is, e.g. the other body of a collision.
        /// </summary>
        public static bool TryGetByBody(Rigidbody body, out VrPhysicsHand hand)
        {
            hand = null;
            return body != null && _handsByBody.TryGetValue(body, out hand);
        }

        private void Awake()
        {
            var blockHand = GetComponent<VrBlockHand>();
            if (blockHand != null)
                _side = blockHand.Side;

            _target = transform.parent;
            _restPosition = transform.localPosition;
            _restRotation = transform.localRotation;

            _rig = GetComponentInParent<IVrRig>();
            if (_rig == null)
                Logger.LogWarning($"[VrPhysicsHand] {name} ({_side}) has no IVrRig parent. Teleports, snap turns " +
                                  "and climbing aren't handled.", LogCat.VR);

            CreateBody();

            if (_ghostMaterial != null)
                _ghost = VrGhostHand.Create(transform, _restPosition, _restRotation, _ghostMaterial);
            else
                Logger.LogWarning($"[VrPhysicsHand] {name} ({_side}) has no ghost material. No ghost hand.", LogCat.VR);
        }

        private void OnEnable()
        {
            if (_rig != null)
                _rig.Teleported += OnTeleported;
        }

        private void OnDisable()
        {
            if (_rig != null)
                _rig.Teleported -= OnTeleported;

            SetActive(false);
        }

        private void OnDestroy()
        {
            ClearHeldItem();
            if (!ReferenceEquals(_rigidbody, null))
                _handsByBody.Remove(_rigidbody);
            if (_body != null)
                Destroy(_body);
            if (_physicsMaterial != null)
                Destroy(_physicsMaterial);
        }

        /// <summary>
        /// Items held or socketed by the player don't collide with the hands (as with the CharacterController).
        /// On restore (item dropped), collisions come back once the item no longer overlaps the hand. Otherwise,
        /// the depenetration would shoot the item away.
        /// </summary>
        public void IgnoreCollisions(IReadOnlyList<Collider> others, bool ignore)
        {
            foreach (var other in others)
            {
                if (other == null)
                    continue;

                if (ignore)
                {
                    _pendingRestoreColliders.Remove(other);
                    // An inactive body gets all ignored pairs when it's activated.
                    if (_ignoredColliders.Add(other) && _isActive)
                        SetCollisionIgnored(other, true);
                }
                else if (!_isActive)
                {
                    // Pairs of an inactive body are reset already.
                    _ignoredColliders.Remove(other);
                }
                else if (_ignoredColliders.Contains(other) && !_pendingRestoreColliders.Contains(other))
                {
                    _pendingRestoreColliders.Add(other);
                }
            }
        }

        /// <summary>
        /// A transform at the world pose of source that moves with the hand visual: with the controller while the
        /// hand is free, stopped where the physics hand is blocked. Grabbers attach held items to it, so the items
        /// follow the physical hand (setting off, climbing: the controller).
        /// </summary>
        public Transform CreateAttachPoint(Transform source)
        {
            if (_attachPoint == null)
            {
                _attachPoint = new GameObject("Physics Hand Attach").transform;
                _attachPoint.SetParent(transform, false);
            }

            if (_target == null)
            {
                _attachPoint.SetPositionAndRotation(source.position, source.rotation);
                return _attachPoint;
            }

            // Relative to the rest pose: the visual may be offset by a blocked hand right now.
            var sourcePosition = _target.InverseTransformPoint(source.position);
            var sourceRotation = Quaternion.Inverse(_target.rotation) * source.rotation;
            var inverseRestRotation = Quaternion.Inverse(_restRotation);
            var localScale = transform.localScale;
            var localPosition = inverseRestRotation * (sourcePosition - _restPosition);
            _attachPoint.SetLocalPositionAndRotation(
                new Vector3(localPosition.x / localScale.x, localPosition.y / localScale.y,
                    localPosition.z / localScale.z),
                inverseRestRotation * sourceRotation);
            return _attachPoint;
        }

        /// <summary>
        /// The hand holds a dynamic item: its collisions give contact haptics, and while it touches something solid,
        /// the hand can't move on without it (the item follows the hand via CreateAttachPoint()).
        /// </summary>
        /// <param name="body">Rigidbody of the item.</param>
        /// <param name="grip">Where the item is held: a transform on the item that matches the attach point once
        /// the item is in the hand. Read every frame, the grabber may move it (other hand let go).</param>
        /// <param name="targetRotation">Rotation the grabber moves the item to. Used while two hands hold it (the
        /// item turns with both hands). null: the item keeps its rotation in this hand.</param>
        public void SetHeldItem(Rigidbody body, Transform grip, Func<Quaternion> targetRotation = null)
        {
            ClearHeldItem();
            if (body == null || grip == null)
                return;

            _heldBody = body;
            _heldGrip = grip;
            _heldTargetRotation = targetRotation;
            UpdateHeldGrip();
            _heldBlockedTime = float.NegativeInfinity;
            _lastHeldContactTime = float.NegativeInfinity;
            _isHeldItemSettled = false;

            foreach (var itemCollider in body.GetComponentsInChildren<Collider>())
            {
                if (!itemCollider.isTrigger && itemCollider.attachedRigidbody == body)
                    _heldColliders.Add(itemCollider);
            }

            _heldContact = body.gameObject.AddComponent<VrPhysicsHandContact>();
            _heldContact.Init(this, true);

            _heldVisualBlocked = 1f;
            if (_attachPoint != null)
                _heldVisual = VrHeldItemVisual.Hold(body, this, grip);
        }

        public void ClearHeldItem()
        {
            if (_heldContact != null)
                Destroy(_heldContact);

            // A dropped item that still passes through a floor would fall through it.
            _heldPassThrough.RestoreAll();
            _heldColliders.Clear();

            if (_heldVisual != null)
            {
                if (_heldVisual.HolderCount == 1)
                    MoveReleasedItemToHand();
                _heldVisual.Release(this);
            }

            RestoreFromGrip();

            _heldContact = null;
            _heldVisual = null;
            _heldGrip = null;
            _heldTargetRotation = null;
            _heldBody = null;
            _heldBlockedTime = float.NegativeInfinity;
        }

        /// <summary>
        /// Collision of the body or the held item (VrPhysicsHandContact).
        /// </summary>
        internal void OnContact(Collision collision, bool isHeldItem, bool isEnter)
        {
            var time = Time.fixedTime;
            var lastContactTime = isHeldItem ? _lastHeldContactTime : _lastBodyContactTime;
            if (isHeldItem)
                _lastHeldContactTime = time;
            else
                _lastBodyContactTime = time;

            if (isHeldItem && IsBlocking(collision.rigidbody))
                _heldBlockedTime = time;

            if (!isEnter || !_isActive || collision.contactCount == 0)
                return;

            // Sliding along a wall: contacts with the next collider start while the old ones still touch.
            if (time - lastContactTime < _contactRearmTime || time - _lastContactHapticTime < _contactCooldown)
                return;

            var speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(0).normal));
            if (speed < _contactMinSpeed)
                return;

            _lastContactHapticTime = time;
            GlobalEventDispatcher.PhysicalHandContact.Invoke((int)_side, speed);
        }

        private bool IsBlocking(Rigidbody other)
        {
            return other == null || other.isKinematic || other.mass >= _heldBlockingMinMass;
        }

        private void Update()
        {
            SetActive(ShouldBeActive());
        }

        private void FixedUpdate()
        {
            // Also with the setting off: the item may pass through a wall after a teleport either way.
            _heldPassThrough.Update(GetViewpoint());

            if (!_isActive)
                return;

            if (_body == null)
            {
                Logger.LogWarning($"[VrPhysicsHand] The physical {_side} hand was destroyed (scene unload?). " +
                                  "Creating a new one.", LogCat.VR);
                CreateBody();
                ReapplyIgnoredCollisions();
                _isSnapRequested = true;
            }

            var deltaTime = Time.fixedDeltaTime;
            GetTargetPose(out var targetPosition, out var targetRotation);

            // Result of the last step: how far the physics kept the hand from where it should be.
            _separation = Vector3.Distance(_rigidbody.position, _lastTargetPosition);

            if (_isSnapRequested)
            {
                Snap(targetPosition, targetRotation, targetPosition, targetRotation);
                PassThroughOverlaps();
                return;
            }

            if (_separation > _maxDistance)
            {
                _stuckTime += deltaTime;
                if (_stuckTime > _maxDistanceTime)
                {
                    Logger.LogEditor($"[VrPhysicsHand] {_side} hand stuck {_separation:0.00} m away from the " +
                                     "controller. Snapped back.", LogCat.VR);
                    SnapThroughBlockers(targetPosition, targetRotation);
                    ReleaseStuckItem();
                    return;
                }
            }
            else
            {
                _stuckTime = 0f;
            }

            SyncColliders();
            RestorePendingCollisions();
            _passThrough.Update(GetViewpoint());

            // A held item that touches a wall holds the hand back. The visual offset and the separation are still
            // measured against the controller (target), so the ghost hand shows and a stuck hand snaps.
            var drivePosition = targetPosition;
            var driveRotation = targetRotation;
            if (TryGetHeldItemHandPose(out var itemHandPosition, out var itemHandRotation))
            {
                drivePosition = itemHandPosition + Vector3.ClampMagnitude(targetPosition - itemHandPosition,
                    _heldLeadDistance);
                driveRotation = Quaternion.RotateTowards(itemHandRotation, targetRotation, _heldLeadAngle);
            }

            Drive(drivePosition, driveRotation, deltaTime);

            _lastTargetPosition = targetPosition;
            _lastTargetRotation = targetRotation;
        }

        private void LateUpdate()
        {
            // After all locomotion of this frame, before the next physics step (XRI's VelocityTracking runs before
            // our FixedUpdate and would fling the item at the old pose towards the new hand).
            HandleRigJump();

            if (_isActive)
                UpdateVisual();
            else
                UpdateInactiveVisual();

            // After the hand visual: the item's attach point is on it.
            UpdateHeldItemVisual();

            // The grip distance is from the last frame's ShowOnGrip(). It's set again before rendering while two hands
            // still hold the item.
            UpdateGhost();
            _gripGhostDistance = 0f;
        }

        /// <summary>
        /// Two hands hold the item: the hand is shown on its grip of the shown item, as the item's pose comes from
        /// both hands. The ghost hand shows the controller. Called by VrHeldItemVisual right before rendering, undone
        /// by RestoreFromGrip() after rendering: the attach point is on the visual, XRI must not follow the grip.
        /// </summary>
        internal void ShowOnGrip(Vector3 itemPosition, Quaternion itemRotation, Transform grip)
        {
            if (_attachPoint == null || grip != _heldGrip)
                return;

            if (!_isVisualOnGrip)
            {
                transform.GetLocalPositionAndRotation(out _visualLocalPositionBeforeGrip,
                    out _visualLocalRotationBeforeGrip);
                _isVisualOnGrip = true;
            }

            UpdateHeldGrip();
            GetHandPoseOnGrip(itemPosition, itemRotation, out var position, out var rotation);
            transform.SetPositionAndRotation(position, rotation);

            GetTargetPose(out var targetPosition, out _);
            _gripGhostDistance = Vector3.Distance(position, targetPosition);
        }

        /// <summary>
        /// Two hands hold the item and this one isn't its owner: the hand's pose on its grip, where ShowOnGrip() shows
        /// it before rendering. For bodies posed in LateUpdate (VRHeroBody's fists), which showed the controller.
        /// </summary>
        public bool TryGetSecondHandGripPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (_heldVisual == null || _heldBody == null || _attachPoint == null || _heldVisual.HolderCount < 2 ||
                _heldVisual.IsOwner(this))
                return false;

            UpdateHeldGrip();
            GetHandPoseOnGrip(_heldBody.position, _heldBody.rotation, out position, out rotation);
            return true;
        }

        internal void RestoreFromGrip()
        {
            if (!_isVisualOnGrip)
                return;

            _isVisualOnGrip = false;
            transform.SetLocalPositionAndRotation(_visualLocalPositionBeforeGrip, _visualLocalRotationBeforeGrip);
        }

        /// <summary>
        /// The other hand let go, this one shows the item now. XRI moved the grip to where the item is, so the item
        /// blends from its physics pose into the hand.
        /// </summary>
        internal void OnHeldItemOwnerChanged()
        {
            _heldVisualBlocked = 1f;
        }

        /// <summary>
        /// Grip pose relative to the item. Read every time: XRI moves the grip to the current pose when the other hand
        /// lets go. Kept if the grip is gone.
        /// </summary>
        private void UpdateHeldGrip()
        {
            if (_heldGrip == null || _heldBody == null)
                return;

            var itemTransform = _heldBody.transform;
            _heldGripLocalPosition = itemTransform.InverseTransformPoint(_heldGrip.position);
            _heldGripLocalRotation = Quaternion.Inverse(itemTransform.rotation) * _heldGrip.rotation;
        }

        /// <summary>
        /// Hand pose with the attach point on the grip of the item at the given pose. The body has the visual's scale.
        /// </summary>
        private void GetHandPoseOnGrip(Vector3 itemPosition, Quaternion itemRotation, out Vector3 position,
            out Quaternion rotation)
        {
            var gripRotation = itemRotation * _heldGripLocalRotation;
            var gripPosition = itemPosition +
                               itemRotation * Vector3.Scale(_heldBody.transform.lossyScale, _heldGripLocalPosition);

            rotation = gripRotation * Quaternion.Inverse(_attachPoint.localRotation);
            position = gripPosition - rotation * Vector3.Scale(transform.lossyScale, _attachPoint.localPosition);
        }

        private void UpdateVisual()
        {
            // Free hand: the current controller pose plus what the last physics step blocked, so there is no lag.
            // Blocked hand: the interpolated physics pose. Otherwise the controller's movement since the last physics
            // step pushes the visual a few mm into the wall until the next step puts it back (jitter at 50 Hz).
            // Interpolated (one physics step behind), so sliding along a wall doesn't step at 50 Hz either.
            GetTargetPose(out var targetPosition, out var targetRotation);
            var positionOffset = _rigidbody.position - _lastTargetPosition;
            var rotationOffset = _rigidbody.rotation * Quaternion.Inverse(_lastTargetRotation);
            var blocked = _blockedDistance > 0f ? Mathf.Clamp01(positionOffset.magnitude / _blockedDistance) : 1f;
            _body.transform.GetPositionAndRotation(out var bodyPosition, out var bodyRotation);
            transform.SetPositionAndRotation(
                Vector3.Lerp(targetPosition + positionOffset, bodyPosition, blocked),
                Quaternion.Slerp(rotationOffset * targetRotation, bodyRotation, blocked));
        }

        /// <summary>
        /// Where the hand would be on the held item (its grip), while the item is blocked by something solid.
        /// </summary>
        private bool TryGetHeldItemHandPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;

            if (_heldBody == null || _attachPoint == null)
                return false;

            UpdateHeldGrip();
            GetHandPoseOnGrip(_heldBody.position, _heldBody.rotation, out position, out rotation);

            // Still flying to the hand (distance grab, after a snap): not held in the hand yet. Once settled, a big
            // gap means the item got wedged: the hand stays with it until it snaps as stuck (and lets go).
            if (!_isHeldItemSettled)
                _isHeldItemSettled = (position - _rigidbody.position).sqrMagnitude <=
                                     _heldMaxGripDistance * _heldMaxGripDistance;

            return _isHeldItemSettled && IsHeldItemBlocked();
        }

        private bool IsHeldItemBlocked()
        {
            return Time.fixedTime - _heldBlockedTime <= _heldBlockedMemory;
        }

        /// <summary>
        /// The held item is shown glued to the hand visual (its grip on the attach point) while it's free. While it's
        /// blocked or still flying to the hand, it's shown where the physics holds it relative to the hand, so the
        /// blade stops at the wall. The physics item lags the hand visual by about a frame and a physics step.
        /// Two hands: only the first one (owner) sets the item pose.
        /// </summary>
        private void UpdateHeldItemVisual()
        {
            if (_heldVisual == null || _heldBody == null || !_heldVisual.IsOwner(this))
                return;

            UpdateHeldGrip();
            GetHeldItemGluedPose(out var gluedPosition, out var gluedRotation);
            if (!_isHeldItemSettled)
                _isHeldItemSettled = (_heldBody.position - gluedPosition).sqrMagnitude <=
                                     _heldMaxGripDistance * _heldMaxGripDistance;

            var isBlocked = !_isHeldItemSettled || IsHeldItemBlocked();
            _heldVisualBlocked = Mathf.MoveTowards(_heldVisualBlocked, isBlocked ? 1f : 0f,
                Time.deltaTime / Mathf.Max(_heldVisualBlendTime, 0.001f));

            GetHeldItemPhysicsPose(out var physicsPosition, out var physicsRotation);
            _heldVisual.SetPose(_attachPoint, Vector3.Lerp(gluedPosition, physicsPosition, _heldVisualBlocked),
                Quaternion.Slerp(gluedRotation, physicsRotation, _heldVisualBlocked));
        }

        /// <summary>
        /// Item pose with its grip on the attach point (where XRI's VelocityTracking moves it to). Two hands: the
        /// grabber's rotation (turned by both hands), the grip stays on this hand.
        /// </summary>
        private void GetHeldItemGluedPose(out Vector3 position, out Quaternion rotation)
        {
            _attachPoint.GetPositionAndRotation(out var attachPosition, out var attachRotation);
            rotation = _heldVisual != null && _heldVisual.HolderCount > 1 && _heldTargetRotation != null
                ? _heldTargetRotation()
                : attachRotation * Quaternion.Inverse(_heldGripLocalRotation);
            position = attachPosition -
                       rotation * Vector3.Scale(_heldBody.transform.lossyScale, _heldGripLocalPosition);
        }

        /// <summary>
        /// Item pose as the physics holds it relative to the physical hand, moved with the hand visual. Both bodies
        /// are from the same physics step, so the item doesn't step against the (interpolated) hand.
        /// Not physical: the item's physics pose.
        /// </summary>
        private void GetHeldItemPhysicsPose(out Vector3 position, out Quaternion rotation)
        {
            position = _heldBody.position;
            rotation = _heldBody.rotation;
            if (!_isActive)
                return;

            var inverseHandRotation = Quaternion.Inverse(_rigidbody.rotation);
            var localPosition = inverseHandRotation * (position - _rigidbody.position);
            var localRotation = inverseHandRotation * rotation;
            transform.GetPositionAndRotation(out var handPosition, out var handRotation);
            position = handPosition + handRotation * localPosition;
            rotation = handRotation * localRotation;
        }

        /// <summary>
        /// A free item was shown in the hand, but its body lags behind (up to v * 30 ms). On release, it starts where
        /// it was seen, keeping its (throw) velocity.
        /// </summary>
        private void MoveReleasedItemToHand()
        {
            if (_heldBody == null || _attachPoint == null || _heldVisualBlocked > 0.01f || IsHeldItemBlocked())
                return;

            GetHeldItemGluedPose(out var position, out var rotation);
            _heldBody.position = position;
            _heldBody.rotation = rotation;
            _heldBody.transform.SetPositionAndRotation(position, rotation);
        }

        private void ReleaseStuckItem()
        {
            if (_heldBody == null)
                return;

            Logger.LogEditor($"[VrPhysicsHand] {_side} hand released {_heldBody.name}, it was stuck with the hand.",
                LogCat.VR);
            HeldItemStuck?.Invoke();
        }

        private bool ShouldBeActive()
        {
            if (!_isPhysical || !isActiveAndEnabled)
                return false;

            // Ladders: the ClimbProvider moves the rig by the hand, blocked hands would fight it. The visual blends
            // to the controller meanwhile and the body starts from there again afterwards.
            return _rig == null || !_rig.IsClimbing;
        }

        private void SetActive(bool isActive)
        {
            if (_isActive == isActive)
                return;

            _isActive = isActive;

            if (isActive)
            {
                if (_body == null)
                    CreateBody();

                _body.SetActive(true);
                ReapplyIgnoredCollisions();

                // Starts where the visual is (still blending back after a ladder): no pop. Overlaps pass through.
                GetTargetPose(out var targetPosition, out var targetRotation);
                Snap(transform.position, transform.rotation, targetPosition, targetRotation);
                PassThroughOverlaps();
                ClearInactiveOffset();
            }
            else
            {
                // Unity can't change pairs of inactive colliders.
                _passThrough.RestoreAll();
                if (_body != null)
                    _body.SetActive(false);

                // Direct follow (5a state), reached by a short blend from where the physics hand was (ladder grabbed
                // while the hand rests on a rung).
                StartInactiveBlend();
                _separation = 0f;
                _stuckTime = 0f;
                SetGhostVisible(false);
            }
        }

        /// <summary>
        /// The visual's offset from its rest pose becomes the start of a blend back to the controller.
        /// </summary>
        private void StartInactiveBlend()
        {
            var localPosition = transform.localPosition;
            var localRotation = transform.localRotation;
            transform.SetLocalPositionAndRotation(_restPosition, _restRotation);

            if (_target == null || _inactiveBlendTime <= 0f)
            {
                ClearInactiveOffset();
                return;
            }

            // Offset in the controller's space, applied on top of the rest pose.
            var offsetPosition = localPosition - _restPosition;
            var offsetRotation = localRotation * Quaternion.Inverse(_restRotation);
            var distance = _target.TransformVector(offsetPosition).magnitude;
            if (distance > _maxDistance)
            {
                ClearInactiveOffset();
                return;
            }

            _inactiveOffsetPosition = offsetPosition;
            _inactiveOffsetRotation = offsetRotation;
            _inactiveBlendSpeed = offsetPosition.magnitude / _inactiveBlendTime;
            _inactiveBlendAngularSpeed = Quaternion.Angle(Quaternion.identity, offsetRotation) / _inactiveBlendTime;
            ApplyInactiveOffset();
        }

        private void UpdateInactiveVisual()
        {
            if (_inactiveOffsetPosition == Vector3.zero && _inactiveOffsetRotation == Quaternion.identity)
                return;

            var deltaTime = Time.deltaTime;
            _inactiveOffsetPosition = Vector3.MoveTowards(_inactiveOffsetPosition, Vector3.zero,
                _inactiveBlendSpeed * deltaTime);
            _inactiveOffsetRotation = Quaternion.RotateTowards(_inactiveOffsetRotation, Quaternion.identity,
                _inactiveBlendAngularSpeed * deltaTime);
            ApplyInactiveOffset();
        }

        private void ApplyInactiveOffset()
        {
            transform.SetLocalPositionAndRotation(_restPosition + _inactiveOffsetPosition,
                _inactiveOffsetRotation * _restRotation);
        }

        private void ClearInactiveOffset()
        {
            _inactiveOffsetPosition = Vector3.zero;
            _inactiveOffsetRotation = Quaternion.identity;
        }

        private void CreateBody()
        {
            _blocks.Clear();
            _handColliders.Clear();

            _body = new GameObject($"Physics Hand ({_side})")
            {
                layer = gameObject.layer
            };
            _body.SetActive(false);

            // The player's scene, not the active (world) scene, which is unloaded on world changes.
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
                SceneManager.MoveGameObjectToScene(_body, gameObject.scene);

            // The old body may have been destroyed with its scene.
            if (!ReferenceEquals(_rigidbody, null))
                _handsByBody.Remove(_rigidbody);

            _rigidbody = _body.AddComponent<Rigidbody>();
            _body.AddComponent<VrPhysicsHandContact>().Init(this, false);
            _handsByBody[_rigidbody] = this;
            _rigidbody.mass = _mass;
            _rigidbody.useGravity = false;
            _rigidbody.linearDamping = 0f;
            _rigidbody.angularDamping = 0f;
            // The interpolated transform is shown while the hand is blocked (smooth between the 50 Hz steps). The free
            // hand uses Rigidbody.position, which isn't interpolated.
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.maxAngularVelocity = _maxAngularSpeed;
            _rigidbody.maxDepenetrationVelocity = _maxDepenetrationSpeed;

            if (_physicsMaterial == null)
            {
                _physicsMaterial = new PhysicsMaterial("Physics Hand")
                {
                    dynamicFriction = 0.4f,
                    staticFriction = 0.5f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Average,
                    bounceCombine = PhysicsMaterialCombine.Minimum
                };
            }

            // One box per block. The blocks are unit cubes, so the visual is the collider shape.
            foreach (var blockRenderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = blockRenderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                var colliderGo = new GameObject($"{blockRenderer.name} Collider")
                {
                    layer = gameObject.layer
                };
                colliderGo.transform.SetParent(_body.transform, false);

                var box = colliderGo.AddComponent<BoxCollider>();
                var bounds = filter.sharedMesh.bounds;
                box.center = bounds.center;
                box.size = bounds.size;
                box.sharedMaterial = _physicsMaterial;

                _blocks.Add((blockRenderer.transform, colliderGo.transform));
                _handColliders.Add(box);
            }

            if (_handColliders.Count == 0)
                Logger.LogWarning($"[VrPhysicsHand] {name} ({_side}) has no blocks (MeshRenderer children). The " +
                                  "physical hand has no colliders.", LogCat.VR);

            SyncColliders();

            // Fixed mass properties: the finger colliders move with the grip, which must not change how the hand
            // reacts to contacts.
            _rigidbody.automaticCenterOfMass = false;
            _rigidbody.centerOfMass = Vector3.zero;
            _rigidbody.automaticInertiaTensor = false;
            _rigidbody.inertiaTensor = Vector3.one * (0.001f * _mass);
            _rigidbody.inertiaTensorRotation = Quaternion.identity;
        }

        /// <summary>
        /// The colliders follow the blocks of the visual (finger and thumb curl, rig scale).
        /// </summary>
        private void SyncColliders()
        {
            var scale = transform.lossyScale;
            if (_body.transform.localScale != scale)
                _body.transform.localScale = scale;

            var inverseRootRotation = Quaternion.Inverse(transform.rotation);
            foreach (var (block, blockCollider) in _blocks)
            {
                if (block == null)
                    continue;

                var localPosition = transform.InverseTransformPoint(block.position);
                var localRotation = inverseRootRotation * block.rotation;
                blockCollider.SetLocalPositionAndRotation(localPosition, localRotation);

                var blockScale = block.lossyScale;
                blockCollider.localScale = new Vector3(blockScale.x / scale.x, blockScale.y / scale.y,
                    blockScale.z / scale.z);
            }
        }

        private void Drive(Vector3 targetPosition, Quaternion targetRotation, float deltaTime)
        {
            // Position: the velocity that reaches the target in this step, reached with limited force. Max Speed is
            // relative to the rig: falling (up to 20 m/s) or a fast dive must not leave the hand behind.
            var followVelocity = (targetPosition - _rigidbody.position) / deltaTime;
            var desiredVelocity = _rigVelocity + Vector3.ClampMagnitude(followVelocity - _rigVelocity, _maxSpeed);

            // Following the controller costs no strength. Walking and the arm reach the controller once per frame,
            // not per physics step, so its speed jumps between steps (0, 1 or 2 frames of movement) and a limited
            // hand would lag and stutter. Only catching up on what a contact held back is limited (= pushing).
            var targetVelocity = (targetPosition - _lastTargetPosition) / deltaTime;
            var followChange = targetVelocity - _lastTargetVelocity;
            _lastTargetVelocity = targetVelocity;

            var maxVelocityChange = _maxForce / _rigidbody.mass * deltaTime;
            var pushChange = Vector3.ClampMagnitude(desiredVelocity - _rigidbody.linearVelocity - followChange,
                maxVelocityChange);
            _rigidbody.AddForce(followChange + pushChange, ForceMode.VelocityChange);

            // Rotation: angular velocity from the quaternion delta.
            var delta = targetRotation * Quaternion.Inverse(_rigidbody.rotation);
            delta.ToAngleAxis(out var angle, out var axis);
            if (angle > 180f)
                angle -= 360f;

            var desiredAngularVelocity = Vector3.zero;
            if (Mathf.Abs(angle) > 0.01f && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
                desiredAngularVelocity = Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / deltaTime),
                    _maxAngularSpeed);

            var angularVelocityChange = Vector3.ClampMagnitude(desiredAngularVelocity - _rigidbody.angularVelocity,
                _maxAngularAcceleration * deltaTime);
            _rigidbody.AddTorque(angularVelocityChange, ForceMode.VelocityChange);
        }

        /// <summary>
        /// Puts the body at the given pose, at rest. The target is what the next step measures the separation
        /// against (the visual's offset).
        /// </summary>
        private void Snap(Vector3 bodyPosition, Quaternion bodyRotation, Vector3 targetPosition,
            Quaternion targetRotation)
        {
            _isSnapRequested = false;
            _isHeldItemSettled = false;
            _stuckTime = 0f;
            _separation = Vector3.Distance(bodyPosition, targetPosition);

            SyncColliders();
            _body.transform.SetPositionAndRotation(bodyPosition, bodyRotation);
            _rigidbody.position = bodyPosition;
            _rigidbody.rotation = bodyRotation;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;

            _lastTargetPosition = targetPosition;
            _lastTargetRotation = targetRotation;
            _lastTargetVelocity = Vector3.zero;
        }

        /// <summary>
        /// A hand stuck behind something snaps to the controller. What held it back stays ignored until the hand is
        /// clear and back on the head's side of it. Otherwise, pulling the hand back would get it stuck again on the
        /// other side, and a snap into a wall would push the hand out to the wrong side.
        /// </summary>
        private void SnapThroughBlockers(Vector3 targetPosition, Quaternion targetRotation)
        {
            var path = targetPosition - _rigidbody.position;
            var distance = path.magnitude;
            if (distance > 0.001f)
            {
                SyncColliders();
                Physics.SyncTransforms();
                var direction = path / distance;
                var mask = VrCollisionPassThrough.GetCollidingLayers(_body.layer);

                // Cast each block from a bit behind the hand: it touches what blocks it, and casts don't report
                // colliders they start in.
                foreach (var handCollider in _handColliders)
                {
                    if (handCollider is not BoxCollider box)
                        continue;

                    var boxTransform = box.transform;
                    var center = boxTransform.TransformPoint(box.center) - direction * _snapCastBackOff;
                    var halfExtents = Vector3.Scale(box.size, boxTransform.lossyScale) * 0.5f;
                    foreach (var hit in Physics.BoxCastAll(center, halfExtents, direction, boxTransform.rotation,
                                 distance + _snapCastBackOff, mask, QueryTriggerInteraction.Ignore))
                        PassThrough(hit.collider);
                }
            }

            Snap(targetPosition, targetRotation, targetPosition, targetRotation);
            PassThroughOverlaps();
        }

        private void PassThrough(Collider other)
        {
            foreach (var handCollider in _handColliders)
                _passThrough.Add(handCollider, other);
        }

        /// <summary>
        /// Everything the hand overlaps at its new pose doesn't push it out, the hand passes through until clear.
        /// </summary>
        private void PassThroughOverlaps()
        {
            Physics.SyncTransforms();
            var count = _passThrough.AddOverlapping(_handColliders);
            if (count > 0)
                Logger.LogEditor($"[VrPhysicsHand] {_side} hand overlaps {count} collider pair(s) after a snap. " +
                                 "Passing through until clear.", LogCat.VR);
        }

        /// <summary>
        /// Teleports, snap turns and other rig jumps move the controller in one frame. The hand snaps with them
        /// instead of flying, and the held item is moved by the same offset (it would fly after the hand and catch on
        /// walls otherwise). Both pass through what they overlap at the new place until clear.
        /// </summary>
        private void HandleRigJump()
        {
            if (_rig == null)
                return;

            var root = _rig.Root;
            var rigPosition = root.position;
            var rigRotation = root.rotation;
            if (!_isRigPoseKnown)
            {
                _isRigPoseKnown = true;
                _isTeleportPending = false;
                StoreRigPose(rigPosition, rigRotation);
                return;
            }

            var hasJumped = _isTeleportPending
                            || Quaternion.Angle(_lastRigRotation, rigRotation) > _rigSnapAngle
                            || (rigPosition - _lastRigPosition).sqrMagnitude > _rigSnapDistance * _rigSnapDistance;
            _isTeleportPending = false;
            if (!hasJumped)
            {
                var deltaTime = Time.deltaTime;
                _rigVelocity = deltaTime > 0f ? (rigPosition - _lastRigPosition) / deltaTime : Vector3.zero;
                StoreRigPose(rigPosition, rigRotation);
                return;
            }

            _rigVelocity = Vector3.zero;

            var deltaRotation = rigRotation * Quaternion.Inverse(_lastRigRotation);
            var isItemMoved = MoveHeldItemWithRig(_lastRigPosition, rigPosition, deltaRotation);
            StoreRigPose(rigPosition, rigRotation);

            if (_isActive && _body != null)
            {
                GetTargetPose(out var targetPosition, out var targetRotation);
                Snap(targetPosition, targetRotation, targetPosition, targetRotation);
                PassThroughOverlaps();
            }
            else
            {
                ClearInactiveOffset();
                ApplyInactiveOffset();
            }

            if (isItemMoved)
            {
                Physics.SyncTransforms();
                _heldPassThrough.AddOverlapping(_heldColliders);
            }
        }

        private bool MoveHeldItemWithRig(Vector3 fromRigPosition, Vector3 toRigPosition, Quaternion deltaRotation)
        {
            if (_heldBody == null)
                return false;

            if (_lastMovedHeldBody == _heldBody && _lastMovedHeldFrame == Time.frameCount)
                return true;

            _lastMovedHeldBody = _heldBody;
            _lastMovedHeldFrame = Time.frameCount;

            var itemTransform = _heldBody.transform;
            var position = toRigPosition + deltaRotation * (_heldBody.position - fromRigPosition);
            var rotation = deltaRotation * _heldBody.rotation;
            itemTransform.SetPositionAndRotation(
                toRigPosition + deltaRotation * (itemTransform.position - fromRigPosition),
                deltaRotation * itemTransform.rotation);
            _heldBody.position = position;
            _heldBody.rotation = rotation;
            if (!_heldBody.isKinematic)
            {
                _heldBody.linearVelocity = deltaRotation * _heldBody.linearVelocity;
                _heldBody.angularVelocity = deltaRotation * _heldBody.angularVelocity;
            }

            Logger.LogEditor($"[VrPhysicsHand] {_side} hand moved its held item {_heldBody.name} with the rig.",
                LogCat.VR);
            return true;
        }

        private void StoreRigPose(Vector3 position, Quaternion rotation)
        {
            _lastRigPosition = position;
            _lastRigRotation = rotation;
        }

        private Vector3? GetViewpoint()
        {
            return _rig != null && _rig.Head != null ? _rig.Head.position : null;
        }

        private void OnTeleported(Vector3 position)
        {
            _isTeleportPending = true;
        }

        private void GetTargetPose(out Vector3 position, out Quaternion rotation)
        {
            position = _target.TransformPoint(_restPosition);
            rotation = _target.rotation * _restRotation;
        }

        private void UpdateGhost()
        {
            // Blocked hand, or a hand shown on its grip of a two-handed item.
            var distance = Mathf.Max(Separation, _gripGhostDistance);
            if (_isGhostVisible)
                SetGhostVisible(distance > _ghostHideDistance);
            else
                SetGhostVisible(distance > _ghostShowDistance);
        }

        private void SetGhostVisible(bool isVisible)
        {
            _isGhostVisible = isVisible;
            if (_ghost != null)
                _ghost.SetVisible(isVisible);
        }

        private void SetCollisionIgnored(Collider other, bool ignore)
        {
            foreach (var handCollider in _handColliders)
            {
                if (handCollider != null)
                    Physics.IgnoreCollision(handCollider, other, ignore);
            }
        }

        /// <summary>
        /// Unity resets ignored pairs when a collider is deactivated, which our body is while the hand isn't physical.
        /// </summary>
        private void ReapplyIgnoredCollisions()
        {
            _ignoredColliders.RemoveWhere(i => i == null);
            foreach (var other in _ignoredColliders)
                SetCollisionIgnored(other, true);
        }

        private void RestorePendingCollisions()
        {
            for (var i = _pendingRestoreColliders.Count - 1; i >= 0; i--)
            {
                var other = _pendingRestoreColliders[i];
                if (other != null && IsOverlappingHand(other))
                    continue;

                _pendingRestoreColliders.RemoveAt(i);
                _ignoredColliders.Remove(other);
                if (other != null)
                    SetCollisionIgnored(other, false);
            }
        }

        private bool IsOverlappingHand(Collider other)
        {
            var otherBounds = other.bounds;
            otherBounds.Expand(_restoreCollisionMargin * 2f);
            foreach (var handCollider in _handColliders)
            {
                if (handCollider != null && handCollider.bounds.Intersects(otherBounds))
                    return true;
            }

            return false;
        }
    }
}
