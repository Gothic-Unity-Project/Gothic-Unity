using System;
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
    /// Lives in the VR scene scope: Reflex disposes it with the scene, which removes the listeners from the static
    /// events. Otherwise a reloaded VR scene would vibrate twice.
    /// </summary>
    public class VrHapticsService : IDisposable
    {
        // Contact haptics: impact speed (m/s) for the weakest and the strongest pulse.
        private const float _contactMinSpeed = 0.3f;
        private const float _contactMaxSpeed = 3f;
        private const float _contactMinAmplitude = 0.15f;

        // Body slots: a hand doesn't tick again this soon, e.g. at the item it just put in.
        private const float _bodySlotMinInterval = 0.4f;

        // Ledge climbing: the tick when a hand comes within touch range of a lip.
        private const float _ledgeTouchAmplitude = 0.3f;

        private readonly float[] _lastBodySlotTimes = { float.NegativeInfinity, float.NegativeInfinity };

        [Inject] private readonly IVrHaptics _vrHaptics;
        [Inject] private readonly VRWeaponService _vrWeaponService;

        public VrHapticsService()
        {
            // Fight
            GlobalEventDispatcher.FightWindowAttack.AddListener(OnFightWindowAttack);
            GlobalEventDispatcher.FightWindowCombo.AddListener(OnFightWindowCombo);
            GlobalEventDispatcher.FightWindowComboFailed.AddListener(OnFightWindowComboFailed);

            // Lock picking
            GlobalEventDispatcher.LockPickComboWrong.AddListener(OnLockPickComboWrong);
            GlobalEventDispatcher.LockPickComboBroken.AddListener(OnLockPickComboBroken);
            GlobalEventDispatcher.LockPickComboCorrect.AddListener(OnLockPickComboSucceeded);
            GlobalEventDispatcher.LockPickComboFinished.AddListener(OnLockPickComboSucceeded);

            // Physical hands
            GlobalEventDispatcher.PhysicalHandContact.AddListener(OnPhysicalHandContact);

            // Body slots
            GlobalEventDispatcher.BodySlotReached.AddListener(OnBodySlotReached);

            // Ledge climbing
            GlobalEventDispatcher.LedgeTouched.AddListener(OnLedgeTouched);
            GlobalEventDispatcher.LedgeClimbRefused.AddListener(OnLedgeClimbRefused);
        }

        public void Dispose()
        {
            GlobalEventDispatcher.FightWindowAttack.RemoveListener(OnFightWindowAttack);
            GlobalEventDispatcher.FightWindowCombo.RemoveListener(OnFightWindowCombo);
            GlobalEventDispatcher.FightWindowComboFailed.RemoveListener(OnFightWindowComboFailed);

            GlobalEventDispatcher.LockPickComboWrong.RemoveListener(OnLockPickComboWrong);
            GlobalEventDispatcher.LockPickComboBroken.RemoveListener(OnLockPickComboBroken);
            GlobalEventDispatcher.LockPickComboCorrect.RemoveListener(OnLockPickComboSucceeded);
            GlobalEventDispatcher.LockPickComboFinished.RemoveListener(OnLockPickComboSucceeded);

            GlobalEventDispatcher.PhysicalHandContact.RemoveListener(OnPhysicalHandContact);

            GlobalEventDispatcher.BodySlotReached.RemoveListener(OnBodySlotReached);

            GlobalEventDispatcher.LedgeTouched.RemoveListener(OnLedgeTouched);
            GlobalEventDispatcher.LedgeClimbRefused.RemoveListener(OnLedgeClimbRefused);
        }

        // Attack can happen now
        private void OnFightWindowAttack(NpcContainer combatant) => AttackVibration(combatant, VrHapticPreset.Success);

        // Now you can start another attack
        private void OnFightWindowCombo(NpcContainer combatant) => AttackVibration(combatant, VrHapticPreset.Info);

        // Attack window passed
        private void OnFightWindowComboFailed(NpcContainer combatant) =>
            AttackVibration(combatant, VrHapticPreset.Error);

        private void OnLockPickComboWrong(VobContainer lockPick, VobContainer target, int handSide) =>
            Vibrate((VrHandSide)handSide, VrHapticPreset.Warning);

        private void OnLockPickComboBroken(VobContainer lockPick, VobContainer target, int handSide) =>
            Vibrate((VrHandSide)handSide, VrHapticPreset.Error);

        private void OnLockPickComboSucceeded(VobContainer lockPick, VobContainer target, int handSide) =>
            Vibrate((VrHandSide)handSide, VrHapticPreset.Success);

        private void OnPhysicalHandContact(int handSide, float speed) => ContactVibration((VrHandSide)handSide, speed);

        private void OnBodySlotReached(int handSide) => BodySlotVibration((VrHandSide)handSide);

        private void OnLedgeTouched(int handSide) =>
            Vibrate((VrHandSide)handSide, VrHapticPreset.Contact, _ledgeTouchAmplitude);

        private void OnLedgeClimbRefused(int handSide) => Vibrate((VrHandSide)handSide, VrHapticPreset.Error);

        /// <summary>
        /// A hand reached a body slot it can use: the slots are behind the back or out of sight.
        /// </summary>
        private void BodySlotVibration(VrHandSide handSide)
        {
            var index = (int)handSide;
            if (Time.time - _lastBodySlotTimes[index] < _bodySlotMinInterval)
                return;

            _lastBodySlotTimes[index] = Time.time;
            Vibrate(handSide, VrHapticPreset.Touch);
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
            Vibrate(handSide, VrHapticPreset.Contact, amplitude);
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

        public void Vibrate(VrHandSide handSide, VrHapticPreset preset, float amplitudeScale)
        {
            _vrHaptics.Vibrate(handSide, preset, amplitudeScale);
        }
    }
}
