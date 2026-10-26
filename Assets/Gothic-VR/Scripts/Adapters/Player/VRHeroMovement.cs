using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Audio;
using Gothic.Core.Services.Config;
using Gothic.VR.Interaction.Ports;
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
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly GroundSoundService _groundSoundService;

        // DeveloperConfig.EnableFootstepSounds: the hero's own steps - Gothic's RUN_<ground> every stride of the way
        // walked (the VR body plays no step events; a timer stepped on while the run state lingered after a tap).
        // Sneaking with the talent is silent.
        private const string _stepSound = "RUN";
        private const float _walkStrideMeters = 0.75f;
        private const float _runStrideMeters = 1.1f;
        private const float _stepVolume = 1f;
        private AudioSource _stepSource;
        private float _strideDistance;
        private Vector3 _lastStepPosition;

        // Speed potions (Mdl_ApplyOverlayMDSTimed HUMANS_SPRINT.MDS): faster VR movement for the potion's time.
        private const string _sprintOverlayName = "SPRINT";
        private const float _sprintSpeedFactor = 1.5f;
        private bool _isSpeedBoosted;
        private string _speedOverlay;
        private float _speedBoostEndTime;
        private float _unboostedMoveSpeed;
        private float _unboostedRunSpeed;

        // Horizontal speed (m/s) for BS_WALK/BS_RUN. Moving in VR equals the hero's default run in Gothic.
        private const float _bodyStateWalkSpeed = 0.2f;
        private const float _bodyStateRunSpeed = 1f;
        private const float _stickyRunSeconds = 1.5f;
        private const float _teleportJumpDistance = 1.5f;
        private float _lastRunTime = float.MinValue;
        private Vector3 _lastBodyStatePosition;

        // DeveloperConfig.EnableSneakSlowSprint: the run (sprint) speed while sneaking.
        private const float _sneakSprintFactor = 0.5f;
        private bool _isSneakSlowed;
        private float _sneakSlowedRunSpeed;
        private float _unslowedRunSpeed;

        // DeveloperConfig.EnableSneakNeedsTalent: NPC_TALENT_SNEAK (G1/G2: 8) - read from the scripts.
        private const string _sneakTalentSymbol = "NPC_TALENT_SNEAK";
        private const int _defaultSneakTalent = 8;
        private bool _isNoSneakLogged;

        private IVrRig _rig;

        /// <summary>
        /// The potion's overlay (HUMANS_SPRINT.MDS) while it works - the VR hero body runs with its sprint animations.
        /// </summary>
        public static string SpeedOverlay { get; private set; }

        private void Awake()
        {
            this.Inject();
            _rig = GetComponentInParent<IVrRig>();
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
            UpdateSneakSprint();
            UpdateHeroFootsteps();
            UpdateSpeedBoost();
        }

        private bool IsHeroSneaking()
        {
            return _gameStateService.GothicVm?.GlobalHero is NpcInstance heroInstance &&
                   heroInstance.GetUserData()?.Props.BodyState == VmGothicEnums.BodyState.BsSneak;
        }

        /// <summary>
        /// DeveloperConfig.EnableSneakSlowSprint: sprinting while sneaking (BS_SNEAK) runs at half the sprint speed -
        /// sneaking up behind someone at full sprint looked silly. Swimming or a potion changing the run speed
        /// meanwhile keeps its own value.
        /// </summary>
        private void UpdateSneakSprint()
        {
            var isSlowed = _configService.Dev.EnableSneakSlowSprint && IsHeroSneaking();
            if (isSlowed == _isSneakSlowed)
                return;

            _isSneakSlowed = isSlowed;
            if (isSlowed)
            {
                _unslowedRunSpeed = _rig.RunSpeed;
                _sneakSlowedRunSpeed = _rig.RunSpeed * _sneakSprintFactor;
                _rig.RunSpeed = _sneakSlowedRunSpeed;
            }
            else if (Mathf.Approximately(_rig.RunSpeed, _sneakSlowedRunSpeed))
            {
                _rig.RunSpeed = _unslowedRunSpeed;
            }
        }

        private void OnHeroOverlayTimed(string overlayName, float seconds)
        {
            if (_rig == null || overlayName == null ||
                overlayName.IndexOf(_sprintOverlayName, System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            _speedOverlay = overlayName;
            if (!_isSpeedBoosted)
            {
                _unboostedMoveSpeed = _rig.MoveSpeed;
                _unboostedRunSpeed = _rig.RunSpeed;
                _rig.MoveSpeed *= _sprintSpeedFactor;
                _rig.RunSpeed *= _sprintSpeedFactor;
                _isSpeedBoosted = true;
            }
            _speedBoostEndTime = Time.time + seconds;
            SpeedOverlay = _speedOverlay;
        }

        private void UpdateSpeedBoost()
        {
            if (!_isSpeedBoosted || Time.time < _speedBoostEndTime)
                return;

            _isSpeedBoosted = false;
            SpeedOverlay = null;
            // Swimming/diving set their own speeds meanwhile (VRSwimDive) - only undo our own change.
            if (Mathf.Approximately(_rig.MoveSpeed, _unboostedMoveSpeed * _sprintSpeedFactor))
                _rig.MoveSpeed = _unboostedMoveSpeed;
            if (Mathf.Approximately(_rig.RunSpeed, _unboostedRunSpeed * _sprintSpeedFactor))
                _rig.RunSpeed = _unboostedRunSpeed;
        }

        /// <summary>
        /// DeveloperConfig.EnableHeroMoveBodyState: the engine sets BS_WALK/BS_RUN from the hero's movement. Scripts
        /// read it, e.g. G1 ZS_Attack_Loop gives up a chase ("$RUNCOWARD") after HAI_TIME_FOLLOW loops of a running
        /// target. Only stand/walk/run are touched - swim/dive (VRSwimDive), unconscious, sitting, ... stay.
        /// </summary>
        private void UpdateHeroBodyState()
        {
            if (!_configService.Dev.EnableHeroMoveBodyState)
                return;
            if (_gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;

            var props = heroInstance.GetUserData()?.Props;
            if (props == null || props.BodyState is not (VmGothicEnums.BodyState.BsStand
                    or VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun
                    or VmGothicEnums.BodyState.BsSneak))
                return;

            // DeveloperConfig.EnableHeroSneakBodyState: crouched = sneaking.
            // Lying or standing up (VRHeroBody's fall camera lowers the head) isn't sneaking - an NPC asked "why are you
            // sneaking around" while the hero got up.
            // DeveloperConfig.EnableSneakNeedsTalent: like the engine (OpenGothic Npc::canSneak) only with the sneak
            // talent - without it a crouching hero just walks: guards turn to him, Lukis hears him coming.
            if (_configService.Dev.EnableHeroSneakBodyState && _rig.IsCrouching && !VRHeroBody.IsHeroDown &&
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

            // DeveloperConfig.EnableStickyHeroRun: running is a snapshot of the speed - in VR you slow down in every
            // turn, and a teleport has no speed at all. ZS_Attack_Loop checks BS_RUN every ~2 s and resets its pursuit
            // counter whenever the hero isn't running at that moment, so NPCs never gave up a chase. Running (or a
            // teleport jump) keeps BS_RUN for a moment.
            if (_configService.Dev.EnableStickyHeroRun)
            {
                var position = _rig.Root.position;
                var horizontalJump = new Vector2(position.x - _lastBodyStatePosition.x,
                    position.z - _lastBodyStatePosition.z).magnitude;
                _lastBodyStatePosition = position;

                if (bodyState == VmGothicEnums.BodyState.BsRun || horizontalJump >= _teleportJumpDistance)
                    _lastRunTime = Time.time;
                else if (Time.time - _lastRunTime < _stickyRunSeconds)
                    bodyState = VmGothicEnums.BodyState.BsRun;
            }

            props.BodyState = bodyState;
        }

        private void UpdateHeroFootsteps()
        {
            // DeveloperConfig.EnableHeroBodySounds: the animated body steps itself.
            if (!_configService.Dev.EnableFootstepSounds || VRHeroBody.IsBodySounding || !_rig.IsGrounded ||
                _gameStateService.GothicVm?.GlobalHero is not NpcInstance heroInstance)
                return;
            var position = _rig.Root.position;
            var moved = new Vector2(position.x - _lastStepPosition.x, position.z - _lastStepPosition.z).magnitude;
            _lastStepPosition = position;

            var bodyState = heroInstance.GetUserData()?.Props?.BodyState;
            // A teleport or a fall isn't walking; sneaking (with the talent) is silent.
            if (bodyState is not (VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun) ||
                moved > _runStrideMeters)
                return;

            _strideDistance += moved;
            var stride = bodyState == VmGothicEnums.BodyState.BsRun ? _runStrideMeters : _walkStrideMeters;
            if (_strideDistance < stride)
                return;
            _strideDistance = 0f;

            var clip = _audioService.GetRandomSoundClip(_groundSoundService.GetSoundName(_stepSound, position));
            if (clip == null)
                return;
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
            if (!_configService.Dev.EnableSneakNeedsTalent)
                return true;
            var sneakTalent = _gameStateService.GothicVm.GetSymbolByName(_sneakTalentSymbol)?.GetInt(0) ??
                              _defaultSneakTalent;
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
