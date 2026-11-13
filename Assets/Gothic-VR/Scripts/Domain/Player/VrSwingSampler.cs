using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Models.Vob;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
using Melee = Gothic.VR.VRConstants.Melee;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// Measures one held melee weapon's movement in the rig's tracking space (IVrRig.TrackingSpace), from the tracked
    /// controllers - not the physics weapon. Walking, running, gravity, ground snapping, turning and teleports move the
    /// tracking space itself, so they never look like a swing.
    /// A swing (Ancient Dungeon VR like): hand speed AND blade tip speed over their thresholds, held for MinTimeOver,
    /// and the tip travelled MinTipPath within PathWindow. A thrust along the blade counts as a swing too.
    /// Owned by VrWeaponAttackDomain: sampled every rendered frame (Update), read in FixedUpdate. No allocations
    /// while sampling (fixed ring buffer).
    /// </summary>
    public class VrSwingSampler
    {
        /// <summary>
        /// Largest PathWindow / MinTimeOver the ring buffer holds (Marvin ranges).
        /// </summary>
        public const float MaxPathWindow = 0.4f;
        public const float MaxTimeOver = 0.2f;

        // 0.4 s of samples up to 300 Hz.
        private const int _bufferSize = 128;

        // Seconds after a grab (or a change of the holding hand) before the tip is measured: the velocity-tracked
        // weapon flies into the hand first.
        private const float _armDelay = 0.15f;

        // Seconds of samples the tip speed is measured over - a single frame is too noisy.
        private const float _speedWindow = 0.05f;

        private readonly Vector3[] _handPositions = new Vector3[_bufferSize];
        private readonly Vector3[] _tipPositions = new Vector3[_bufferSize];
        private readonly float[] _times = new float[_bufferSize];
        private int _newest = -1;
        private int _count;

        private IVrRig _rig;
        private IVrInput _input;
        private Transform _weapon;
        private MeshFilter _meshFilter;
        private VrSwingThresholds _thresholds;

        // The tip is measured from the first holding hand. The faster holding hand gives the hand speed.
        private VrHandSide _hand;
        private bool _isLeftHolding;
        private bool _isRightHolding;

        private float _armTime;
        private Vector3 _tipInHand;
        private Vector3 _bladeInWeapon;
        private float _overSince = -1f;
        private bool _isThrustNow;

        public bool IsActive => _weapon != null;

        /// <summary>
        /// The tip is known (some time after the grab). Until then no swing counts.
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// The blade's direction is known (kept on a hand change, until the weapon is let go).
        /// </summary>
        public bool HasBlade { get; private set; }

        /// <summary>
        /// Speed (m/s) of the faster holding hand, in tracking space.
        /// </summary>
        public float HandleSpeed { get; private set; }

        /// <summary>
        /// Speed (m/s) of the blade tip, in tracking space.
        /// </summary>
        public float TipSpeed { get; private set; }

        /// <summary>
        /// Path (m) of the tip within PathWindow.
        /// </summary>
        public float TipPath { get; private set; }

        /// <summary>
        /// Path (m) of the first holding hand within PathWindow (thrusts).
        /// </summary>
        public float HandPath { get; private set; }

        /// <summary>
        /// Velocity of the faster holding hand without locomotion, in world directions.
        /// </summary>
        public Vector3 HandVelocity { get; private set; }

        /// <summary>
        /// Seconds the speeds have been over the thresholds without a break.
        /// </summary>
        public float TimeOver => _overSince < 0f || _count == 0 ? 0f : _times[_newest] - _overSince;

        /// <summary>
        /// The blade's direction from the grip to the tip, in world space. Zero while unknown (HasBlade).
        /// </summary>
        public Vector3 BladeAxis => HasBlade && _weapon != null ? _weapon.TransformDirection(_bladeInWeapon)
            : Vector3.zero;

        public VrSwingThresholds Thresholds => _thresholds;

        /// <summary>
        /// A weapon is grabbed. Thresholds are taken now - changes (Marvin) apply on the next grab.
        /// </summary>
        public void Start(IVrRig rig, IVrInput input, Transform weapon, VrHandSide hand, VrSwingThresholds thresholds)
        {
            Stop();
            _rig = rig;
            _input = input;
            _weapon = weapon;
            _meshFilter = weapon.GetComponentInChildren<MeshFilter>();
            thresholds.PathWindow = Mathf.Min(thresholds.PathWindow, MaxPathWindow);
            thresholds.MinTimeOver = Mathf.Min(thresholds.MinTimeOver, MaxTimeOver);
            _thresholds = thresholds;
            _hand = hand;
            _isLeftHolding = hand == VrHandSide.Left;
            _isRightHolding = hand == VrHandSide.Right;
            Rearm();
        }

        /// <summary>
        /// The hands on the weapon changed (second hand grabbed, one let go). Old samples are dropped; if the first
        /// holding hand let go, the tip is measured again from the other one.
        /// </summary>
        public void SetHolding(bool isLeftHolding, bool isRightHolding)
        {
            if (!IsActive || (isLeftHolding == _isLeftHolding && isRightHolding == _isRightHolding))
                return;

            _isLeftHolding = isLeftHolding;
            _isRightHolding = isRightHolding;
            var isHandStillHolding = _hand == VrHandSide.Left ? isLeftHolding : isRightHolding;
            if (!isHandStillHolding && (isLeftHolding || isRightHolding))
            {
                _hand = isLeftHolding ? VrHandSide.Left : VrHandSide.Right;
                Rearm();
                return;
            }
            Clear();
        }

        /// <summary>
        /// The weapon is let go.
        /// </summary>
        public void Stop()
        {
            Clear();
            _rig = null;
            _input = null;
            _weapon = null;
            _meshFilter = null;
            IsReady = false;
            HasBlade = false;
        }

        /// <summary>
        /// Drops all samples (grab, release, hand change).
        /// </summary>
        public void Clear()
        {
            _newest = -1;
            _count = 0;
            _overSince = -1f;
            _isThrustNow = false;
            HandleSpeed = 0f;
            TipSpeed = 0f;
            TipPath = 0f;
            HandPath = 0f;
            HandVelocity = Vector3.zero;
        }

        /// <summary>
        /// Every rendered frame (tracked poses change per frame, not per physics step).
        /// </summary>
        public void Sample()
        {
            if (_weapon == null || _rig == null)
                return;

            var trackingSpace = _rig.TrackingSpace;
            var hand = _hand == VrHandSide.Left ? _rig.LeftHand : _rig.RightHand;
            if (trackingSpace == null || hand == null)
                return;

            var now = Time.time;
            if (!IsReady)
            {
                if (now < _armTime)
                    return;
                Arm(hand);
            }

            _newest = (_newest + 1) % _bufferSize;
            if (_count < _bufferSize)
                _count++;
            _times[_newest] = now;
            _handPositions[_newest] = trackingSpace.InverseTransformPoint(hand.position);
            _tipPositions[_newest] = trackingSpace.InverseTransformPoint(hand.TransformPoint(_tipInHand));

            UpdateHandVelocity(trackingSpace);
            TipSpeed = GetSpeed(_tipPositions);
            TipPath = GetPath(_tipPositions, _thresholds.PathWindow, out _);
            HandPath = GetPath(_handPositions, _thresholds.PathWindow, out _);
            UpdateTimeOver(now);
        }

        /// <summary>
        /// The whole gate: speeds over the thresholds for MinTimeOver and enough tip (thrust: hand) path.
        /// </summary>
        public bool IsSwinging()
        {
            if (!IsReady || _overSince < 0f || TimeOver < _thresholds.MinTimeOver)
                return false;

            var path = _isThrustNow ? Mathf.Max(TipPath, HandPath) : TipPath;
            return path >= _thresholds.MinTipPath;
        }

        /// <summary>
        /// A thrust: hand speed along the blade at least minSpeed and nearly all of the hand's speed
        /// (Melee.StabThrustAlignment). Swing thrusts and the stab finish use different speeds.
        /// </summary>
        public bool IsThrust(float minSpeed)
        {
            if (!HasBlade || HandleSpeed <= 0f)
                return false;

            var alongBlade = Vector3.Dot(HandVelocity, BladeAxis);
            return alongBlade >= minSpeed && alongBlade >= HandleSpeed * Melee.StabThrustAlignment;
        }

        /// <summary>
        /// The current values against the thresholds, for [VrSwing] logs. Allocates - call it inside LogEditor only.
        /// </summary>
        public string Describe()
        {
            var t = _thresholds;
            return $"hand {HandleSpeed:0.00}/{t.HandleSpeed:0.00} m/s, tip {TipSpeed:0.00}/{t.TipSpeed:0.00} m/s, " +
                   $"tip path {TipPath:0.00}/{t.MinTipPath:0.00} m in {t.PathWindow:0.00} s, " +
                   $"over {TimeOver:0.000}/{t.MinTimeOver:0.000} s" +
                   (_isThrustNow ? $", thrust (hand path {HandPath:0.00} m)" : string.Empty);
        }

        private void Rearm()
        {
            Clear();
            IsReady = false;
            _armTime = Time.time + _armDelay;
        }

        /// <summary>
        /// The weapon is in the hand now: the blade's tip (the far end of the mesh along its longest axis, away from
        /// the hand) is fixed in the hand's space. Gothic weapon meshes usually lie along their local X axis.
        /// </summary>
        private void Arm(Transform hand)
        {
            var handInWeapon = _weapon.InverseTransformPoint(hand.position);
            Vector3 endA;
            Vector3 endB;
            var mesh = _meshFilter != null ? _meshFilter.sharedMesh : null;
            if (mesh != null)
            {
                var bounds = mesh.bounds;
                var extents = bounds.extents;
                var offset = extents.x >= extents.y && extents.x >= extents.z ? new Vector3(extents.x, 0f, 0f)
                    : extents.y >= extents.z ? new Vector3(0f, extents.y, 0f)
                    : new Vector3(0f, 0f, extents.z);
                var meshTransform = _meshFilter.transform;
                endA = _weapon.InverseTransformPoint(meshTransform.TransformPoint(bounds.center + offset));
                endB = _weapon.InverseTransformPoint(meshTransform.TransformPoint(bounds.center - offset));
            }
            else
            {
                Logger.LogWarning($"[VrSwing] No mesh on {_weapon.name} - blade assumed along its X axis.", LogCat.VR);
                endA = new Vector3(0.5f, 0f, 0f);
                endB = new Vector3(-0.5f, 0f, 0f);
            }

            var isATip = (endA - handInWeapon).sqrMagnitude >= (endB - handInWeapon).sqrMagnitude;
            var tipInWeapon = isATip ? endA : endB;
            var baseInWeapon = isATip ? endB : endA;
            _bladeInWeapon = (tipInWeapon - baseInWeapon).normalized;
            _tipInHand = hand.InverseTransformPoint(_weapon.TransformPoint(tipInWeapon));
            HasBlade = _bladeInWeapon != Vector3.zero;
            IsReady = true;
        }

        private void UpdateHandVelocity(Transform trackingSpace)
        {
            var velocity = Vector3.zero;
            var speed = 0f;
            if (_isLeftHolding)
            {
                velocity = _input.GetControllerVelocity(VrHandSide.Left);
                speed = velocity.magnitude;
            }
            if (_isRightHolding)
            {
                var right = _input.GetControllerVelocity(VrHandSide.Right);
                var rightSpeed = right.magnitude;
                if (rightSpeed > speed)
                {
                    velocity = right;
                    speed = rightSpeed;
                }
            }

            HandleSpeed = speed;
            HandVelocity = trackingSpace.TransformDirection(velocity);
        }

        private void UpdateTimeOver(float now)
        {
            var isSwingOver = TipSpeed >= _thresholds.TipSpeed && HandleSpeed >= _thresholds.HandleSpeed;
            _isThrustNow = !isSwingOver && IsThrust(_thresholds.ThrustSpeed);
            if (!isSwingOver && !_isThrustNow)
            {
                _overSince = -1f;
                return;
            }

            if (_overSince < 0f)
                _overSince = now;
        }

        private float GetSpeed(Vector3[] positions)
        {
            var path = GetPath(positions, _speedWindow, out var elapsed);
            return elapsed > 0f ? path / elapsed : 0f;
        }

        /// <summary>
        /// Path length of the newest samples within window (at least the last segment, so low frame rates still
        /// measure). Limited to the buffered samples.
        /// </summary>
        private float GetPath(Vector3[] positions, float window, out float elapsed)
        {
            elapsed = 0f;
            if (_count < 2)
                return 0f;

            var newestTime = _times[_newest];
            var path = 0f;
            var index = _newest;
            for (var i = 1; i < _count; i++)
            {
                var previous = index == 0 ? _bufferSize - 1 : index - 1;
                var age = newestTime - _times[previous];
                if (i > 1 && age > window)
                    break;

                path += Vector3.Distance(positions[index], positions[previous]);
                elapsed = age;
                index = previous;
            }
            return path;
        }
    }
}
