#if GUZ_HVR_INSTALLED
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Shared;
using MyBox;
using UnityEngine.InputSystem;

namespace GUZ.VR.Adapters.HVROverrides
{
    public class VRPlayerInputs : HVRPlayerInputs
    {
        [Separator("GUZ - Settings")]
        public bool IsMenuActivated;
        public HVRButtonState MenuState;
        public bool IsMenuButtonEnabled = true;

        // Just Pressed
        public HVRButtonState BothGripsActivatedState;
        public bool IsBothGripsActivated;
        
        // Pressed
        public bool IsBothGripsActive;
        public HVRButtonState BothGripsActiveState;
        

        protected override void UpdateInput()
        {
            base.UpdateInput();

            if (!UpdateInputs)
            {
                return;
            }

            IsMenuActivated = GetMenuActivated();
            ResetState(ref MenuState);
            SetState(ref MenuState, IsMenuActivated);

            // Just Pressed
            IsBothGripsActivated = GetBothGripsActivated();
            ResetState(ref BothGripsActivatedState);
            SetState(ref BothGripsActivatedState, IsBothGripsActivated);
            
            
            // Pressed
            IsBothGripsActive = GetBothGripsActive();
            ResetState(ref BothGripsActiveState);
            SetState(ref BothGripsActiveState, IsBothGripsActive);
        }

        private bool GetMenuActivated()
        {
            if (!IsMenuButtonEnabled)
            {
                return false;
            }
            // If HVRSimulator is Active
            if (UseWASD)
            {
                return Keyboard.current[Key.Escape].wasPressedThisFrame;
            }

            return HVRController.GetButtonState(HVRHandSide.Left, HVRButtons.Secondary).JustActivated;
        }
        
        private bool GetBothGripsActivated()
        {
            if (UseWASD)
            {
                return Keyboard.current[Key.T].wasPressedThisFrame;
            }
            else
            {
                var leftButtonState = HVRController.GetButtonState(HVRHandSide.Left, HVRButtons.Grip);
                var rightButtonState = HVRController.GetButtonState(HVRHandSide.Right, HVRButtons.Grip);
                
                return leftButtonState.JustActivated && leftButtonState.Active ||
                       rightButtonState.JustActivated && rightButtonState.Active;
            }
        }
        
        private bool GetBothGripsActive()
        {
            if (UseWASD)
            {
                return Keyboard.current[Key.T].isPressed;
            }
            else
            {
                return HVRController.GetButtonState(HVRHandSide.Left, HVRButtons.Grip).Active
                       && HVRController.GetButtonState(HVRHandSide.Right, HVRButtons.Grip).Active;
            }
        }

        protected override bool GetIsJumpActivated()
        {
            // If HVRSimulator is active
            if (UseWASD)
            {
                return Keyboard.current[Key.Space].wasPressedThisFrame;
            }

            return RightController.PrimaryButtonState.JustActivated;
        }

        protected override bool GetCrouch()
        {
            // If HVRSimulator is active
            if (UseWASD)
            {
                return Keyboard.current[Key.Z].wasPressedThisFrame || Keyboard.current[Key.X].wasPressedThisFrame;
            }
            else
            {
                return base.GetCrouch();
            }
        }
        
        protected override bool GetSprinting()
        {
            // If HVRSimulator is active
            if (UseWASD)
            {
                return Keyboard.current[Key.LeftShift].wasPressedThisFrame;
            }
            else
            {
                return base.GetSprinting();
            }
        }

    }
}
#endif

