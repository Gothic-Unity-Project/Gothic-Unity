using System;
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
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
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
        [Inject] private readonly IVrSfxPlayer _vrSfxPlayer;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly IVrInput _vrInput;

        private const int _twoHandedFlags = (int)VmGothicEnums.ItemFlags.Item2HdAxe | (int)VmGothicEnums.ItemFlags.Item2HdSwd;

        private bool _soundPlayed;
        private SfxModel _swingSwordSound;
        private float _soundPlayTime;

        public VobContainer WeaponVobContainer { get; private set; }

        /// <summary>
        /// The held weapon's movement (hand and tip speeds, blade axis). Read by the stab finish.
        /// </summary>
        public VrSwingSampler Swing => _swing;
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

        // The swing, measured in the rig's tracking space. Its tip speed drives the combo (drop and return).
        private readonly VrSwingSampler _swing = new();
        private float _attackVelocityThreshold;

        // Internal tracking variables
        private bool _hasDroppedBelowThreshold;
        private bool _hasReturnedToThreshold;
        private float _velocityDropThreshold;

        private AttackWindowStateMachine _stateMachine;

        // Vanilla-like timings (seconds), used when a mod's Humans.mds provides no usable attack events.
        private static readonly (float attackWindowTime, float comboWindowStart, float comboWindowTime)
            _fallbackWindowTimes = (0.4f, 0.5f, 0.4f);

        /// <summary>
        /// TRUE, when:
        ///   1. No weapon used by this handler so far
        ///   2. Same weapon is grabbed by another hand
        /// FALSE, when:
        ///   1. Already handling one weapon, but another one is provided.
        /// </summary>
        public bool TryHandle(VobContainer vobContainer, WeaponPhysicsConfig weaponConfig, VrHandSide handSide, NpcContainer playerNpcContainer)
        {
            if (!CanHandle(vobContainer))
                return false;

            // First time grabbing this weapon.
            if (WeaponVobContainer == null)
            {
                // A mod's Humans.mds can lack the attack animation or its events. Default timings are used then,
                // so the handler is always fully set up (a null state machine made swinging do nothing).
                WeaponVobContainer = vobContainer;
                var attackAnimation = GetAttackAnimation();
                var windowTimes = _fallbackWindowTimes;

                if (attackAnimation == null)
                {
                    // Mods (or a broken Humans.mds parse) can lack the animation. Attacking must still work.
                    Logger.LogError("Humans.mds attack animation not found. Using default attack window timings.",
                        LogCat.VR);
                    _swingSwordSound = null;
                }
                else
                {
                    var calculated = CalculateWindowTimes(attackAnimation);
                    if (calculated.attackWindowTime > 0f)
                        windowTimes = calculated;
                    else
                        Logger.LogError($"Attack animation >{attackAnimation.Name}< has unusable events. Using " +
                                        "default attack window timings.",
                            LogCat.VR);

                    CalculateAttackSound(attackAnimation);
                }

                _weaponRigidbody = vobContainer.Go.GetComponentInChildren<Rigidbody>();

                var thresholds = GetSwingThresholds(weaponConfig);
                _attackVelocityThreshold = thresholds.TipSpeed;
                _velocityDropThreshold = _attackVelocityThreshold * (1f - weaponConfig.WeaponVelocityDropPercentage);
                StartSwingSampling(handSide, thresholds);

                _stateMachine = new AttackWindowStateMachine(playerNpcContainer, windowTimes.attackWindowTime,
                    windowTimes.comboWindowStart, windowTimes.comboWindowTime);
            }

            if (handSide == VrHandSide.Left)
                _handlesLeftHand = true;
            else if (handSide == VrHandSide.Right)
                _handlesRightHand = true;

            _swing.SetHolding(_handlesLeftHand, _handlesRightHand);
            AlterWeaponWeights(weaponConfig);

            return true;
        }

        private VrSwingThresholds GetSwingThresholds(WeaponPhysicsConfig weaponConfig)
        {
            var is2HD = Is2HD();
            return new VrSwingThresholds
            {
                HandleSpeed = is2HD ? weaponConfig.HandleSpeed2H : weaponConfig.HandleSpeed1H,
                TipSpeed = is2HD ? weaponConfig.TipSpeed2H : weaponConfig.TipSpeed1H,
                ThrustSpeed = weaponConfig.ThrustSpeed,
                MinTimeOver = weaponConfig.MinTimeOver,
                MinTipPath = weaponConfig.MinTipPath,
                PathWindow = weaponConfig.PathWindow
            };
        }

        private void StartSwingSampling(VrHandSide handSide, VrSwingThresholds thresholds)
        {
            var rig = _vrPlayerService.Rig;
            if (rig == null || _weaponRigidbody == null)
            {
                Logger.LogWarning("[VrSwing] No VR rig or weapon body - this weapon can't swing.", LogCat.VR);
                _swing.Stop();
                return;
            }

            _swing.Start(rig, _vrInput, _weaponRigidbody.transform, handSide, thresholds);
        }

        private bool CanHandle(VobContainer newWeapon)
        {
            // No weapon handled so far. Free for the first one.
            if (WeaponVobContainer == null)
                return true;

            // The handled weapon got destroyed while in hand (no release event reached us). Free this handler again.
            if (WeaponVobContainer.Go == null)
            {
                Logger.LogWarning("Handled weapon was destroyed while held. Resetting weapon attack handler.",
                    LogCat.VR);
                FullStopHandling();
                return true;
            }

            // The same weapon is trying to be grabbed by another hand.
            if (WeaponVobContainer == newWeapon)
                return true;

            // else
            return false;
        }

        public bool TryUnHandle(WeaponPhysicsConfig weaponConfig, VrHandSide handSide)
        {
            if (!CanUnHandle(handSide))
                return false;

            if (handSide == VrHandSide.Left)
                _handlesLeftHand = false;
            else if (handSide == VrHandSide.Right)
                _handlesRightHand = false;

            _swing.SetHolding(_handlesLeftHand, _handlesRightHand);

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
        private bool CanUnHandle(VrHandSide releasedHandSide)
        {
            if (releasedHandSide == VrHandSide.Left)
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
            // Samples of an earlier hold (a throw, the weapon flying into the hand) started the next swing at once.
            _swing.Stop();
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
            var mds = _resourceCacheService.TryGetModelScript("Humans");
            return mds?.Animations.FirstOrDefault(i => i.Name.EqualsIgnoreCase(attackAnimationName));
        }

        public VmGothicEnums.WeaponState GetWeaponState()
        {
            if (WeaponVobContainer == null)
                return VmGothicEnums.WeaponState.NoWeapon;

            return Is2HD() ? VmGothicEnums.WeaponState.W2H : VmGothicEnums.WeaponState.W1H;
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
            {
                Logger.LogWarning($"Humans.mds: '{attackAnim.Name}' has no sound effect. Weapon swing will be silent.",
                    LogCat.VR);
                return;
            }

            _swingSwordSound = _vmCacheService.TryGetSfxData(soundAttack.Name);
            _soundPlayTime = soundAttack.Frame / attackAnim.Fps;
        }

        /// <summary>
        /// Every rendered frame: the tracked controllers move per frame, not per physics step.
        /// </summary>
        public void SampleSwing()
        {
            if (_weaponRigidbody == null)
                return;

            _swing.Sample();
        }

        /// <summary>
        /// Every physics step: the attack window logic, on the latest swing values.
        /// </summary>
        public void FixedUpdate()
        {
            if (_weaponRigidbody == null)
                return;

            UpdateVrAttackLogic();
        }

        public void AddLeftHand()
        {
            _handlesLeftHand = true;
            _swing.SetHolding(_handlesLeftHand, _handlesRightHand);
        }

        public void AddRightHand()
        {
            _handlesRightHand = true;
            _swing.SetHolding(_handlesLeftHand, _handlesRightHand);
        }

        public void RemoveLeftHand()
        {
            _handlesLeftHand = false;
            _swing.SetHolding(_handlesLeftHand, _handlesRightHand);
        }

        public void RemoveRightHand()
        {
            _handlesRightHand = false;
            _swing.SetHolding(_handlesLeftHand, _handlesRightHand);
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

            // A mod's Humans.mds can be missing the swing sound effect on this attack animation.
            if (_swingSwordSound == null)
                return;

            _vrSfxPlayer.Play(_audioService.CreateAudioClip(_swingSwordSound.GetRandomSound()), _weaponRigidbody.position);
        }

        private void HandleInitialWindow()
        {
            // An unconscious hero can still swing a weapon picked up again - but it's no attack.
            if (GetOwner()?.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return;

            // Hand and tip fast enough for long enough, and the tip went far enough (or a thrust along the blade).
            if (_swing.IsSwinging())
                StartAttack();
        }

        private void StartAttack()
        {
            _hasDroppedBelowThreshold = false;
            _hasReturnedToThreshold = false;

            // Which values made this a swing (to tune the thresholds from). Built inside the call: LogEditor is
            // stripped in release, its arguments then cost nothing.
            Logger.LogEditor($"[VrSwing] Attack: {_swing.Describe()}", LogCat.VR);

            // If no sound is set, ignore playing it and mark it as "played".
            _soundPlayed = _swingSwordSound == null;

            _stateMachine.StartAttack();

            // The swing is the hero's "prehit" - NPCs can parry or jump back.
            if (GetOwner()?.PrefabProps?.IsHero() == true)
                GlobalEventDispatcher.HeroMeleeSwing.Invoke();
        }

        private bool CheckIfComboWindowFailed()
        {
            // Track velocity drops and returns
            if (_swing.TipSpeed < _velocityDropThreshold && !_hasDroppedBelowThreshold)
                _hasDroppedBelowThreshold = true;

            if (_hasDroppedBelowThreshold && _swing.TipSpeed >= _attackVelocityThreshold && !_hasReturnedToThreshold)
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
            if (!_hasDroppedBelowThreshold && _swing.TipSpeed < _velocityDropThreshold)
                _hasDroppedBelowThreshold = true;

            if (_hasDroppedBelowThreshold && _swing.TipSpeed >= _attackVelocityThreshold)
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
