#if GOTHIC_HVR_INSTALLED
using Gothic.Core;
using Gothic.Core.Adapters.UI.Menus;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using HurricaneVR.Framework.Core.Player;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZenKit.Daedalus;

namespace Gothic.VR.Adapters.HVROverrides
{
    public class VRPlayerController : HVRPlayerController
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;

        public VRPlayerInputs VrInputs => (VRPlayerInputs)Inputs;

        [Separator("Gothic - Settings")]
        public MenuHandler MenuHandler;

        [SerializeField] private float _characterControllerSwimDiveStepHeight = 1f; // 1m means walking out of water in Xardas' old tower in G1.

        // For resetting values when stop swimming.
        private float _defaultCharacterControllerStepHeight;

        // Speed potions (Mdl_ApplyOverlayMDSTimed HUMANS_SPRINT.MDS): faster VR movement for the potion's time.
        private const string _sprintOverlayName = "SPRINT";
        private const float _sprintSpeedFactor = 1.5f;
        private bool _isSpeedBoosted;
        private string _speedOverlay;
        // The potion's overlay (HUMANS_SPRINT.MDS) while it works - the VR hero body runs with its sprint animations.
        public string SpeedOverlay => _isSpeedBoosted ? _speedOverlay : null;
        private float _speedBoostEndTime;
        private float _unboostedMoveSpeed;
        private float _unboostedRunSpeed;

        // Horizontal speed (m/s) for BS_WALK/BS_RUN. Moving in VR equals the hero's default run in Gothic.
        private const float _bodyStateWalkSpeed = 0.2f;
        private const float _bodyStateRunSpeed = 1f;

        protected override void Start()
        {
            base.Start();
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefsUpdated);
            GlobalEventDispatcher.ScriptHeroOverlayTimed.AddListener(OnHeroOverlayTimed);

            _defaultCharacterControllerStepHeight = CharacterController.stepOffset;

