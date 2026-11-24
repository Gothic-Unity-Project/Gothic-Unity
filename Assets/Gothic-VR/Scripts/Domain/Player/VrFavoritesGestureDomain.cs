using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using RingConstants = Gothic.VR.VRConstants.FavoritesRing;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// The favorites ring's open/close gesture, owned by VrFavoritesService: both hands empty, open (no grip - climbing
    /// a ledge grips it) and above the eyes for GestureSeconds, a haptic tick per third. A hand holding the backpack
    /// counts as empty, like before (open the ring to put an item in quickly). No button: holding both triggers up
    /// there felt like forever. The hands have to come down before the next toggle.
    /// </summary>
    public class VrFavoritesGestureDomain
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly VrHapticsService _hapticsService;

        private float _gestureTime;
        private float _gestureBrokenTime;
        private bool _isGestureLatched;
        private int _gestureHapticStep;

        /// <summary>
        /// Every frame: true once the gesture was held long enough - the ring opens or closes.
        /// </summary>
        /// <param name="head">The camera, null if there's none.</param>
        /// <param name="isBlocked">The hero is busy or a hand holds a readied ring spell.</param>
        public bool Tick(Transform head, bool isBlocked)
        {
            var left = _vrPlayerService.GetHandModelGo(VrHandSide.Left);
            var right = _vrPlayerService.GetHandModelGo(VrHandSide.Right);
            var areHandsUp = head != null && left != null && right != null &&
                             left.transform.position.y > head.position.y + RingConstants.GestureHeightAboveEyes &&
                             right.transform.position.y > head.position.y + RingConstants.GestureHeightAboveEyes;

            // After a toggle the hands have to come down first - else the ring would flip every GestureSeconds.
            if (!areHandsUp)
                _isGestureLatched = false;

            var isGesture = areHandsUp && !isBlocked &&
                            _vrPlayerService.GrabbedItemLeft == null && _vrPlayerService.GrabbedItemRight == null &&
                            IsHandOpen(VrHandSide.Left) && IsHandOpen(VrHandSide.Right);
            if (_isGestureLatched)
            {
                _gestureTime = 0f;
                _gestureHapticStep = 0;
                return false;
            }

            // A hand dipping below the eyes for a moment doesn't start the hold again.
            if (!isGesture)
            {
                _gestureBrokenTime += Time.deltaTime;
                if (_gestureBrokenTime > RingConstants.GestureGraceSeconds)
                {
                    _gestureTime = 0f;
                    _gestureHapticStep = 0;
                }
                return false;
            }

            _gestureBrokenTime = 0f;
            _gestureTime += Time.deltaTime;
            var step = Mathf.FloorToInt(_gestureTime / RingConstants.GestureSeconds * 3f);
            if (step > _gestureHapticStep && step < 3)
            {
                _gestureHapticStep = step;
                _hapticsService.Vibrate(VrHandSide.Left, VrHapticPreset.Info);
                _hapticsService.Vibrate(VrHandSide.Right, VrHapticPreset.Info);
            }
            if (_gestureTime < RingConstants.GestureSeconds)
                return false;

            _isGestureLatched = true;
            _gestureTime = 0f;
            _gestureHapticStep = 0;
            _hapticsService.Vibrate(VrHandSide.Left, VrHapticPreset.Success);
            _hapticsService.Vibrate(VrHandSide.Right, VrHapticPreset.Success);
            return true;
        }

        /// <summary>
        /// No grip, or the grip holds the backpack.
        /// </summary>
        private bool IsHandOpen(VrHandSide side)
        {
            if (!_vrInput.GetButton(side, VrButton.Grip).IsActive)
                return true;

            var rig = _vrPlayerService.Rig;
            if (rig == null)
                return false;
            foreach (var hand in rig.HandGrabbers)
            {
                if (hand.HandSide == side && hand.Held != null &&
                    hand.Held.GetComponentInParent<VRBackpack>() != null)
                    return true;
            }
            return false;
        }
    }
}
