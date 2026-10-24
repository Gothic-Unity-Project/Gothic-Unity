using Gothic.Core;
using Gothic.Core.Models.Container;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Decides which game event vibrates which hand with which preset. The vibration itself is done by IVrHaptics.
    /// </summary>
    public class VrHapticsService
    {
        [Inject] private readonly IVrHaptics _vrHaptics;
        [Inject] private readonly VRWeaponService _vrWeaponService;

        public VrHapticsService()
        {
            // Fight
            GlobalEventDispatcher.FightWindowAttack.AddListener(combatant =>
                AttackVibration(combatant, VrHapticPreset.Success)); // Attack can happen now
            GlobalEventDispatcher.FightWindowCombo.AddListener(combatant =>
                AttackVibration(combatant, VrHapticPreset.Info)); // Now you can start another attack
            GlobalEventDispatcher.FightWindowComboFailed.AddListener(combatant =>
                AttackVibration(combatant, VrHapticPreset.Error)); // Attack window passed

            // Lock picking
            GlobalEventDispatcher.LockPickComboWrong.AddListener((_, _, handSide) =>
                Vibrate((VrHandSide)handSide, VrHapticPreset.Warning));
            GlobalEventDispatcher.LockPickComboBroken.AddListener((_, _, handSide) =>
                Vibrate((VrHandSide)handSide, VrHapticPreset.Error));
            GlobalEventDispatcher.LockPickComboCorrect.AddListener((_, _, handSide) =>
                Vibrate((VrHandSide)handSide, VrHapticPreset.Success));
            GlobalEventDispatcher.LockPickComboFinished.AddListener((_, _, handSide) =>
                Vibrate((VrHandSide)handSide, VrHapticPreset.Success));
        }

        private void AttackVibration(NpcContainer combatant, VrHapticPreset preset)
        {
            // Resolve which hand holds the weapon for this combatant.
            // For NPCs attacking the player, we don't vibrate — only when it's the player's own attack.
            var handSide = _vrWeaponService.GetHandSideForCombatant(combatant);

            if (handSide == GlobalEventDispatcher.HandSide.Both)
            {
                Vibrate(VrHandSide.Left, preset);
                Vibrate(VrHandSide.Right, preset);
            }
            else if (handSide.TryToVrHandSide(out var vrHandSide))
            {
                Vibrate(vrHandSide, preset);
            }
        }

        public void Vibrate(VrHandSide handSide, VrHapticPreset preset)
        {
            _vrHaptics.Vibrate(handSide, preset);
        }
    }
}