            // Enabled later via button press or other events
            MenuHandler?.gameObject.SetActive(false);
        }

        protected override void Update()
        {
            base.Update();

            if (VrInputs.IsMenuActivated && IsGameScene())
            {
                _gameStateService.InGameAndAlive = true;
                MenuHandler.ToggleVisibility();
            }

            UpdateHeroBodyState();
            UpdateSpeedBoost();
        }

        private void OnHeroOverlayTimed(string overlayName, float seconds)
        {
            if (overlayName == null || overlayName.IndexOf(_sprintOverlayName, System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            _speedOverlay = overlayName;
            if (!_isSpeedBoosted)
            {
                _unboostedMoveSpeed = MoveSpeed;
                _unboostedRunSpeed = RunSpeed;
                MoveSpeed *= _sprintSpeedFactor;
                RunSpeed *= _sprintSpeedFactor;
                _isSpeedBoosted = true;
            }
            _speedBoostEndTime = Time.time + seconds;
        }

        private void UpdateSpeedBoost()
        {
            if (!_isSpeedBoosted || Time.time < _speedBoostEndTime)
                return;

            _isSpeedBoosted = false;
            // Swimming/diving set their own speeds meanwhile (VRSwimDive) - only undo our own change.
            if (Mathf.Approximately(MoveSpeed, _unboostedMoveSpeed * _sprintSpeedFactor))
                MoveSpeed = _unboostedMoveSpeed;
            if (Mathf.Approximately(RunSpeed, _unboostedRunSpeed * _sprintSpeedFactor))
                RunSpeed = _unboostedRunSpeed;
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
                    or VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun))
                return;

            var velocity = CharacterController.velocity;
            var speed = new Vector2(velocity.x, velocity.z).magnitude;
            props.BodyState = speed switch
            {
                >= _bodyStateRunSpeed => VmGothicEnums.BodyState.BsRun,
                >= _bodyStateWalkSpeed => VmGothicEnums.BodyState.BsWalk,
                _ => VmGothicEnums.BodyState.BsStand
            };
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefsUpdated);
            GlobalEventDispatcher.ScriptHeroOverlayTimed.RemoveListener(OnHeroOverlayTimed);
        }

        /// <summary>
        /// Used in game scenes where you play the game (world.unity, ...)
        /// </summary>
        public void SetNormalControls(bool useDefaultValues = false)
        {
            // We have our player created before Gothic inis are loaded. We therefore need to set some default values.
            if (useDefaultValues)
            {
                CameraRig.SetSitStandMode(HVRSitStand.PlayerHeight);
                DirectionStyle = PlayerDirectionMode.Camera;
                RotationType = RotationType.Snap;
                SnapAmount = 45f;
                SmoothTurnSpeed = 90f;

                return;
            }

            var sitStandSetting =
                _configService.Gothic.GetInt(VRConstants.IniNames.SitStand, (int)HVRSitStand.PlayerHeight);
            CameraRig.SetSitStandMode((HVRSitStand)sitStandSetting);

            DirectionStyle = (PlayerDirectionMode)_configService.Gothic.GetInt(VRConstants.IniNames.MoveDirection, (int)PlayerDirectionMode.Camera);
            RotationType = (RotationType)_configService.Gothic.GetInt(VRConstants.IniNames.RotationType, (int)RotationType.Snap);

            var snapSetting = _configService.Gothic.GetInt(VRConstants.IniNames.SnapRotationAmount, VRConstants.SnapRotationDefaultValue);
            // e.g., 20° = 5° + 3*5°
            SnapAmount = VRConstants.SnapRotationAmountSettingTickAmount + VRConstants.SnapRotationAmountSettingTickAmount * snapSetting;
            
            var smoothSetting = _configService.Gothic.GetFloat(VRConstants.IniNames.SmoothRotationSpeed, VRConstants.SmoothRotationDefaultValue);
            // e.g., 50 = 5 + 90 * 0.5f
            SmoothTurnSpeed = VRConstants.SmoothRotationMinSpeed + VRConstants.SmoothRotationMaxAdditionalSpeed * smoothSetting;
        }

        /// <summary>
        /// Disable certain actions to keep player stuck in current position.
        /// </summary>
        public void SetLockedControls()
        {
            // HINT: Disable physics
            // We can't disable physics as it would prevent HVRTeleport.Teleport() from finishing (as it checks for Player.IsGrounded every frame).
            // Therefore, we need to ground the player always on a plane and disable movement only.

            MovementEnabled = false;
            RotationEnabled = false;
            Teleporter.enabled = false;
        }

        public void SetUnlockedControls()
        {
            MovementEnabled = true;
            RotationEnabled = true;
            Teleporter.enabled = true;
        }
        
        public void SetWalkingControls()
        {
            ChangeGrabbing(true);

            CanCrouch = true;
            CanJump = true;
            Teleporter.enabled = true;
            CharacterController.stepOffset = _defaultCharacterControllerStepHeight;

            // Disable vertical walking controls
        }
        
        public void SetWaterWalkingControls()
        {
            ChangeGrabbing(true);

            CanCrouch = false;
            CanJump = false;
            Teleporter.enabled = false;
            CharacterController.stepOffset = _characterControllerSwimDiveStepHeight;
        }

        public void SetSwimmingControls()
        {
            // Disable grabbing of objects (as in G1)
            ChangeGrabbing(false);

            CanCrouch = false;
            CanJump = false;
            Teleporter.enabled = false;
            CharacterController.stepOffset = _characterControllerSwimDiveStepHeight;

            // Disable vertical walking controls
        }
        
        public void SetDivingControls()
        {
            // Disable grabbing of objects (as in G1)
            ChangeGrabbing(false);

            CanCrouch = false;
            CanJump = false;
            Teleporter.enabled = false;
            CharacterController.stepOffset = _characterControllerSwimDiveStepHeight;

            // Enable vertical walking controls
        }

        /// <summary>
        /// Transformed into a monster (VRTransformService): no grabbing, backpack or looting - like the engine.
        /// </summary>
        public void SetGrabbingEnabled(bool enable) => ChangeGrabbing(enable);

        private void ChangeGrabbing(bool enable)
        {
            LeftHand.AllowGrabbing = enable;
            LeftHand.ForceGrabber.AllowGrabbing = enable;
            LeftHand.AllowHovering = enable;
            LeftHand.ForceGrabber.AllowHovering = enable;

            RightHand.AllowGrabbing = enable;
            RightHand.ForceGrabber.AllowGrabbing = enable;
            RightHand.AllowHovering = enable;
            RightHand.ForceGrabber.AllowHovering = enable;
            
            // Disable hand animations. Basically open the hand fully if disabled=true
            if (LeftHand.HandAnimator && LeftHand.HandAnimator.CurrentPoser)
            {
                if (LeftHand.HandAnimator.CurrentPoser.PrimaryPose != null)
                {
                    LeftHand.HandAnimator.CurrentPoser.PrimaryPose.Disabled = !enable;
                }

                if (LeftHand.HandAnimator.CurrentPoser.Blends != null)
                {
                    LeftHand.HandAnimator.CurrentPoser.Blends.ForEach(i => i.Disabled = !enable);
                }
            }

            if (RightHand.HandAnimator && RightHand.HandAnimator.CurrentPoser)
            {
                if (RightHand.HandAnimator.CurrentPoser.PrimaryPose != null)
                {
                    RightHand.HandAnimator.CurrentPoser.PrimaryPose.Disabled = !enable;
                }

                if (RightHand.HandAnimator.CurrentPoser.Blends != null)
                {
                    RightHand.HandAnimator.CurrentPoser.Blends.ForEach(i => i.Disabled = !enable);
                }
            }
        }

        private void OnPlayerPrefsUpdated(string preferenceKey, object value)
        {
            // Just update everything.
            if (preferenceKey == VRConstants.IniNames.MoveDirection ||
                preferenceKey == VRConstants.IniNames.RotationType ||
                preferenceKey == VRConstants.IniNames.SnapRotationAmount ||
                preferenceKey == VRConstants.IniNames.SmoothRotationSpeed ||
                preferenceKey == VRConstants.IniNames.SitStand)
            {
                SetNormalControls();
            }
        }

        /// <summary>
        /// Game scenes are all the ones where we play. Aka !=MainMenu, !=Loadings, ...
        /// </summary>
        private bool IsGameScene()
        {
            var activeSceneName = SceneManager.GetActiveScene().name;

            return activeSceneName switch
            {
                Constants.SceneMainMenu => false,
                Constants.SceneLoading => false,
                _ => true
            };
        }

        // protected override void HandleHorizontalMovement()
        // {
        //     if (_playerAi.WalkMode == (int)VmGothicEnums.WalkMode.Swim)
        //     {
        //         
        //     }
        //     else if (_playerAi.WalkMode == (int)VmGothicEnums.WalkMode.Dive)
        //     {
        //         
        //     }
        //     else
        //     {
        //         base.HandleHorizontalMovement();
        //     }
        // }
        //
        // protected override void HandleVerticalMovement()
        // {
        //     if (_playerAi.WalkMode == (int)VmGothicEnums.WalkMode.Swim)
        //     {
        //         
        //     }
        //     else if (_playerAi.WalkMode == (int)VmGothicEnums.WalkMode.Dive)
        //     {
        //         
        //     }
        //     else
        //     {
        //         base.HandleVerticalMovement();
        //     }
        // }
    }
}
#endif
