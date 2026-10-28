using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// Player-initiated teleport: pulling the thumbstick back shows an arc from the hand, releasing it teleports to
    /// the arc's end if the player fits there. The world is created at runtime, so there are no TeleportationArea
    /// interactables. The arc is checked against the physics world instead. The teleport itself is done by XRI's
    /// TeleportationProvider (via the onTeleport callback).
    /// Releasing at an invalid spot (red) does nothing. The cancel button (grip) cancels until the stick is released.
    /// </summary>
    public class XriTeleportAimer : MonoBehaviour
    {
        private const int _reticleSegments = 24;

        [SerializeField] private XriTeleportSettings _settings = new();

        [Tooltip("Line of the aiming arc. Material, width and shadows are set on the component.")]
        [SerializeField] private LineRenderer _arc;

        [Tooltip("Ring at the arc's end. Its points are written at runtime, so it needs Loop enabled.")]
        [SerializeField] private LineRenderer _reticle;

        private Vector3[] _points;

        private InputAction _stickAction;
        private InputAction _cancelAction;
        private Transform _hand;
        private Transform _head;
        private CharacterController _character;
        private LayerMask _blockingLayers;
        private Func<bool> _isAllowed;
        private Func<bool> _isGrounded;
        private Func<float> _getStandingHeight;
        private Func<float> _getMaxSlopeAngle;
        private Func<Vector3, bool> _isClimbableTop;
        private Action<Vector3, Quaternion> _onTeleport;
        private bool _isInitialized;
        private bool _isAiming;
        private bool _isCanceled;
        private bool _hasTarget;
        private Vector3 _target;

        /// <summary>
        /// Called by XriRig, which owns the rig references. Aiming starts only after this.
        /// isClimbableTop: a target higher than MaxJumpDistance is valid if it's the top of a ledge Gothic's hero
        /// climbs (VR ledge climbing).
        /// </summary>
        public void Init(InputAction stickAction, InputAction cancelAction, Transform hand, Transform head,
            CharacterController character, LayerMask blockingLayers, Func<bool> isAllowed, Func<bool> isGrounded,
            Func<float> getStandingHeight, Func<float> getMaxSlopeAngle, Func<Vector3, bool> isClimbableTop,
            Action<Vector3, Quaternion> onTeleport)
        {
            _stickAction = stickAction;
            _cancelAction = cancelAction;
            _hand = hand;
            _head = head;
            _character = character;
            _blockingLayers = blockingLayers;
            _isAllowed = isAllowed;
            _isGrounded = isGrounded;
            _getStandingHeight = getStandingHeight;
            _getMaxSlopeAngle = getMaxSlopeAngle;
            _isClimbableTop = isClimbableTop;
            _onTeleport = onTeleport;

            _stickAction.Enable();
            _cancelAction?.Enable();

            _points = new Vector3[_settings.ArcSamples + 1];
            _reticle.positionCount = _reticleSegments;
            SetVisible(false);
            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized)
                return;

            // The Sector interaction (South, Locked) performs when the stick is pulled back first, stays performed
            // when it's turned sideways and cancels once the stick returns to the center. A stick pushed sideways
            // first (snap turn) never performs.
            if (_stickAction.phase != InputActionPhase.Performed)
            {
                if (_isAiming && _hasTarget && _isAllowed() && _isGrounded())
                    _onTeleport(_target, Quaternion.Euler(0f, _head.eulerAngles.y, 0f));
                Stop();
                _isCanceled = false;
                return;
            }

            if (_isCanceled)
                return;

            if (_isAiming && _cancelAction != null && _cancelAction.WasPressedThisFrame())
            {
                Stop();
                _isCanceled = true;
                return;
            }

            if (!_isAllowed())
            {
                Stop();
                return;
            }

            _isAiming = true;
            UpdateArc();
        }

        private void Stop()
        {
            _isAiming = false;
            _hasTarget = false;
            SetVisible(false);
        }

        private void UpdateArc()
        {
            _hasTarget = false;

            var origin = _hand.position;
            var velocity = _hand.forward * _settings.ArcSpeed;
            var gravity = Vector3.down * _settings.ArcGravity;
            var step = _settings.ArcStep / _settings.ArcSpeed;
            var previous = origin;
            var normal = Vector3.up;
            var isHit = false;
            var count = 0;
            _points[count++] = origin;

            for (var i = 1; i <= _settings.ArcSamples; i++)
            {
                var t = i * step;
                var point = origin + velocity * t + 0.5f * t * t * gravity;
                var segment = point - previous;

                if (Physics.SphereCast(previous, _settings.ArcRadius, segment.normalized, out var hit,
                        segment.magnitude, _blockingLayers, QueryTriggerInteraction.Ignore))
                {
                    _points[count++] = hit.point;
                    _target = hit.point;
                    normal = hit.normal;
                    isHit = true;
                    break;
                }

                _points[count++] = point;
                previous = point;
            }

            // Mid-air (jumping, falling) the arc stays red, so that teleports can't extend jumps or cancel falls.
            if (isHit)
                SnapToGround(ref _target, ref normal);

            _hasTarget = isHit && _isGrounded() && IsValidTarget(_target, normal);

            var color = _hasTarget ? _settings.ValidColor : _settings.InvalidColor;
            _arc.startColor = _arc.endColor = color;
            _arc.positionCount = count;
            _arc.SetPositions(_points);
            _arc.enabled = true;

            if (isHit)
                DrawReticle(_target, normal, color);
            else
                _reticle.enabled = false;
        }

        /// <summary>
        /// The sphere cast's normal is the contact normal of the sphere. On edges and steps it is tilted and says
        /// nothing about the ground. A ray from above the hit (like HVR's downward ray) gives the surface the player
        /// would stand on.
        /// </summary>
        private void SnapToGround(ref Vector3 point, ref Vector3 normal)
        {
            var origin = point + Vector3.up * _settings.GroundRayHeight;
            if (Physics.Raycast(origin, Vector3.down, out var hit, _settings.GroundRayHeight * 2f, _blockingLayers,
                    QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
            }
        }

        /// <summary>
        /// Walkable slope (OpenGothic MoveAlgo::testSlide: the NPC slides once normal.y < cos(slide_angle)), height
        /// difference to the capsule's bottom (HVR CheckVerticalDistance / CheckFallDistance; higher onto a climbable
        /// ledge), enough room for the player's standing capsule (also while crouching, as the player stands up
        /// afterwards) and a free line of sight from the head (HVR CheckDestinationLineOfSight).
        /// </summary>
        private bool IsValidTarget(Vector3 point, Vector3 normal)
        {
            var maxSlopeAngle = _getMaxSlopeAngle();
            if (maxSlopeAngle <= 0f)
                maxSlopeAngle = _character.slopeLimit;
            if (normal.y < Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad))
                return false;

            // Same as HVR's GetBottom() for a CharacterController.
            var capsuleBottom = _character.transform.TransformPoint(_character.center).y - _character.height * 0.5f;
            if (point.y < capsuleBottom - _settings.MaxDropDistance)
                return false;

            var radius = _character.radius;
            var height = Mathf.Max(_character.height, _getStandingHeight());
            var bottom = point + Vector3.up * (radius + _character.stepOffset);
            var top = point + Vector3.up * Mathf.Max(height - radius, radius + _character.stepOffset);

            if (Physics.CheckCapsule(bottom, top, radius * 0.95f, _blockingLayers, QueryTriggerInteraction.Ignore))
                return false;

            if (!HasLineOfSight(point))
                return false;

            // Last, as it probes the most.
            return point.y <= capsuleBottom + _settings.MaxJumpDistance ||
                   (_isClimbableTop != null && _isClimbableTop(point));
        }

        /// <summary>
        /// One free line from the head to any of the points above the target is enough, so that stairs and ledges
        /// near the target don't block it, but walls between player and target do.
        /// </summary>
        private bool HasLineOfSight(Vector3 point)
        {
            var head = _head.position;
            var samples = Mathf.Max(1, _settings.LineOfSightSamples);

            for (var i = 0; i < samples; i++)
            {
                var target = point + Vector3.up * (i / (float)samples * _settings.LineOfSightHeight);
                var direction = target - head;
                if (!Physics.Raycast(head, direction, direction.magnitude, _blockingLayers,
                        QueryTriggerInteraction.Ignore))
                    return true;
            }

            return false;
        }

        private void DrawReticle(Vector3 center, Vector3 normal, Color color)
        {
            var rotation = Quaternion.FromToRotation(Vector3.up, normal);
            for (var i = 0; i < _reticleSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / _reticleSegments;
                var local = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _settings.ReticleRadius;
                _reticle.SetPosition(i, center + normal * 0.02f + rotation * local);
            }

            _reticle.startColor = _reticle.endColor = color;
            _reticle.enabled = true;
        }

        private void SetVisible(bool isVisible)
        {
            _arc.enabled = isVisible;
            _reticle.enabled = isVisible;
        }
    }
}
