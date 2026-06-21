#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Services;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// Added to the oCMobWheel prefab. When the VR player's hand enters the trigger radius,
    /// pressing Grip (or F on WASD) activates the wheel which in turn opens the linked mover gate.
    /// Replicates what AI_UseMob("VWHEEL", 1) does for NPCs.
    /// </summary>
    public class VRWheelInteraction : MonoBehaviour
    {
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private bool _playerHandNear;
        private int _currentState = -1;

        private void Awake() => this.Inject();

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("PlayerHand"))
                _playerHandNear = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("PlayerHand"))
                _playerHandNear = false;
        }

        private void Update()
        {
            if (!_playerHandNear) return;

            bool activated;
            if (_vrPlayerService.VRPlayerInputs.UseWASD)
                activated = Keyboard.current[Key.F].wasPressedThisFrame;
            else
                activated = HVRController.GetButtonState(HVRHandSide.Right, HVRButtons.Grip).JustActivated
                         || HVRController.GetButtonState(HVRHandSide.Left, HVRButtons.Grip).JustActivated;

            if (!activated) return;

            ActivateWheel();
        }

        private void ActivateWheel()
        {
            var container = GetComponentInParent<VobLoader>()?.Container;
            if (container == null) return;

            var interactable = container.VobAs<IInteractiveObject>();
            if (interactable == null) return;

            // Toggle: 0 → open, already open → close
            var newState = _currentState <= 0 ? 1 : 0;
            _currentState = newState;

            // Propagate to InteractiveProperties so Wld_GetMobState returns correct value
            var props = container.PropsAs<InteractiveProperties>();
            if (props != null)
                props.State = _currentState;

            // Activate the linked mover (e.g. gate)
            var target = interactable.Target;
            if (string.IsNullOrEmpty(target)) return;

            if (!_vobService.TryGetMovers(target, out var moverContainers))
            {
                Logger.LogWarning($"[VRWheelInteraction] Mover '{target}' not found in VobsMover registry", LogCat.Vob);
                return;
            }

            Logger.Log($"[VRWheelInteraction] Activating wheel → mover '{target}' ({moverContainers.Count} instance(s)) state={_currentState}", LogCat.Vob);
            foreach (var moverContainer in moverContainers)
            {
                var moverAdapter = moverContainer?.Go?.GetComponentInChildren<MoverAdapter>();
                if (moverAdapter != null) moverAdapter.Toggle();
            }
        }
    }
}
#endif
