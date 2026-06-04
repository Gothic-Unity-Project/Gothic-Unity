#if GOTHIC_HVR_INSTALLED
using Gothic.Core;
using Gothic.Core.Models.Container;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;

namespace Gothic.VR.Services
{
    public class VrHapticsService
    {
        public enum VibrationType
        {
            Info,
            Warning,
            Error,
            Success
        }

        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VRWeaponService _vrWeaponService;

        private readonly HapticData[] _vibrationData =
        {
            // Info - e.g., you are inside an interaction collider
            new (0.15f, 0.25f, 30f),

            // Warning - e.g., your lock pick rotation is invalid
            new (0.25f, 0.5f, 100f),

            // Error - e.g., your key is broken
            new (0.4f, 0.8f, 175f),

            // Success - e.g., you opened a chest
            new (0.5f, 0.6f, 80f)
        };

        public VrHapticsService()
        {
            // Fight
            GlobalEventDispatcher.FightWindowAttack.AddListener(combatant => AttackVibration(combatant, VibrationType.Success)); // Attack can happen now
            GlobalEventDispatcher.FightWindowCombo.AddListener(combatant => AttackVibration(combatant, VibrationType.Info)); // Now you can start another attack
            GlobalEventDispatcher.FightWindowComboFailed.AddListener(combatant => AttackVibration(combatant, VibrationType.Error)); // Attack window passed

            // Lock picking
            GlobalEventDispatcher.LockPickComboWrong.AddListener((_, _, handSide) => Vibrate((HVRHandSide)handSide, VibrationType.Warning));
            GlobalEventDispatcher.LockPickComboBroken.AddListener((_, _, handSide) => Vibrate((HVRHandSide)handSide, VibrationType.Error));
            GlobalEventDispatcher.LockPickComboCorrect.AddListener((_, _, handSide) => Vibrate((HVRHandSide)handSide, VibrationType.Success));
            GlobalEventDispatcher.LockPickComboFinished.AddListener((_, _, handSide) => Vibrate((HVRHandSide)handSide, VibrationType.Success));
        }

        private void AttackVibration(NpcContainer combatant, VibrationType vibrationType)
        {
            // Resolve which hand holds the weapon for this combatant.
            // For NPCs attacking the player, we don't vibrate — only when it's the player's own attack.
            var handSide = _vrWeaponService.GetHandSideForCombatant(combatant);

            switch (handSide)
            {
                case GlobalEventDispatcher.HandSide.Both:
                    Vibrate(HVRHandSide.Left, vibrationType);
                    Vibrate(HVRHandSide.Right, vibrationType);
                    break;
                case GlobalEventDispatcher.HandSide.Left:
                    Vibrate(HVRHandSide.Left, vibrationType);
                    break;
                case GlobalEventDispatcher.HandSide.Right:
                    Vibrate(HVRHandSide.Right, vibrationType);
                    break;
            }
        }


        public void Vibrate(HVRHandSide handSide, VibrationType type)
        {
            _vrPlayerService.GetHand(handSide).Vibrate(_vibrationData[(int)type]);
        }
    }
}
#endif
