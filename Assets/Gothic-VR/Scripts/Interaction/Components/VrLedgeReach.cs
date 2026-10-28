using System;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Movement;
using Gothic.VR.Domain.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// The reach of ledge climbing (Docs/ledge-climbing.md): a VrLedgeAnchor follows each empty hand along the
    /// climbable lip it reaches, so that the hand can grip it. What a grip does is VrLedgeClimb's part.
    /// Hints (Gothic.ini ledgeHints): a hand near a climbable lip half closes and ticks once, a short strip glows on
    /// the lip. Nothing is shown from afar.
    /// Owns whether ledge climbing is on (Climber): VrLedgeClimb and VrLedgeLocomotion work only while the reach does.
    ///
    /// Sits on the player rig. Runs before XRI's interaction manager (-105), so that the anchors are in place when the
    /// hands look for something to grab. Lips count from the floor the player last stood on, a jump doesn't lower
    /// them.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class VrLedgeReach : MonoBehaviour
    {
        [Inject] private readonly LedgeService _ledgeService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly IVrHaptics _vrHaptics;

        [Tooltip("Grab point put on a lip. One is created per hand.")] [SerializeField]
        private VrLedgeAnchor _anchorPrefab;

        [Tooltip("Distance in m between hand and lip from which the hand half closes and ticks.")] [SerializeField]
        private float _touchRange = 0.2f;

        [Tooltip("Distance in m between hand and lip from which the lip's strip starts to glow (Near, Assist).")]
        [SerializeField]
        private float _glowRange = 0.45f;

        [Tooltip("Amplitude (0-1) of the tick when a hand comes within touch range of a lip.")] [SerializeField]
        private float _tickAmplitude = 0.3f;

        [Tooltip("A placed anchor stays while the hand is within its grab box, grown by this margin (m).")]
        [SerializeField]
        private float _anchorKeepMargin = 0.05f;

        private readonly VrLedgeAnchor[] _anchors = new VrLedgeAnchor[2];
        private readonly VrBlockHand[] _blockHands = new VrBlockHand[2];
        private readonly IVrGrabber[] _grabbers = new IVrGrabber[2];
        private readonly bool[] _isInTouchRange = new bool[2];

        private VrLedgeClimber _climber;
        private bool _isClimberChecked;
        private GameObject _anchorRoot;
        private VrLedgeHint _hint = (VrLedgeHint)VRConstants.LedgeHintsDefaultValue;
        private float _baseFeetY;

        /// <summary>
        /// A hand gripped an anchor. Raised within XRI's select event.
        /// </summary>
        public event Action<VrLedgeAnchor, VrGrabArgs> Grabbed;

        public event Action<VrGrabArgs> Released;

        /// <summary>
        /// The player as climber. null: ledge climbing is off (DeveloperConfig, no IVrRig next to it or no anchor
        /// prefab). Decided once, on the first access.
        /// </summary>
        public VrLedgeClimber Climber
        {
            get
            {
                if (!_isClimberChecked)
                {
                    _isClimberChecked = true;
                    _climber = CreateClimber();
                }

                return _climber;
            }
        }

        /// <summary>
        /// Hides the anchors and hints while true (VrLedgeClimb during a comfort climb).
        /// </summary>
        public bool IsSuspended { get; set; }

        private void Start()
        {
            if (Climber == null)
            {
                enabled = false;
                return;
            }

            CreateAnchors();
            FindHands();
            ReadSettings();

            GlobalEventDispatcher.GothicInisInitialized.AddListener(ReadSettings);
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
        }

        private void OnDisable()
        {
            if (_anchorRoot == null)
                return;

            HideHand(VrHandSide.Left);
            HideHand(VrHandSide.Right);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.GothicInisInitialized.RemoveListener(ReadSettings);
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);

            if (_anchorRoot != null)
                Destroy(_anchorRoot);
        }

        private void Update()
        {
            if (IsSuspended)
            {
                HideHand(VrHandSide.Left);
                HideHand(VrHandSide.Right);
                return;
            }

            var rig = _climber.Rig;
            if (rig.IsGrounded && !rig.IsClimbing)
                _baseFeetY = _climber.Feet.y;

            var isMoved = UpdateHand(VrHandSide.Left);
            isMoved |= UpdateHand(VrHandSide.Right);

            // Physics.autoSyncTransforms is off: the hands' grab casts would see the anchors' old places.
            if (isMoved)
                Physics.SyncTransforms();
        }

        /// <summary>
        /// The held anchor on the highest lip, null if no hand grips one.
        /// </summary>
        public VrLedgeAnchor GetHeldAnchor()
        {
            VrLedgeAnchor highest = null;
            foreach (var anchor in _anchors)
            {
                if (anchor != null && anchor.IsHeld &&
                    (highest == null || anchor.Info.EdgePoint.y > highest.Info.EdgePoint.y))
                    highest = anchor;
            }

            return highest;
        }

        public void ReleaseAnchors()
        {
            foreach (var anchor in _anchors)
            {
                if (anchor != null && anchor.IsHeld)
                    anchor.Interactable.ForceRelease();
            }
        }

        /// <summary>
        /// Places or hides the hand's anchor and its hints. Returns whether the anchor moved.
        /// </summary>
        private bool UpdateHand(VrHandSide side)
        {
            var index = (int)side;
            var anchor = _anchors[index];
            if (anchor.IsHeld)
            {
                SetTouch(side, false);
                anchor.SetGlow(0f);
                return false;
            }

            if (!CanReach(side))
            {
                HideHand(side);
                return false;
            }

            var hand = GetHand(side).position;
            if (anchor.Contains(hand, _anchorKeepMargin))
            {
                UpdateHints(side, Vector3.Distance(hand, VrLedgeClimber.ClosestOnLip(anchor.Info, hand)));
                return false;
            }

            var info = _climber.ProbeAtHand(hand, _baseFeetY);
            var point = VrLedgeClimber.ClosestOnLip(info, hand);
            var distance = Vector3.Distance(hand, point);
            if (!info.IsClimbable || distance > GetPlaceRange())
            {
                HideHand(side);
                return false;
            }

            anchor.Place(info, point, _hint == VrLedgeHint.Assist);
            UpdateHints(side, distance);
            return true;
        }

        private bool CanReach(VrHandSide side)
        {
            var grabber = _grabbers[(int)side];
            return _climber.Rig.IsGrabbingEnabled(side) && (grabber == null || grabber.Held == null);
        }

        private void UpdateHints(VrHandSide side, float distance)
        {
            SetTouch(side, _hint != VrLedgeHint.Off && distance <= _touchRange);

            var isGlowing = _hint is VrLedgeHint.Near or VrLedgeHint.Assist;
            var glow = isGlowing ? 1f - Mathf.InverseLerp(_touchRange * 0.5f, _glowRange, distance) : 0f;
            _anchors[(int)side].SetGlow(glow);
        }

        private void SetTouch(VrHandSide side, bool isTouching)
        {
            var index = (int)side;
            if (_blockHands[index] != null)
                _blockHands[index].HintCurl = isTouching ? 1f : 0f;

            if (isTouching == _isInTouchRange[index])
                return;

            _isInTouchRange[index] = isTouching;
            if (isTouching)
                _vrHaptics.Vibrate(side, VrHapticPreset.Contact, _tickAmplitude);
        }

        private void HideHand(VrHandSide side)
        {
            var anchor = _anchors[(int)side];
            if (!anchor.IsHeld)
                anchor.Hide();

            SetTouch(side, false);
        }

        private float GetPlaceRange()
        {
            return _hint is VrLedgeHint.Near or VrLedgeHint.Assist ? Mathf.Max(_touchRange, _glowRange) : _touchRange;
        }

        private Transform GetHand(VrHandSide side)
        {
            return side == VrHandSide.Left ? _climber.Rig.LeftHand : _climber.Rig.RightHand;
        }

        private VrLedgeClimber CreateClimber()
        {
            if (!_configService.Dev.EnableVrLedgeClimbing)
                return null;

            var rig = GetComponent<IVrRig>();
            if (rig == null || _anchorPrefab == null)
            {
                Logger.LogError($"[Ledge] {name}: {nameof(VrLedgeReach)} needs an IVrRig next to it and an anchor " +
                                "prefab. Ledges can't be climbed.", LogCat.VR);
                return null;
            }

            var layers = VrCollisionPassThrough.GetCollidingLayers(rig.CharacterController.gameObject.layer);
            return new VrLedgeClimber(rig, _ledgeService, layers);
        }

        /// <summary>
        /// The anchors live in the player's scene, not under the rig: they stay where the lip is.
        /// </summary>
        private void CreateAnchors()
        {
            _anchorRoot = new GameObject("Ledge Anchors");
            SceneManager.MoveGameObjectToScene(_anchorRoot, gameObject.scene);

            for (var i = 0; i < _anchors.Length; i++)
            {
                var anchor = Instantiate(_anchorPrefab, _anchorRoot.transform);
                anchor.name = $"Ledge Anchor {(VrHandSide)i}";
                anchor.Interactable.Grabbed += args => Grabbed?.Invoke(anchor, args);
                anchor.Interactable.Released += args => Released?.Invoke(args);
                _anchors[i] = anchor;
            }
        }

        private void FindHands()
        {
            foreach (var blockHand in GetComponentsInChildren<VrBlockHand>(true))
                _blockHands[(int)blockHand.Side] = blockHand;

            foreach (var grabber in _climber.Rig.HandGrabbers)
            {
                if (grabber.Kind == VrGrabberKind.Hand)
                    _grabbers[(int)grabber.HandSide] = grabber;
            }
        }

        private void ReadSettings()
        {
            var gothicIni = _configService.Gothic;
            if (gothicIni != null)
                _hint = (VrLedgeHint)gothicIni.GetInt(VRConstants.IniNames.LedgeHints,
                    VRConstants.LedgeHintsDefaultValue);
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == VRConstants.IniNames.LedgeHints)
                ReadSettings();
        }
    }
}
