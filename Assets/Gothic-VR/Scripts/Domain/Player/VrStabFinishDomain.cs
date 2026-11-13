using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Melee = Gothic.VR.VRConstants.Melee;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// The "stabFinish" option (Immersion menu): the hero finishes off an
    /// unconscious NPC like a VR take on Gothic's t_1hSFinish - thrust the blade into the body (hand speed along the
    /// blade, in tracking space: VrSwingSampler.IsThrust).
    /// The stab plays the engine's FIG_SwordFinal; the blade kept in the body for the hold time kills -
    /// FightFinishingMove, like an NPC's AI_FinishingMove. Slashes don't finish anyone off then. Pulling the blade
    /// out lets go.
    /// </summary>
    public class VrStabFinishDomain
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly IVrSfxPlayer _vrSfxPlayer;

        private NpcContainer _attacker;
        private NpcContainer _target;
        private VrInteractable _weapon;
        private VrSwingSampler _swing;
        private IVrGrabber _hand;
        private Vector3 _stabPosition;
        private float _stabTime;
        private int _holdPulses;

        public bool IsStuck => _target != null;

        /// <summary>
        /// The blade entered an unconscious NPC. true = it was a stab (the blade sticks now). swing is the held
        /// weapon's sampler (hand speed and blade axis in tracking space).
        /// </summary>
        public bool TryStab(NpcContainer attacker, NpcContainer target, VrInteractable weapon, VrSwingSampler swing)
        {
            if (IsStuck || weapon == null || swing == null || !swing.IsReady)
                return false;

            _vrPlayerService.TryGetHoldingHand(weapon, out var hand);
            // IVrGrabber is a MonoBehaviour: Unity's null check (a destroyed hand), not the reference one.
            // Called every physics step while the blade is inside the hitbox - no log here.
            if (hand as UnityEngine.Object == null || !swing.IsThrust(Melee.StabThrustSpeed))
                return false;

            var alongBlade = Vector3.Dot(swing.HandVelocity, swing.BladeAxis);
            _attacker = attacker;
            _target = target;
            _weapon = weapon;
            _swing = swing;
            _hand = hand;
            _stabPosition = weapon.transform.position;
            _stabTime = Time.time;
            _holdPulses = 0;

            PlayStabSound(weapon.transform.position);
            GlobalEventDispatcher.StabStuck.Invoke((int)hand.HandSide);
            Logger.Log($"[VrStabFinish] Stab into {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                       $"({alongBlade:0.0} m/s) - hold it {Melee.StabHoldSeconds:0} s to finish", LogCat.Fight);
            return true;
        }

        /// <summary>
        /// Every frame while the blade sticks: kept in long enough kills.
        /// </summary>
        public void Tick()
        {
            if (!IsStuck)
                return;

            var maxStuck = Melee.StabHoldSeconds + 1f;
            if (_weapon == null || _hand as UnityEngine.Object == null || _hand.Held != _weapon ||
                !_swing.HasBlade || !_target.IsKnockedOut() || Time.time - _stabTime > maxStuck)
            {
                Logger.Log("[VrStabFinish] Blade let go - no finish", LogCat.Fight);
                Clear();
                return;
            }

            var weapon = _weapon.transform;
            var axis = _swing.BladeAxis;
            var moved = weapon.position - _stabPosition;
            var alongBlade = Vector3.Dot(moved, axis);
            if (-alongBlade > Melee.StabMaxPullBackDistance ||
                (moved - axis * alongBlade).magnitude > Melee.StabMaxSidewaysDistance)
            {
                Logger.Log("[VrStabFinish] Blade pulled out - no finish", LogCat.Fight);
                Clear();
                return;
            }

            var heldTime = Time.time - _stabTime;
            // A pulse every second - you feel the blade is in.
            if (heldTime >= (_holdPulses + 1) * Melee.StabHoldPulseSeconds && heldTime < Melee.StabHoldSeconds)
            {
                _holdPulses++;
                GlobalEventDispatcher.StabHeld.Invoke((int)_hand.HandSide);
            }
            if (heldTime < Melee.StabHoldSeconds)
                return;

            Finish($"held {heldTime:0.0} s");
        }

        private void Finish(string reason)
        {
            Logger.Log($"[VrStabFinish] {_attacker.Instance.GetName(NpcNameSlot.Slot0)} finishes off " +
                       $"{_target.Instance.GetName(NpcNameSlot.Slot0)} ({reason})", LogCat.Fight);
            GlobalEventDispatcher.StabFinished.Invoke((int)_hand.HandSide);
            var attacker = _attacker;
            var target = _target;
            Clear();
            GlobalEventDispatcher.FightFinishingMove.Invoke(attacker, target);
        }

        public void Clear()
        {
            _attacker = null;
            _target = null;
            _weapon = null;
            _swing = null;
            _hand = null;
        }

        private void PlayStabSound(Vector3 position)
        {
            var sfx = _vmCacheService.TryGetSfxData(Constants.SfxSwordFinal);
            var clip = sfx == null ? null : _audioService.CreateAudioClip(sfx.GetRandomSound());
            if (clip == null)
            {
                Logger.LogWarning($"[VrStabFinish] No sound {Constants.SfxSwordFinal}", LogCat.Fight);
                return;
            }
            _vrSfxPlayer.Play(clip, position);
        }
    }
}
