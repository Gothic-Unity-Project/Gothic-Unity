#if GOTHIC_HVR_INSTALLED
using HurricaneVR.Framework.ControllerInput;
using UnityEngine.InputSystem;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Keyboard bindings of HVR's locomotion inputs while UseWASD is set. Gothic inputs (menu, both grips) are in
    /// VRPlayer.
    /// </summary>
    public class HvrPlayerInputs : HVRPlayerInputs
    {
        protected override bool GetIsJumpActivated()
        {
            if (UseWASD)
                return Keyboard.current[Key.Space].wasPressedThisFrame;

            return RightController.PrimaryButtonState.JustActivated;
        }

        protected override bool GetCrouch()
        {
            if (UseWASD)
                return Keyboard.current[Key.Z].wasPressedThisFrame || Keyboard.current[Key.X].wasPressedThisFrame;

            return base.GetCrouch();
        }

        protected override bool GetSprinting()
        {
            if (UseWASD)
                return Keyboard.current[Key.LeftShift].wasPressedThisFrame;

            return base.GetSprinting();
        }
    }
}
#endif
