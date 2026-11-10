using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using RingConstants = Gothic.VR.VRConstants.FavoritesRing;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// The favorites ring's open/close gesture, owned by VrFavoritesService: both hands empty and above the eyes, both
    /// triggers held for GestureSeconds (a haptic tick per third). The triggers have to be let go before the next
    /// toggle.
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
            var leftTrigger = _vrInput.GetButton(VrHandSide.Left, VrButton.Trigger).IsActive;
            var rightTrigger = _vrInput.GetButton(VrHandSide.Right, VrButton.Trigger).IsActive;

            if (!leftTrigger || !rightTrigger)
                _isGestureLatched = false;

            var isGesture = head != null && left != null && right != null && leftTrigger && rightTrigger &&
                            _vrPlayerService.GrabbedItemLeft == null && _vrPlayerService.GrabbedItemRight == null &&
                            left.transform.position.y > head.position.y + RingConstants.GestureHeightAboveEyes &&
                            right.transform.position.y > head.position.y + RingConstants.GestureHeightAboveEyes &&
                            !isBlocked;
            if (_isGestureLatched)
            {
                _gestureTime = 0f;
                _gestureHapticStep = 0;
                return false;
            }

            // A hand dipping below the eyes or a trigger flickering for a moment
            // doesn't start the hold again - it took much longer than GestureSeconds.
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
    }
}
