#if GOTHIC_HVR_INSTALLED
using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Audio;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.VR.Models.Vob;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core.Utils;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using EventType = ZenKit.EventType;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Domain.Player
{
    public class VrWeaponAttackDomain
    {
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        private CharacterController _characterController => _vrPlayerService.VRContextInteractionService.GetVRPlayerController().CharacterController;

        private const int _twoHandedFlags = (int)VmGothicEnums.ItemFlags.Item2HdAxe | (int)VmGothicEnums.ItemFlags.Item2HdSwd;

        private bool _soundPlayed;
        private SfxModel _swingSwordSound;
        private float _soundPlayTime;

        public VobContainer WeaponVobContainer { get; private set; }
        private Rigidbody _weaponRigidbody;

        private bool _handlesLeftHand;
        private bool _handlesRightHand;
        private GlobalEventDispatcher.HandSide _handValue
        {
            get
            {
                if (_handlesLeftHand && _handlesRightHand)
                    return GlobalEventDispatcher.HandSide.Both;
                else if (_handlesLeftHand)
                    return GlobalEventDispatcher.HandSide.Left;
                else
                    return GlobalEventDispatcher.HandSide.Right;
            }
        }

        private float _attackVelocityThreshold;
        private float _velocityDropPercentage;

        // Velocity sampling properties
        private float _velocityCheckDuration;
        private int _velocitySampleCount;
        private readonly Queue<float> _velocityHistory = new();
        private float _velocityCheckTimer;

        // Current state properties
        private float _currentWeaponVelocity => GetAverageVelocity();

        // Internal tracking variables
        private bool _hasDroppedBelowThreshold;
        private bool _hasReturnedToThreshold;
        private float _velocityDropThreshold;

        private AttackWindowStateMachine _stateMachine;

        /// <summary>
        /// TRUE, when:
        ///   1. No weapon used by this handler so far
        ///   2. Same weapon is grabbed by another hand
        /// FALSE, when:
        ///   1. Already handling one weapon, but another one is provided.
        /// </summary>
        public bool TryHandle(VobContainer vobContainer, WeaponPhysicsConfig weaponConfig, HVRHandSide handSide, NpcContainer playerNpcContainer)
        {
            if (!CanHandle(vobContainer))
                return false;

            // First time grabbing this weapon.
            if (WeaponVobContainer == null)
            {
                WeaponVobContainer = vobContainer;
                _weaponRigidbody = vobContainer.Go.GetComponentInChildren<Rigidbody>();

                _attackVelocityThreshold = weaponConfig.WeaponVelocityThreshold;
                _velocityDropPercentage = weaponConfig.WeaponVelocityDropPercentage;
                var attackAnimation = GetAttackAnimation();
                var (attackWindowTime, comboWindowStart, comboWindowTime) = CalculateWindowTimes(attackAnimation);
                CalculateAttackSound(attackAnimation);

                _velocityCheckDuration = weaponConfig.VelocityCheckDuration;
                _velocitySampleCount = weaponConfig.VelocitySampleCount;

                _velocityDropThreshold = _attackVelocityThreshold * (1f - _velocityDropPercentage);

                _stateMachine = new AttackWindowStateMachine(playerNpcContainer, attackWindowTime, comboWindowStart, comboWindowTime);
            }

            if (handSide == HVRHandSide.Left)
                _handlesLeftHand = true;
            else if (handSide == HVRHandSide.Right)
                _handlesRightHand = true;

            AlterWeaponWeights(weaponConfig);

            return true;
        }

        private bool CanHandle(VobContainer newWeapon)
        {
            // No weapon handled so far. Free for the first one.
            if (WeaponVobContainer == null)
                return true;

            // The same weapon is trying to be grabbed by another hand.
            if (WeaponVobContainer == newWeapon)
                return true;

            // else
            return false;
        }

        public bool TryUnHandle(WeaponPhysicsConfig weaponConfig, HVRHandSide handSide)
        {
            if (!CanUnHandle(handSide))
                return false;

            if (handSide == HVRHandSide.Left)
                _handlesLeftHand = false;
            else if (handSide == HVRHandSide.Right)
                _handlesRightHand = false;

            // Alter weight now, before we potentially unset VobContainer and Rigidbody.
            AlterWeaponWeights(weaponConfig);

            if (!_handlesRightHand && !_handlesLeftHand)
                FullStopHandling();

            return true;
        }

        /// <summary>
        /// Unhandle means: Do we released item in hand which we handled?
        /// Hint: We can only have one item in a hand at a time. Therefore, we don't need to check for the rigidbody itself.
        /// </summary>
        private bool CanUnHandle(HVRHandSide releasedHandSide)
        {
            if (releasedHandSide == HVRHandSide.Left)
                return _handlesLeftHand;
            else
                return _handlesRightHand;
        }

        private void FullStopHandling()
        {
            // We need to execute it before we clear values.
            _stateMachine?.Reset();

            WeaponVobContainer = null;
            _weaponRigidbody = null;
            _handlesLeftHand = false;
            _handlesRightHand = false;
            _stateMachine = null;
        }

        /// <summary>
        /// Set the mass of weapons based on 1HD / 2HD types and the amount of hands holding it.
        /// </summary>
        private void AlterWeaponWeights(WeaponPhysicsConfig weaponConfig)
        {
            // We have one weapon in both hands
            if (_handlesLeftHand && _handlesRightHand)
            {
                _weaponRigidbody!.mass = weaponConfig.Mass1HAnyHand2HTwoHanded;
                _weaponRigidbody.linearDamping = weaponConfig.LinearDamping1HAnyHand2HTwoHanded;
                _weaponRigidbody.angularDamping = weaponConfig.AngularDamping1HAnyHand2HTwoHanded;
            }
            // We have the weapon in only one hand!
            else if (_handlesLeftHand ^ _handlesRightHand)
            {
                var is2HD = Is2HD();
                _weaponRigidbody!.mass = is2HD ? weaponConfig.Mass2HOneHanded : weaponConfig.Mass1HAnyHand2HTwoHanded;
                _weaponRigidbody.linearDamping = is2HD ? weaponConfig.LinearDamping2HOneHanded : weaponConfig.LinearDamping1HAnyHand2HTwoHanded;
                _weaponRigidbody.angularDamping = is2HD ? weaponConfig.AngularDamping2HOneHanded : weaponConfig.AngularDamping1HAnyHand2HTwoHanded;
            }
            // We don't have the weapon in any hand. Then reset it's weight values for world physics (e.g., gravity).
            else
            {
                _weaponRigidbody!.mass = weaponConfig.Mass1HAnyHand2HTwoHanded;
                _weaponRigidbody.linearDamping = weaponConfig.LinearDamping1HAnyHand2HTwoHanded;
                _weaponRigidbody.angularDamping = weaponConfig.AngularDamping1HAnyHand2HTwoHanded;
            }
        }

        private IAnimation GetAttackAnimation()
        {
            // Left-Right attacks always have a useful combo setting. (The combo for 2H forward (s_2hAttack) is broken to use with combos.)
            var attackAnimationName = $"t_{(Is2HD() ? "2" : "1")}hAttackL";

            // FIXME - Combo settings for hero with more skills are in overlay mds (e.g., HUMANS_1HST2.mds) Use for improved weapon handling.
            var mds = _resourceCacheService.TryGetModelScript("Humans")!;
            return mds.Animations.First(i => i.Name.EqualsIgnoreCase(attackAnimationName));
        }

        private bool Is2HD()
        {
            return (WeaponVobContainer.GetItemInstance().Flags & _twoHandedFlags) != 0;
        }

        /// <summary>
        /// After checking G1 animations (e.g., Humans.mds), we leverage the following data:
        /// 1. We assume our event calculations always start at frame 1.
        /// 2. DEF_OPT_FRAME is always from 1...x frame
        /// 3. DEF_WINDOW is for combo window and always goes between "x...y" frame.
        /// </summary>
        private (float attackWindowTime, float comboWindowStart, float comboWindowTime) CalculateWindowTimes(IAnimation attackAnim)
        {
            var eventLimb = attackAnim.EventTags.FirstOrDefault(i => i.Type == EventType.HitLimb);
            var eventLastHitFrame = attackAnim.EventTags.FirstOrDefault(i => i.Type == EventType.OptimalFrame);
            var eventHitWindow = attackAnim.EventTags.FirstOrDefault(i => i.Type == EventType.ComboWindow);

            if (eventLimb == null || eventLastHitFrame == null || eventHitWindow == null)
            {
                Logger.LogError(
                    $"Attack animation >{attackAnim.Name}< has missing at least one of the required events. Skipping fight for it.",
                    LogCat.VR);
                return (0f, 0f, 0f);
            }

            // Limb --> Check if collider is ZS_RIGHTHAND. If not --> Error log
            if (!eventLimb.Slots.Item1.EqualsIgnoreCase("ZS_RIGHTHAND"))
                Logger.LogError(
                    $"Collider check for weapon attack is not ZS_RIGHTHAND. Others aren't handled so far. Current: {eventLimb.Slots.Item1}",
                    LogCat.VR);

            // LastHitFrame --> Define attackWindowTime
            var attackWindowTime = eventLastHitFrame.Frames.First() / attackAnim.Fps;

            // HitWindow --> Define comboWindowTime
            var hitWindows = eventHitWindow.Frames;
            if (hitWindows.Count < 2)
            {
                Logger.LogError(
                    $"Animation >{attackAnim.Name}< need to provide at least two windows (start-end). Skipping...",
                    LogCat.VR);
                return (0f, 0f, 0f);
            }

            var comboWindowStart = hitWindows[0] / attackAnim.Fps;
            var comboWindowTime = (hitWindows[1] - hitWindows[0]) / attackAnim.Fps;

            return (attackWindowTime, comboWindowStart, comboWindowTime);
        }

        private void CalculateAttackSound(IAnimation attackAnim)
        {
            var soundAttack = attackAnim.SoundEffects.FirstOrDefault();

            if (soundAttack == null)
                return;

            _swingSwordSound = _vmCacheService.TryGetSfxData(soundAttack.Name);
            _soundPlayTime = soundAttack.Frame / attackAnim.Fps;
        }

        public void FixedUpdate()
        {
            if (_weaponRigidbody == null)
                return;

            UpdateVelocityHistory();
            UpdateVrAttackLogic();
        }

        public void AddLeftHand()
        {
            _handlesLeftHand = true;
        }

        public void AddRightHand()
        {
            _handlesRightHand = true;
        }

        public void RemoveLeftHand()
        {
            _handlesLeftHand = false;
        }

        public void RemoveRightHand()
        {
            _handlesRightHand = false;
        }

        /// <summary>
        /// Logic goes like this:
        /// To ignore some Controller tracking issues for a frame, we need to have a few samples of velocity before calculating the average.
        /// We also don't use every frame (a tracking issue could last for x-frames) but instead only a sample at each x-milliseconds.
        /// </summary>
        private void UpdateVelocityHistory()
        {
            _velocityCheckTimer += Time.fixedDeltaTime;

            if (_velocityCheckTimer < _velocityCheckDuration / _velocitySampleCount)
                return;

            // We need to subtract the current players movement. Otherwise a run will count as a swing.
            // TODO - Maybe we should subtract the V3 velocity instead of magnitude to countermeasure player movement?
            var currentVelocity = _weaponRigidbody.linearVelocity.magnitude - _characterController.velocity.magnitude;
            _velocityHistory.Enqueue(currentVelocity);

            // Keep only the required number of samples
            if (_velocityHistory.Count > _velocitySampleCount)
                _velocityHistory.Dequeue();

            _velocityCheckTimer = 0f;
        }

        private float GetAverageVelocity()
        {
            if (_velocityHistory.Count == 0)
                return 0f;

            var sum = _velocityHistory.Sum();
            return sum / _velocityHistory.Count;
        }

        /// <summary>
        /// VR-specific layer on top of the shared state machine.
        /// Handles velocity-driven transitions: initial->attack, combo detection via velocity drop/return, combo failure.
        /// </summary>
        private void UpdateVrAttackLogic()
        {
            if (_stateMachine == null)
                return;

            switch (_stateMachine.CurrentState)
            {
                case AttackWindowStateMachine.State.Initial:
                    HandleInitialWindow();
                    break;
                case AttackWindowStateMachine.State.Attack:
                    HandleSound();
                    if (!CheckIfComboWindowFailed())
                        _stateMachine.Tick(Time.fixedDeltaTime);
                    break;
                case AttackWindowStateMachine.State.ComboFailed:
                    // Simply wait until the whole "animation" is over and then start again.
                    _stateMachine.Tick(Time.fixedDeltaTime);
                    break;
                case AttackWindowStateMachine.State.WaitingForCombo:
                    HandleSound();
                    if (!CheckIfComboWindowFailed())
                        _stateMachine.Tick(Time.fixedDeltaTime);
                    break;
                case AttackWindowStateMachine.State.Combo:
                    HandleSound();
                    HandleComboWindow();
                    break;
            }
        }

        private void HandleSound()
        {
            if (_soundPlayed || _stateMachine.ElapsedTime <= _soundPlayTime)
                return;

            _soundPlayed = true;
            SFXPlayer.Instance.PlaySFX(_audioService.CreateAudioClip(_swingSwordSound.GetRandomSound()), _weaponRigidbody.position);
        }

        private void HandleInitialWindow()
        {
            if (_currentWeaponVelocity >= _attackVelocityThreshold)
                StartAttack();
        }

        private void StartAttack()
        {
            _hasDroppedBelowThreshold = false;
            _hasReturnedToThreshold = false;

            // If no sound is set, ignore playing it and mark it as "played".
            _soundPlayed = _swingSwordSound == null;

            _stateMachine.StartAttack();
        }

        private bool CheckIfComboWindowFailed()
        {
            // Track velocity drops and returns
            if (_currentWeaponVelocity < _velocityDropThreshold && !_hasDroppedBelowThreshold)
                _hasDroppedBelowThreshold = true;

            if (_hasDroppedBelowThreshold && _currentWeaponVelocity >= _attackVelocityThreshold && !_hasReturnedToThreshold)
            {
                _hasReturnedToThreshold = true;
                // Combo failed - velocity dropped and returned during attack window
                _stateMachine.FailCombo();
                return true;
            }

            return false;
        }

        private void HandleComboWindow()
        {
            // Check for combo conditions

            // Check #1 - When we enter the ComboWindow or we're inside already, we need to have the sword at least dropping below threshold once.
            // Basically fighter changes velocity direction to do a left-right swing.
            if (!_hasDroppedBelowThreshold && _currentWeaponVelocity < _velocityDropThreshold)
                _hasDroppedBelowThreshold = true;

            if (_hasDroppedBelowThreshold && _currentWeaponVelocity >= _attackVelocityThreshold)
                _hasReturnedToThreshold = true;

            // It means e.g., we changed directions and got up to speed within combo window time. Now let's start the combo immediately.
            if (_hasReturnedToThreshold)
            {
                StartAttack();
                return;
            }

            // Let state machine check if combo window time is up
            _stateMachine.Tick(Time.fixedDeltaTime);
        }

        // Public methods for external systems
        public bool IsInAttackState()
        {
            return _stateMachine?.IsInAttackWindow ?? false;
        }

        public bool IsInComboState()
        {
            return _stateMachine?.IsInComboWindow ?? false;
        }

        public bool IsFailedComboState()
        {
            return _stateMachine?.IsComboFailed ?? false;
        }

        public float GetRemainingStateTime()
        {
            return _stateMachine?.ElapsedTime ?? 0f;
        }

        public float GetVelocityThreshold()
        {
            return _attackVelocityThreshold;
        }

        public float GetVelocityDropThreshold()
        {
            return _velocityDropThreshold;
        }

        public void AdvanceStateAfterAttack()
        {
            _stateMachine?.AdvanceToWaitingForCombo();
        }

        public NpcContainer GetOwner()
        {
            return _stateMachine?.Owner;
        }

        public GlobalEventDispatcher.HandSide GetHandSide()
        {
            return _handValue;
        }

        // FIXME - DEBUG values. Need to be adjustable via MarvinMode Inspector...
        private float _amplitude = 0.2f;
        private float _duration = 0.5f;
        private float _frequency = 50f;

        public Collider[] CheckBoxColliderOverlap(BoxCollider boxCollider)
        {
            CalculateBoxColliderOverlap(boxCollider, out var center, out var size, out var rotation);

            var colliders = Physics.OverlapBox(center, size / 2, rotation, 1 << Constants.VobNpcOrMonsterLayer);

            return colliders;
        }

        public void CalculateBoxColliderOverlap(BoxCollider boxCollider, out Vector3 center, out Vector3 size,
            out Quaternion rotation)
        {
            var bounds = boxCollider.bounds;
            center = bounds.center;
            size = bounds.size;
            rotation = boxCollider.transform.rotation;
        }

        public Collider[] CheckCapsuleColliderOverlap(CapsuleCollider capsuleCollider)
        {
            CalculateCapsuleOverlap(capsuleCollider, out var point0, out var point1, out var radius);

            var colliders = Physics.OverlapCapsule(point0, point1, radius, 1 << Constants.VobNpcOrMonsterLayer);

            return colliders;
        }

        public void CalculateCapsuleOverlap(CapsuleCollider capsuleCollider, out Vector3 point0, out Vector3 point1,
            out float radius)
        {
            // Calculate capsule radius
            var bounds = capsuleCollider.bounds;
            var center = bounds.center;
            radius = capsuleCollider.radius * Mathf.Max(capsuleCollider.transform.lossyScale.x, capsuleCollider.transform.lossyScale.z);

            // Calculate capsule endpoints
            var height = capsuleCollider.height * capsuleCollider.transform.lossyScale.y;
            var direction = capsuleCollider.direction switch
            {
                0 => capsuleCollider.transform.right,
                1 => capsuleCollider.transform.up,
                2 => capsuleCollider.transform.forward,
                _ => throw new ArgumentOutOfRangeException()
            };

            var halfHeight = (height / 2) - radius;
            point0 = center + direction * halfHeight;
            point1 = center - direction * halfHeight;
        }
    }
}
#endif
