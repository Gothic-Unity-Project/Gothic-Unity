using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrStabFinish + the "stabFinish" option (Immersion menu): the hero finishes off an
    /// unconscious NPC like a VR take on Gothic's t_1hSFinish - thrust the blade into the body (speed along the blade).
    /// The stab plays the engine's FIG_SwordFinal; the blade kept in the body for _holdSeconds kills
    /// (DeveloperConfig.EnableStabFinishHold; off: a 45 degree twist of the wrist, like the lock picks) - FightFinishingMove,
    /// like an NPC's AI_FinishingMove. Slashes don't finish anyone off then. Pulling the blade out lets go.
    /// </summary>
    public class VrStabFinishDomain
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly IVrSfxPlayer _vrSfxPlayer;
        [Inject] private readonly ConfigService _configService;

        // Hand speed (m/s) along the blade for a stab, and the share of the whole speed that has to go along it.
        // A slash whose blade happened to point along its way counted as a stab - the share is high now.
        private const float _thrustSpeed = 1.2f;
        private const float _thrustAlignment = 0.85f;
        private const float _twistAngle = 45f;
        // DeveloperConfig.EnableStabFinishHold: the blade in the body this long kills.
        private const float _holdSeconds = 3f;
        private const float _holdPulseSeconds = 1f;
        // Meters back out of the body (against the tip) / sideways that let go.
        private const float _maxPullBackDistance = 0.15f;
        private const float _maxSidewaysDistance = 0.25f;
        // The twist only counts while the blade rests in the body: faster (m/s) and the wrist angle starts over -
        // a slash after the stab turned the blade 45 degrees and killed.
        private const float _maxTwistMoveSpeed = 0.6f;
        private const float _maxStuckSeconds = 4f;
        private const string _stabSound = "FIG_SWORDFINAL";

        private NpcContainer _attacker;
        private NpcContainer _target;
        private VrInteractable _weapon;
        private Rigidbody _rigidbody;
        private IVrGrabber _hand;
        private Vector3 _stabPosition;
        private Vector3 _startUp;
        private float _stabTime;
        private int _holdPulses;

        public bool IsStuck => _target != null;

        private bool IsHoldMode => _configService.Dev.EnableStabFinishHold;

        /// <summary>
        /// The blade entered an unconscious NPC. true = it was a stab (the blade sticks now).
        /// </summary>
        public bool TryStab(NpcContainer attacker, NpcContainer target, VrInteractable weapon, Vector3 playerVelocity)
        {
            var hand = GetHoldingHand(weapon);
            var rigidbody = weapon != null ? weapon.GetComponent<Rigidbody>() : null;
            var handPosition = GetHandPosition(hand);
            if (IsStuck || hand == null || rigidbody == null || handPosition == null)
                return false;

            var tipDirection = GetTipDirection(weapon.transform, handPosition.Value);
            var velocity = rigidbody.linearVelocity - playerVelocity;
            var alongBlade = Vector3.Dot(velocity, tipDirection);
            // Called every physics step while the blade is inside the hitbox - no log here.
            if (alongBlade < _thrustSpeed || alongBlade < velocity.magnitude * _thrustAlignment)
                return false;

            _attacker = attacker;
            _target = target;
            _weapon = weapon;
            _rigidbody = rigidbody;
            _hand = hand;
            _stabPosition = weapon.transform.position;
            _startUp = weapon.transform.up;
            _stabTime = Time.time;
            _holdPulses = 0;

            PlayStabSound(weapon.transform.position);
            _hapticsService.Vibrate(hand.HandSide, VrHapticPreset.Info);
            Logger.Log($"[VrStabFinish] Stab into {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                       $"({alongBlade:0.0} m/s) - " + (IsHoldMode ? $"hold it {_holdSeconds:0} s" : "twist") +
                       " to finish", LogCat.Fight);
            return true;
        }

        /// <summary>
        /// Every frame while the blade sticks: kept in long enough (or the twist around the blade) kills.
        /// </summary>
        public void Tick(Vector3 playerVelocity)
        {
            if (!IsStuck)
                return;

            var handPosition = GetHandPosition(_hand);
            var maxStuck = IsHoldMode ? _holdSeconds + 1f : _maxStuckSeconds;
            if (_weapon == null || _rigidbody == null || _hand == null || _hand.Held != _weapon ||
                handPosition == null || !NpcHelperService.IsKnockedOut(_target) || Time.time - _stabTime > maxStuck)
            {
                Logger.Log("[VrStabFinish] Blade let go - no finish", LogCat.Fight);
                Clear();
                return;
            }

            var weapon = _weapon.transform;
            var axis = GetTipDirection(weapon, handPosition.Value);
            var moved = weapon.position - _stabPosition;
            var alongBlade = Vector3.Dot(moved, axis);
            if (-alongBlade > _maxPullBackDistance || (moved - axis * alongBlade).magnitude > _maxSidewaysDistance)
            {
                Logger.Log("[VrStabFinish] Blade pulled out - no finish", LogCat.Fight);
                Clear();
                return;
            }

            if (IsHoldMode)
            {
                var heldTime = Time.time - _stabTime;
                // A pulse every second - you feel the blade is in.
                if (heldTime >= (_holdPulses + 1) * _holdPulseSeconds && heldTime < _holdSeconds)
                {
                    _holdPulses++;
                    _hapticsService.Vibrate(_hand.HandSide, VrHapticPreset.Info);
                }
                if (heldTime < _holdSeconds)
                    return;

                Finish($"held {heldTime:0.0} s");
                return;
            }

            if ((_rigidbody.linearVelocity - playerVelocity).magnitude > _maxTwistMoveSpeed)
            {
                _startUp = weapon.up;
                return;
            }

            var twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(_startUp, axis),
                Vector3.ProjectOnPlane(weapon.up, axis), axis);
            if (Mathf.Abs(twist) < _twistAngle)
                return;

            Finish($"twist {twist:0}");
        }

        private void Finish(string reason)
        {
            Logger.Log($"[VrStabFinish] {_attacker.Instance.GetName(NpcNameSlot.Slot0)} finishes off " +
                       $"{_target.Instance.GetName(NpcNameSlot.Slot0)} ({reason})", LogCat.Fight);
            _hapticsService.Vibrate(_hand.HandSide, VrHapticPreset.Success);
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

        private IVrGrabber GetHoldingHand(VrInteractable weapon)
        {
            var rig = _vrPlayerService.VRContextInteractionService?.Rig;
            if (weapon == null || rig == null)
                return null;

            foreach (var hand in rig.HandGrabbers)
            {
                if (hand.Kind == VrGrabberKind.Hand && hand.Held == weapon)
                    return hand;
            }
            return null;
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
        private static Vector3 GetTipDirection(Transform weapon, Vector3 handPosition)
        {
            var axis = weapon.right;
            var meshFilter = weapon.GetComponentInChildren<MeshFilter>();
            var center = meshFilter != null && meshFilter.sharedMesh != null
                ? meshFilter.transform.TransformPoint(meshFilter.sharedMesh.bounds.center)
                : weapon.position;
            return Vector3.Dot(center - handPosition, axis) >= 0f ? axis : -axis;
        }

        private void PlayStabSound(Vector3 position)
        {
            var sfx = _vmCacheService.TryGetSfxData(_stabSound);
            var clip = sfx == null ? null : _audioService.CreateAudioClip(sfx.GetRandomSound());
            if (clip == null)
            {
                Logger.LogWarning($"[VrStabFinish] No sound {_stabSound}", LogCat.Fight);
                return;
            }
            _vrSfxPlayer.Play(clip, position);
        }
    }
}
