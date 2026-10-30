using System.Collections;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Models.Movement;
using Gothic.Core.Services.Config;
using Gothic.VR.Domain.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// The climb of ledge climbing (Docs/ledge-climbing.md): what gripping a lip (VrLedgeReach) does.
    /// - Physical: the backend climbs (XRI: ClimbProvider). Once pulled up and with the head above the top, the player
    ///   is put onto the top. Gripping a table's edge alone does nothing.
    /// - Comfort (Gothic.ini ledgeClimbMode): the grip fades out, puts the player on top and fades in. A jump in front
    ///   of a lip starts the same blink (VrLedgeLocomotion).
    /// </summary>
    [RequireComponent(typeof(VrLedgeReach))]
    public class VrLedgeClimb : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly IVrHaptics _vrHaptics;

        [Header("Physical")]
        [Tooltip("Height in m the player must have pulled up since gripping before stepping onto the top. " +
                 "Gripping a table's edge alone does nothing.")]
        [SerializeField]
        private float _minPull = 0.25f;

        [Tooltip("The head needs to be this far (m) above the top to step onto it.")] [SerializeField]
        private float _headAboveTop = 0.15f;

        [Tooltip("Seconds until the next step onto the top is tried after a failed one.")] [SerializeField]
        private float _retryInterval = 0.5f;

        [Header("Comfort")]
        [Tooltip("Speed (alpha per second) of the fade out and in of a comfort climb.")] [SerializeField]
        private float _fadeSpeed = 4f;

        private VrLedgeClimber _climber;
        private VrLedgeReach _reach;
        private VrLedgeClimbMode _mode = (VrLedgeClimbMode)VRConstants.LedgeClimbModeDefaultValue;
        private float _grabFeetY;
        private int _holdCount;
        private float _nextStepOnTime;
        private bool _isStepOnFailureLogged;
        private VrLedgeAnchor _comfortAnchor;
        private VrHandSide _comfortHand;

        public bool IsComfortMode => _mode == VrLedgeClimbMode.Comfort;

        public bool IsBlinking { get; private set; }

        private void Start()
        {
            _reach = GetComponent<VrLedgeReach>();
            _climber = _reach.Climber;
            if (_climber == null)
            {
                enabled = false;
                return;
            }

            _reach.Grabbed += OnAnchorGrabbed;
            _reach.Released += OnAnchorReleased;

            ReadSettings();
            GlobalEventDispatcher.GothicInisInitialized.AddListener(ReadSettings);
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.GothicInisInitialized.RemoveListener(ReadSettings);
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);

            if (_reach == null)
                return;

            _reach.Grabbed -= OnAnchorGrabbed;
            _reach.Released -= OnAnchorReleased;
        }

        private void Update()
        {
            if (IsBlinking)
                return;

            if (_comfortAnchor != null)
            {
                StartComfortClimb();
                return;
            }

            if (_holdCount > 0)
                TryStepOntoTop(_climber.Feet);
        }

        /// <summary>
        /// Comfort climb onto the ledge: fade out, put onto the top, fade in. false if there's no room on top.
        /// </summary>
        public bool TryBlinkOnto(LedgeInfo info)
        {
            if (!isActiveAndEnabled || IsBlinking || !_climber.TryGetStandPoint(info, out var standPoint))
                return false;

            StartCoroutine(BlinkOnto(standPoint));
            return true;
        }

        private void OnAnchorGrabbed(VrLedgeAnchor anchor, VrGrabArgs args)
        {
            if (!args.IsHandOrDistance)
                return;

            _holdCount++;
            if (_holdCount == 1)
            {
                _grabFeetY = _climber.Feet.y;
                _isStepOnFailureLogged = false;
            }

            // Released in the next Update(), not within XRI's select event.
            if (IsComfortMode)
            {
                _comfortAnchor = anchor;
                _comfortHand = args.HandSide;
            }
        }

        private void OnAnchorReleased(VrGrabArgs args)
        {
            if (args.IsHandOrDistance)
                _holdCount = Mathf.Max(0, _holdCount - 1);
        }

        private void StartComfortClimb()
        {
            var info = _comfortAnchor.Info;
            _comfortAnchor = null;
            ReleaseAnchors();

            if (!TryBlinkOnto(info))
                _vrHaptics.Vibrate(_comfortHand, VrHapticPreset.Error);
        }

        /// <summary>
        /// Physical climbing: pulled up far enough and the head above the top, the player is put onto it.
        /// </summary>
        private void TryStepOntoTop(Vector3 feet)
        {
            if (Time.time < _nextStepOnTime || feet.y - _grabFeetY < _minPull)
                return;

            var anchor = _reach.GetHeldAnchor();
            if (anchor == null || _climber.Rig.Head.position.y < anchor.Info.EdgePoint.y + _headAboveTop)
                return;

            if (!_climber.TryGetStandPoint(anchor.Info, out var standPoint))
            {
                _nextStepOnTime = Time.time + _retryInterval;
                if (!_isStepOnFailureLogged)
                {
                    _isStepOnFailureLogged = true;
                    Logger.LogWarning($"[Ledge] No room on top of the lip at {anchor.Info.EdgePoint}.", LogCat.VR);
                }

                return;
            }

            ReleaseAnchors();
            _climber.Rig.Teleport(standPoint, _climber.HeadYaw);
        }

        private void ReleaseAnchors()
        {
            _reach.ReleaseAnchors();
            _holdCount = 0;
        }

        /// <summary>
        /// Like VRSeat. The reach is suspended meanwhile, so that no anchor shows up during the fade.
        /// </summary>
        private IEnumerator BlinkOnto(Vector3 standPoint)
        {
            IsBlinking = true;
            ReleaseAnchors();
            _reach.IsSuspended = true;

            var rig = _climber.Rig;
            rig.Fade(1f, _fadeSpeed);
            var timeout = Time.time + 1f / _fadeSpeed + 0.5f;
            yield return new WaitUntil(() => rig.FadeAlpha >= 1f || Time.time > timeout);

            rig.Teleport(standPoint, _climber.HeadYaw);
            yield return null;

            rig.Fade(0f, _fadeSpeed);
            _reach.IsSuspended = false;
            IsBlinking = false;
        }

        private void ReadSettings()
        {
            var gothicIni = _configService.Gothic;
            if (gothicIni != null)
                _mode = (VrLedgeClimbMode)gothicIni.GetInt(VRConstants.IniNames.LedgeClimbMode,
                    VRConstants.LedgeClimbModeDefaultValue);
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == VRConstants.IniNames.LedgeClimbMode)
                ReadSettings();
        }
    }
}
