using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Minecraft-style hand built from primitive blocks: palm, a finger block hinged at the knuckles and a thumb.
    /// Sits on the visual root of one hand. The finger block closes with the grip value, the thumb with thumb touch.
    /// While the hand holds something, the fingers stay at a fixed holding angle (no deep clipping into the item);
    /// while grabbing is disabled (swimming), the hand is open.
    ///
    /// Only the visual: no colliders, no physics. The root can be reparented (e.g. under a physics hand) and finds
    /// the rig once in Awake.
    /// </summary>
    public class VrBlockHand : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;

        [SerializeField] private VrHandSide _side;

        [Header("Fingers")]
        [Tooltip("Knuckle hinge. Its local rotation is set around Finger Axis by the curl.")]
        [SerializeField] private Transform _fingerHinge;

        [Tooltip("Local axis of the knuckle hinge. A positive angle closes the fingers towards the palm.")]
        [SerializeField] private Vector3 _fingerAxis = Vector3.up;

        [Tooltip("Finger angle in degrees at full grip.")]
        [SerializeField] private float _fingerClosedAngle = 100f;

        [Tooltip("Finger angle in degrees while the hand holds an item (less than closed: no clipping into it).")]
        [SerializeField] private float _fingerHoldingAngle = 70f;

        [Tooltip("Finger angle in degrees at full HintCurl (a climbable lip within reach): half closed.")]
        [SerializeField] private float _fingerHintAngle = 40f;

        [Header("Thumb")]
        [Tooltip("Thumb hinge. Its local rotation is set around Thumb Axis while the thumb touches a button/stick.")]
        [SerializeField] private Transform _thumbHinge;

        [Tooltip("Local axis of the thumb hinge. A positive angle moves the thumb down onto the controller.")]
        [SerializeField] private Vector3 _thumbAxis = Vector3.right;

        [Tooltip("Thumb angle in degrees while touching.")]
        [SerializeField] private float _thumbClosedAngle = 35f;

        [Tooltip("Speed in degrees per second, with which the blocks follow the input. Hides digital grip jumps.")]
        [SerializeField] private float _angleSpeed = 900f;

        private IVrRig _rig;
        private IVrGrabber _grabber;
        private Quaternion _fingerRestRotation;
        private Quaternion _thumbRestRotation;
        private float _fingerAngle;
        private float _thumbAngle;

        public VrHandSide Side => _side;

        /// <summary>
        /// 0 (none) to 1: the fingers close at least this part of the hint angle (VrLedgeClimb, a lip within reach).
        /// </summary>
        public float HintCurl { get; set; }

        private void Awake()
        {
            _rig = GetComponentInParent<IVrRig>();
            if (_rig == null)
                Logger.LogWarning($"[VrBlockHand] {name} ({_side}) has no IVrRig parent. The hand stays open " +
                                  "while holding and swimming.", LogCat.VR);

            if (_fingerHinge != null)
                _fingerRestRotation = _fingerHinge.localRotation;
            if (_thumbHinge != null)
                _thumbRestRotation = _thumbHinge.localRotation;
        }

        private void Start()
        {
            _grabber = FindGrabber();
        }

        private void Update()
        {
            if (_vrInput == null)
                return;

            GetTargetAngles(out var fingerTarget, out var thumbTarget);

            var maxDelta = _angleSpeed * Time.deltaTime;
            _fingerAngle = Mathf.MoveTowards(_fingerAngle, fingerTarget, maxDelta);
            _thumbAngle = Mathf.MoveTowards(_thumbAngle, thumbTarget, maxDelta);

            if (_fingerHinge != null)
                _fingerHinge.localRotation = _fingerRestRotation * Quaternion.AngleAxis(_fingerAngle, _fingerAxis);
            if (_thumbHinge != null)
                _thumbHinge.localRotation = _thumbRestRotation * Quaternion.AngleAxis(_thumbAngle, _thumbAxis);
        }

        private void GetTargetAngles(out float finger, out float thumb)
        {
            if (_rig != null && !_rig.IsGrabbingEnabled(_side))
            {
                finger = 0f;
                thumb = 0f;
                return;
            }

            thumb = _vrInput.IsThumbTouching(_side) ? _thumbClosedAngle : 0f;
            finger = _grabber?.Held != null
                ? _fingerHoldingAngle
                : Mathf.Max(_vrInput.GetGripValue(_side) * _fingerClosedAngle, HintCurl * _fingerHintAngle);
        }

        /// <summary>
        /// The direct grabber of this hand (XRI: the Near-Far interactor, which also ends distance grabs in the hand).
        /// </summary>
        private IVrGrabber FindGrabber()
        {
            if (_rig == null)
                return null;

            foreach (var grabber in _rig.HandGrabbers)
            {
                if (grabber.Kind == VrGrabberKind.Hand && grabber.HandSide == _side)
                    return grabber;
            }

            Logger.LogWarning($"[VrBlockHand] No hand grabber for the {_side} hand. The fingers won't hold items.",
                LogCat.VR);
            return null;
        }
    }
}
