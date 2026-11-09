using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Audio;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// What the engine derives from the hero's movement, for any VR backend (IVrRig): BS_WALK/BS_RUN/BS_SNEAK, the
    /// hero's own footsteps, the speed potion's faster movement and the slower sprint while sneaking.
    /// Sits next to VRPlayer (added by VRContextInteractionService).
    /// </summary>
    public class VRHeroMovement : MonoBehaviour
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly GroundSoundService _groundSoundService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        // Gothic.ini footstepSounds: the hero's own steps - Gothic's RUN_<ground> every stride of the way
        // walked (the VR body plays no step events; a timer stepped on while the run state lingered after a tap).
        // Sneaking with the talent is silent.
        private const string _stepSound = "RUN";
        private const float _walkStrideMeters = 0.75f;
        private const float _runStrideMeters = 1.1f;
        private const float _stepVolume = 1f;
        private AudioSource _stepSource;
        private float _strideDistance;
        private Vector3 _lastStepPosition;
        // Why the last step was silent (logged once per change).
        private string _silentStepReason;

        // Speed potions (Mdl_ApplyOverlayMDSTimed HUMANS_SPRINT.MDS): faster VR movement for the potion's time.
        private const string _sprintOverlayName = "SPRINT";
        private const float _sprintSpeedFactor = 1.5f;
        private bool _isSpeedBoosted;
        private string _speedOverlay;
        private float _speedBoostEndTime;

        // Horizontal speed (m/s) for BS_WALK/BS_RUN. Moving in VR equals the hero's default run in Gothic.
        private const float _bodyStateWalkSpeed = 0.2f;
        private const float _bodyStateRunSpeed = 1f;
        private const float _stickyRunSeconds = 1.5f;
        private const float _teleportJumpDistance = 1.5f;
        private float _lastRunTime = float.MinValue;
        private Vector3 _lastBodyStatePosition;

        // Like vanilla Gothic: no sprint while sneaking. The sprint input then moves at the plain move speed.
        private bool _isSneaking;

        // The speeds without the potion and the sneak rule, and what we set last - one base, the factor on top (the
        // two undoing each other's values left the run speed wrong for good). Changed by someone else meanwhile
        // (swimming, a transformation): that's the new base.
        private bool _isSpeedModified;
        private float _baseMoveSpeed;
        private float _baseRunSpeed;
        private float _appliedMoveSpeed;
        private float _appliedRunSpeed;

        // NPC_TALENT_SNEAK (G1/G2: 8) - read from the scripts.
        private const string _sneakTalentSymbol = "NPC_TALENT_SNEAK";
        private const int _defaultSneakTalent = 8;
        private bool _isNoSneakLogged;
        private object _sneakTalentVm;
        private int _sneakTalent;

        private IVrRig _rig;

        /// <summary>
        /// The potion's overlay (HUMANS_SPRINT.MDS) while it works - the VR hero body runs with its sprint animations.
        /// </summary>
        public static string SpeedOverlay { get; private set; }

        private void Awake()
        {
            this.Inject();
            _rig = GetComponentInParent<IVrRig>();
            if (_rig == null)
                Logger.LogWarning("[VRHeroMovement] No VR rig above - no body states, steps or speed changes.",
                    LogCat.VR);
            GlobalEventDispatcher.ScriptHeroOverlayTimed.AddListener(OnHeroOverlayTimed);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.ScriptHeroOverlayTimed.RemoveListener(OnHeroOverlayTimed);
            SpeedOverlay = null;
        }

        private void Update()
        {
            if (_rig == null)
                return;

            UpdateHeroBodyState();
            UpdateSneakSpeed();
            UpdateHeroFootsteps();
            UpdateSpeedBoost();
        }

        private bool IsHeroSneaking()
        {
            return _gameStateService.GothicVm?.GlobalHero is NpcInstance heroInstance &&
                   heroInstance.GetUserData()?.Props?.BodyState == VmGothicEnums.BodyState.BsSneak;
        }

        /// <summary>
        /// No sprint while sneaking (BS_SNEAK), like vanilla Gothic: the sprint input does nothing, the hero moves at
        /// sneak (move) speed. The body state stays BS_SNEAK (UpdateHeroBodyState), so it is no run for the footsteps
        /// and the scripts.
        /// </summary>
        private void UpdateSneakSpeed()
        {
            _isSneaking = IsHeroSneaking();
            ApplySpeeds();
        }

        /// <summary>
        /// The base speeds times the potion (move and run); sneaking caps the run speed at the move speed.
        /// </summary>
        private void ApplySpeeds()
        {
            var isModified = _isSpeedBoosted || _isSneaking;
            if (!_isSpeedModified && !isModified)
                return;

            if (!_isSpeedModified)
            {
                _baseMoveSpeed = _rig.MoveSpeed;
                _baseRunSpeed = _rig.RunSpeed;
            }
            else if (!Mathf.Approximately(_rig.MoveSpeed, _appliedMoveSpeed) ||
                     !Mathf.Approximately(_rig.RunSpeed, _appliedRunSpeed))
            {
                // Someone else set speeds while ours were on: they are the new base, our factors go on top. VRSwimDive
                // sets its plain speeds (the ones it found at the start, half in water) - divided by our factors, a
                // potion through knee deep water left the hero a third slower, a sneak twice as fast.
                _baseMoveSpeed = _rig.MoveSpeed;
                _baseRunSpeed = _rig.RunSpeed;
            }

            var boost = _isSpeedBoosted ? _sprintSpeedFactor : 1f;
            _appliedMoveSpeed = _baseMoveSpeed * boost;
            _appliedRunSpeed = _isSneaking ? _appliedMoveSpeed : _baseRunSpeed * boost;
            _rig.MoveSpeed = _appliedMoveSpeed;
            _rig.RunSpeed = _appliedRunSpeed;
            _isSpeedModified = isModified;
        }

        private void OnHeroOverlayTimed(string overlayName, float seconds)
        {
            if (_rig == null || overlayName == null ||
                overlayName.IndexOf(_sprintOverlayName, System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            _speedOverlay = overlayName;
            _isSpeedBoosted = true;
            ApplySpeeds();
            _speedBoostEndTime = Time.time + seconds;
            SpeedOverlay = _speedOverlay;
        }

        private void UpdateSpeedBoost()
        {
            if (!_isSpeedBoosted || Time.time < _speedBoostEndTime)
                return;

            _isSpeedBoosted = false;
            SpeedOverlay = null;
            ApplySpeeds();
        }

        /// <summary>
        /// The engine sets BS_WALK/BS_RUN from the hero's movement. Scripts
        /// read it, e.g. G1 ZS_Attack_Loop gives up a chase ("$RUNCOWARD") after HAI_TIME_FOLLOW loops of a running
        /// target. Only stand/walk/run are touched - swim/dive (VRSwimDive), unconscious, sitting, ... stay.
        /// </summary>
        private void UpdateHeroBodyState()
        {
            if (_gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;

            var position = _rig.Root.position;
            var horizontalJump = new Vector2(position.x - _lastBodyStatePosition.x,
                position.z - _lastBodyStatePosition.z).magnitude;
            var isFirstCheck = _lastBodyStatePosition == Vector3.zero;
            _lastBodyStatePosition = position;

            var props = heroInstance.GetUserData()?.Props;
            if (props == null || props.BodyState is not (VmGothicEnums.BodyState.BsStand
                    or VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun
                    or VmGothicEnums.BodyState.BsSneak))
                return;

            // Crouched = sneaking.
            // Lying or standing up (the VR hero body's fall camera lowers the head) isn't sneaking - an NPC asked "why are you
            // sneaking around" while the hero got up.
            // Like the engine (OpenGothic Npc::canSneak) only with the sneak
            // talent - without it a crouching hero just walks: guards turn to him, Lukis hears him coming.
            if (_rig.IsCrouching && !_vrPlayerService.IsHeroBodyDown &&
                CanHeroSneak(heroInstance))
            {
                props.BodyState = VmGothicEnums.BodyState.BsSneak;
                return;
            }

            var velocity = _rig.CharacterController.velocity;
            var speed = new Vector2(velocity.x, velocity.z).magnitude;
            var bodyState = speed switch
            {
                >= _bodyStateRunSpeed => VmGothicEnums.BodyState.BsRun,
                >= _bodyStateWalkSpeed => VmGothicEnums.BodyState.BsWalk,
                _ => VmGothicEnums.BodyState.BsStand
            };

            // Running is a snapshot of the speed - in VR you slow down in every
            // turn, and a teleport has no speed at all. ZS_Attack_Loop checks BS_RUN every ~2 s and resets its pursuit
            // counter whenever the hero isn't running at that moment, so NPCs never gave up a chase. Running (or a
            // teleport jump) keeps BS_RUN for a moment.
            if (bodyState == VmGothicEnums.BodyState.BsRun || (!isFirstCheck && horizontalJump >= _teleportJumpDistance))
                _lastRunTime = Time.time;
            else if (Time.time - _lastRunTime < _stickyRunSeconds)
                bodyState = VmGothicEnums.BodyState.BsRun;

            props.BodyState = bodyState;
        }

        private void LogSilentStep(string reason)
        {
            if (reason == _silentStepReason)
                return;
            _silentStepReason = reason;
            Logger.Log($"[HeroSteps] No step sound: {reason}", LogCat.VR);
        }

        private void UpdateHeroFootsteps()
        {
            // The animated body steps itself (the stride steps below are the fallback for a body without animations).
            if (!_groundSoundService.IsEnabled || VRHeroBody.IsBodySounding ||
                _gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;
            if (!_rig.IsGrounded)
            {
                LogSilentStep("not grounded");
                return;
            }
            var position = _rig.Root.position;
            var moved = new Vector2(position.x - _lastStepPosition.x, position.z - _lastStepPosition.z).magnitude;
            _lastStepPosition = position;

            var bodyState = heroInstance.GetUserData()?.Props?.BodyState;
            // A teleport or a fall isn't walking; sneaking (with the talent) is silent.
            if (bodyState is not (VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun) ||
                moved > _runStrideMeters)
            {
                if (moved > 0.001f)
                    LogSilentStep(moved > _runStrideMeters ? $"jump of {moved:0.00} m" : $"body state {bodyState}");
                return;
            }

            _strideDistance += moved;
            var stride = bodyState == VmGothicEnums.BodyState.BsRun ? _runStrideMeters : _walkStrideMeters;
            if (_strideDistance < stride)
                return;
            _strideDistance = 0f;

            var soundName = _groundSoundService.GetSoundName(_stepSound, position);
            var clip = _audioService.GetRandomSoundClip(soundName);
            if (clip == null)
            {
                LogSilentStep($"no clip for {soundName}");
                return;
            }
            if (_silentStepReason != null)
                Logger.Log($"[HeroSteps] Steps play again ({soundName})", LogCat.VR);
            _silentStepReason = null;
            if (_stepSource == null)
            {
                _stepSource = gameObject.AddComponent<AudioSource>();
                _stepSource.playOnAwake = false;
                // The player's own feet: right below him, no distance falloff.
                _stepSource.spatialBlend = 0f;
            }
            _stepSource.PlayOneShot(clip, _stepVolume);
        }

        private bool CanHeroSneak(NpcInstance heroInstance)
        {
            var vm = _gameStateService.GothicVm;
            if (!ReferenceEquals(vm, _sneakTalentVm))
            {
                _sneakTalentVm = vm;
                _sneakTalent = vm?.GetSymbolByName(_sneakTalentSymbol)?.GetInt(0) ?? _defaultSneakTalent;
            }
            var sneakTalent = _sneakTalent;
            var vob = heroInstance.GetUserData()?.Vob;
            var canSneak = vob == null || vob.GetTalent(sneakTalent)?.Skill > 0;
            if (!canSneak && !_isNoSneakLogged)
            {
                _isNoSneakLogged = true;
                Logger.Log("[VRHeroMovement] Crouching without the sneak talent - the hero walks (not BS_SNEAK)",
                    LogCat.VR);
            }
            if (canSneak)
                _isNoSneakLogged = false;
            return canSneak;
        }
    }
}
