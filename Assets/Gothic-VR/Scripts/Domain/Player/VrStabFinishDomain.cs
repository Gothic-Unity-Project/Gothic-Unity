#if GOTHIC_HVR_INSTALLED
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Utils;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrStabFinish + the "stabFinish" option (Immersion menu): the hero finishes off an
    /// unconscious NPC like a VR take on Gothic's t_1hSFinish - thrust the blade into the body (speed along the blade)
    /// and twist the wrist (like the lock picks, 45 degrees). The stab plays the engine's FIG_SwordFinal, the twist
    /// kills (FightFinishingMove, like an NPC's AI_FinishingMove). Slashes don't finish anyone off then.
    /// Pulling the blade out or waiting too long lets go.
    /// </summary>
    public class VrStabFinishDomain
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VrHapticsService _hapticsService;

        // Hand speed (m/s) along the blade for a stab, and the share of the whole speed that has to go along it.
        // A slash whose blade happened to point along its way counted as a stab - the share is high now.
        private const float _thrustSpeed = 1.2f;
        private const float _thrustAlignment = 0.85f;
        private const float _twistAngle = 45f;
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
        private Transform _weapon;
        private Rigidbody _rigidbody;
        private HVRHandGrabber _hand;
        private Vector3 _stabPosition;
        private Vector3 _startUp;
        private float _stabTime;

        public bool IsStuck => _target != null;

        /// <summary>
        /// The blade entered an unconscious NPC. true = it was a stab (the blade sticks now).
        /// </summary>
        public bool TryStab(NpcContainer attacker, NpcContainer target, HVRGrabbable weapon, Vector3 playerVelocity)
        {
            if (IsStuck || weapon == null || weapon.HandGrabbers.Count == 0 || weapon.Rigidbody == null)
                return false;

            var hand = weapon.HandGrabbers[0];
            var tipDirection = GetTipDirection(weapon.transform, hand.transform.position);
            var velocity = weapon.Rigidbody.linearVelocity - playerVelocity;
            var alongBlade = Vector3.Dot(velocity, tipDirection);
            // Called every physics step while the blade is inside the hitbox - no log here.
            if (alongBlade < _thrustSpeed || alongBlade < velocity.magnitude * _thrustAlignment)
                return false;

            _attacker = attacker;
            _target = target;
            _weapon = weapon.transform;
            _rigidbody = weapon.Rigidbody;
            _hand = hand;
            _stabPosition = _weapon.position;
            _startUp = _weapon.up;
            _stabTime = Time.time;

            PlayStabSound(_weapon.position);
            _hapticsService.Vibrate(hand.HandSide, VrHapticsService.VibrationType.Info);
            Logger.Log($"[VrStabFinish] Stab into {target.Instance.GetName(NpcNameSlot.Slot0)} " +
                       $"({alongBlade:0.0} m/s) - twist to finish", LogCat.Fight);
            return true;
        }

        /// <summary>
        /// Every frame while the blade sticks: the twist around the blade kills.
        /// </summary>
        public void Tick(Vector3 playerVelocity)
        {
            if (!IsStuck)
                return;

            if (_weapon == null || _rigidbody == null || _hand == null || _hand.GrabbedTarget == null ||
                _hand.GrabbedTarget.transform != _weapon || !NpcHelperService.IsKnockedOut(_target) ||
                Time.time - _stabTime > _maxStuckSeconds)
            {
                Logger.Log("[VrStabFinish] Blade let go - no finish", LogCat.Fight);
                Clear();
                return;
            }

            var axis = GetTipDirection(_weapon, _hand.transform.position);
            var moved = _weapon.position - _stabPosition;
            var alongBlade = Vector3.Dot(moved, axis);
            if (-alongBlade > _maxPullBackDistance || (moved - axis * alongBlade).magnitude > _maxSidewaysDistance)
            {
                Logger.Log("[VrStabFinish] Blade pulled out - no finish", LogCat.Fight);
                Clear();
                return;
            }

            if ((_rigidbody.linearVelocity - playerVelocity).magnitude > _maxTwistMoveSpeed)
            {
                _startUp = _weapon.up;
                return;
            }

            var twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(_startUp, axis),
                Vector3.ProjectOnPlane(_weapon.up, axis), axis);
            if (Mathf.Abs(twist) < _twistAngle)
                return;

            Logger.Log($"[VrStabFinish] {_attacker.Instance.GetName(NpcNameSlot.Slot0)} finishes off " +
                       $"{_target.Instance.GetName(NpcNameSlot.Slot0)} (twist {twist:0})", LogCat.Fight);
            _hapticsService.Vibrate(_hand.HandSide, VrHapticsService.VibrationType.Success);
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
            SFXPlayer.Instance.PlaySFX(clip, position);
        }
    }
}
#endif
