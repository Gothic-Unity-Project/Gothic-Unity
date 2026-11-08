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
    /// unconscious NPC like a VR take on Gothic's t_1hSFinish - thrust the blade into the body (speed along the blade).
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

        // Hand speed (m/s) along the blade for a stab, and the share of the whole speed that has to go along it.
        // A slash whose blade happened to point along its way counted as a stab - the share is high now.
        // The blade in the body this long kills.
        // Meters back out of the body (against the tip) / sideways that let go.

        private NpcContainer _attacker;
        private NpcContainer _target;
        private VrInteractable _weapon;
        private Rigidbody _rigidbody;
        private IVrGrabber _hand;
        private Vector3 _stabPosition;
        private float _stabTime;
        private int _holdPulses;

        // The weapon's Rigidbody and mesh, looked up once per weapon (TryStab runs every physics step in a hitbox).
        private VrInteractable _cachedWeapon;
        private Rigidbody _cachedRigidbody;
        private MeshFilter _cachedMeshFilter;

        public bool IsStuck => _target != null;

        /// <summary>
        /// The blade entered an unconscious NPC. true = it was a stab (the blade sticks now).
        /// </summary>
        public bool TryStab(NpcContainer attacker, NpcContainer target, VrInteractable weapon, Vector3 playerVelocity)
        {
            if (IsStuck)
                return false;

            _vrPlayerService.TryGetHoldingHand(weapon, out var hand);
            CacheWeapon(weapon);
            var rigidbody = _cachedRigidbody;
            var handPosition = GetHandPosition(hand);
            if (hand == null || rigidbody == null || handPosition == null)
                return false;

            var tipDirection = GetTipDirection(weapon.transform, _cachedMeshFilter, handPosition.Value);
            var velocity = rigidbody.linearVelocity - playerVelocity;
            var alongBlade = Vector3.Dot(velocity, tipDirection);
            // Called every physics step while the blade is inside the hitbox - no log here.
            if (alongBlade < Melee.StabThrustSpeed || alongBlade < velocity.magnitude * Melee.StabThrustAlignment)
                return false;

            _attacker = attacker;
            _target = target;
            _weapon = weapon;
            _rigidbody = rigidbody;
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

            var handPosition = GetHandPosition(_hand);
            var maxStuck = Melee.StabHoldSeconds + 1f;
            if (_weapon == null || _rigidbody == null || _hand == null || _hand.Held != _weapon ||
                handPosition == null || !_target.IsKnockedOut() || Time.time - _stabTime > maxStuck)
            {
                Logger.Log("[VrStabFinish] Blade let go - no finish", LogCat.Fight);
                Clear();
                return;
            }

            var weapon = _weapon.transform;
            var axis = GetTipDirection(weapon, _cachedMeshFilter, handPosition.Value);
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
            _rigidbody = null;
            _hand = null;
        }

        private void CacheWeapon(VrInteractable weapon)
        {
            if (weapon == _cachedWeapon)
                return;

            _cachedWeapon = weapon;
            _cachedRigidbody = weapon != null ? weapon.GetComponent<Rigidbody>() : null;
            _cachedMeshFilter = weapon != null ? weapon.GetComponentInChildren<MeshFilter>() : null;
        }

        private Vector3? GetHandPosition(IVrGrabber hand)
        {
            var handGo = hand != null ? _vrPlayerService.GetHandModelGo(hand.HandSide) : null;
            return handGo != null ? handGo.transform.position : null;
        }

        /// <summary>
        /// Gothic weapon meshes lie along their local X axis (WeaponAdapter: bounds.size.x is the length) - the tip is
        /// the end away from the hand.
        /// </summary>
        private static Vector3 GetTipDirection(Transform weapon, MeshFilter meshFilter, Vector3 handPosition)
        {
            var axis = weapon.right;
            var center = meshFilter != null && meshFilter.sharedMesh != null
                ? meshFilter.transform.TransformPoint(meshFilter.sharedMesh.bounds.center)
                : weapon.position;
            return Vector3.Dot(center - handPosition, axis) >= 0f ? axis : -axis;
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
