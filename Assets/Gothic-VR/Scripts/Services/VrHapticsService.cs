using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Decides which game event vibrates which hand with which preset. The vibration itself is done by IVrHaptics.
    /// </summary>
    public class VrHapticsService
    {
        // Contact haptics: impact speed (m/s) for the weakest and the strongest pulse.
        private const float _contactMinSpeed = 0.3f;
        private const float _contactMaxSpeed = 3f;
        private const float _contactMinAmplitude = 0.15f;

        // Body slots: a hand doesn't tick again this soon, e.g. at the item it just put in.
        private const float _bodySlotMinInterval = 0.4f;

        private readonly float[] _lastBodySlotTimes = { float.NegativeInfinity, float.NegativeInfinity };

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

            // Physical hands
            GlobalEventDispatcher.PhysicalHandContact.AddListener((handSide, speed) =>
                ContactVibration((VrHandSide)handSide, speed));

            // Body slots
            GlobalEventDispatcher.BodySlotReached.AddListener(handSide => BodySlotVibration((VrHandSide)handSide));
        }

        /// <summary>
        /// A hand reached a body slot it can use: the slots are behind the back or out of sight.
        /// </summary>
        private void BodySlotVibration(VrHandSide handSide)
        {
            var index = (int)handSide;
            if (Time.time - _lastBodySlotTimes[index] < _bodySlotMinInterval)
                return;

            _lastBodySlotTimes[index] = Time.time;
            _vrHaptics.Vibrate(handSide, VrHapticPreset.Touch);
        }

        /// <summary>
        /// A hand or its held item hit something: the faster, the stronger.
        /// </summary>
        private void ContactVibration(VrHandSide handSide, float speed)
        {
            var amplitude = Mathf.Lerp(_contactMinAmplitude, 1f,
                Mathf.InverseLerp(_contactMinSpeed, _contactMaxSpeed, speed));
            Logger.LogEditor($"[VrHapticsService] Contact {handSide}: {speed:0.00} m/s -> amplitude {amplitude:0.00}",
                LogCat.VR);
            _vrHaptics.Vibrate(handSide, VrHapticPreset.Contact, amplitude);
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
