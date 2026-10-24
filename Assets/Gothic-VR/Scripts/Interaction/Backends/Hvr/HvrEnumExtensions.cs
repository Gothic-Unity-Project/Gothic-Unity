#if GOTHIC_HVR_INSTALLED
using System;
using Gothic.VR.Interaction.Models;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Player;
using HurricaneVR.Framework.Shared;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Maps our VR models to HVR types and back. Only used inside the HVR backend.
    /// </summary>
    public static class HvrEnumExtensions
    {
        public static HVRHandSide ToHvr(this VrHandSide side)
        {
            return side == VrHandSide.Left ? HVRHandSide.Left : HVRHandSide.Right;
        }

        public static VrHandSide ToVr(this HVRHandSide side)
        {
            return side == HVRHandSide.Left ? VrHandSide.Left : VrHandSide.Right;
        }

        public static VrGrabberKind ToVrKind(this HVRGrabberBase grabber)
        {
            return grabber switch
            {
                HVRHandGrabber => VrGrabberKind.Hand,
                HVRForceGrabber => VrGrabberKind.Distance,
                HVRSocket => VrGrabberKind.Socket,
                _ => VrGrabberKind.Other
            };
        }

        /// <summary>
        /// Hand of a hand or distance grabber. Left for all other grabbers.
        /// </summary>
        public static VrHandSide ToVrHandSide(this HVRGrabberBase grabber)
        {
            return grabber switch
            {
                HVRHandGrabber hand => hand.HandSide.ToVr(),
                HVRForceGrabber force when force.HandGrabber != null => force.HandGrabber.HandSide.ToVr(),
                _ => VrHandSide.Left
            };
        }

        public static HVRButtons ToHvr(this VrButton button)
        {
            return button switch
            {
                VrButton.Grip => HVRButtons.Grip,
                VrButton.Trigger => HVRButtons.Trigger,
                VrButton.Primary => HVRButtons.Primary,
                VrButton.Secondary => HVRButtons.Secondary,
                VrButton.Menu => HVRButtons.Menu,
                VrButton.JoystickButton => HVRButtons.JoystickButton,
                _ => throw new ArgumentOutOfRangeException(nameof(button), button, null)
            };
        }

        public static VrButtonState ToVr(this HVRButtonState state)
        {
            return new VrButtonState(state.Active, state.JustActivated, state.JustDeactivated);
        }

        public static HVRSitStand ToHvr(this VrSitStand sitStand)
        {
            return sitStand switch
            {
                VrSitStand.Sitting => HVRSitStand.Sitting,
                VrSitStand.Standing => HVRSitStand.Standing,
                _ => HVRSitStand.PlayerHeight
            };
        }

        public static PlayerDirectionMode ToHvr(this VrMoveDirection direction)
        {
            return direction switch
            {
                VrMoveDirection.LeftController => PlayerDirectionMode.LeftController,
                VrMoveDirection.RightController => PlayerDirectionMode.RightController,
                _ => PlayerDirectionMode.Camera
            };
        }

        public static RotationType ToHvr(this VrTurnType turnType)
        {
            return turnType == VrTurnType.Smooth ? RotationType.Smooth : RotationType.Snap;
        }
    }
}
#endif
