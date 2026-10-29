using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Caches;
using Gothic.Core.Models.Config;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.VR.Adapters.Marvin;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Adapters.UI;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services.Context
{
    public class VRContextInteractionService : IContextInteractionService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly VRCinemaService _vrCinemaService;
        [Inject] private readonly MarvinService _marvinService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly IVrUiPointers _vrUiPointers;
        // Also creates the service, which listens to the haptic events (fight, lock picking, physical hands).
        [Inject] private readonly VrHapticsService _vrHapticsService;
        [Inject] private readonly IVrBackend _vrBackend;

        private const string _contextName = "VR";

        // Meters per second the player is pulled back when they ended up beyond KeepPlayerWithin's radius.
        private const float _keepWithinPullBackSpeed = 1f;

        // Share of the normal speed left at KeepPlayerWithin's radius for walking back in or sideways.
        private const float _keepWithinMinSpeed = 0.25f;

        private IVrRig _rig;
        private VRPlayer _player;
        private float _defaultStepOffset;
        private GameObject _marvinRoot;
        private int _keepWithinLastFrame = -1;
        private Vector3 _keepWithinLastPosition;
        private VrPhysicsHand[] _physicsHands = System.Array.Empty<VrPhysicsHand>();

        /// <summary>
        /// The player body of the active VR backend. null before SetupPlayerController().
        /// </summary>
        public IVrRig Rig => _rig;

        /// <summary>
        /// Gothic side of the player prefab (menu, Gothic inputs). null before SetupPlayerController().
        /// </summary>
        public VRPlayer Player => _player;

        /// <summary>
        /// Physical hands of the player (Xri backend). Empty for backends with their own hand physics (HVR).
        /// </summary>
        public IReadOnlyList<VrPhysicsHand> PhysicsHands => _physicsHands;

        public VRContextInteractionService()
        {
            this.Inject();

            _vrWeaponService.Init();
            _vrCinemaService.Init();

            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(OnLoadingSceneLoaded);
            GlobalEventDispatcher.GothicInisInitialized.AddListener(() =>
            {
                SetRenderDistance(_configService.Gothic.IniVisualRange);
                ApplyComfortSettings();
                ApplyPhysicalHands();
            });
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(PlayerPrefsUpdated);
            GlobalEventDispatcher.MarvinModeChanged.AddListener(OnMarvinModeChanged);
            GlobalEventDispatcher.MarvinLogoPoked.AddListener(OnMarvinLogoPoked);
        }

        public string GetContextName()
        {
            return _contextName;
        }

        private void OnLoadingSceneLoaded()
        {
            // Needed for: World -> Open MainMenu -> Hit "Load"/"New Game"
            _player.MenuHandler.gameObject.SetActive(false);
        }

        public float GetFrameRate()
        {
            // If we have no VR device attached to our computer, we will get an NPE for activeLoader.
            if (_configService.Dev.EnableVRDeviceSimulator || XRGeneralSettings.Instance.Manager.activeLoader == null)
            {
                return 0;
            }

            var xrDisplay = XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<XRDisplaySubsystem>();
            xrDisplay.TryGetDisplayRefreshRate(out var xrRefreshRate);

            return xrRefreshRate;
        }

        public void SetupPlayerController(DeveloperConfig developerConfig)
        {
            // We call this method once at the very beginning of game start. We can safely assume we are in the Player.unity scene.
            var activeScene = SceneManager.GetActiveScene();

            // VR Player: Gothic part and the backend's rig live on the same GameObject (inactive in the scene). We use
            // the one with the backend's rig and remove any other, so that scene-wide lookups (dialog, canvases, ...)
            // find only ours.
            var players = activeScene.GetRootGameObjects()
                .SelectMany(i => i.GetComponentsInChildren<VRPlayer>(true))
                .ToList();
            _player = players.FirstOrDefault(i => _vrBackend.OwnsRig(i.GetComponent<IVrRig>()));
            if (_player == null)
            {
                var message = $"No VR player with a rig of the VR backend ({_vrBackend.Name}) found. " +
                              "Check the player prefab in Player.unity.";
                Logger.LogError(message, LogCat.VR);
                throw new MissingComponentException(message);
            }

            _rig = _player.GetComponent<IVrRig>();
            var playerRoot = _player.transform.root.gameObject;
            foreach (var otherRoot in players.Select(i => i.transform.root.gameObject).Distinct())
            {
                if (otherRoot != playerRoot)
                    Object.Destroy(otherRoot);
            }

            playerRoot.SetActive(true);
            _defaultStepOffset = _rig.StepOffset;
            ApplyComfortSettings(true);

            _physicsHands = _player.GetComponentsInChildren<VrPhysicsHand>(true);
            if (_configService.Gothic != null)
                ApplyPhysicalHands();

            // XRDeviceSimulator
            _vrInput.SetDeviceSimulatorActive(developerConfig.EnableVRDeviceSimulator);

            // Marvin Mode
            // Searched inside our player only: other players are destroyed at the end of this frame.
            _marvinRoot = playerRoot.GetComponentInChildren<MarvinRootHandler>(true)!.gameObject;
            _marvinRoot.SetActive(_marvinService.IsMarvinMode);

            // PrintScreen() messages in front of the camera
            activeScene.GetComponentInChildren<VRPrintScreen>(true)!.Init();
        }

        private void OnMarvinModeChanged(bool isActive)
        {
            _marvinRoot.SetActive(isActive);
        }

        /// <summary>
        /// Vibrates the hand whose laser pointer clicked the logo. Only while the pokes count (menu open in game),
        /// so the main menu doesn't hint at the activation.
        /// </summary>
        private void OnMarvinLogoPoked(PointerEventData eventData)
        {
            if (!_marvinService.IsActivationInputActive)
                return;

            if (!_vrUiPointers.TryGetHandSide(eventData, out var handSide))
            {
                Logger.LogWarning("[VRContextInteractionService] No VR pointer found for the Marvin logo click.",
                    LogCat.Ui);
                return;
            }

            _vrHapticsService.Vibrate(handSide, VrHapticPreset.Info);
        }

        private void PlayerPrefsUpdated(string key, object value)
        {
            if (key == GothicIniConfig.IniKeyVisualRange)
            {
                SetRenderDistance(int.Parse((string)value));
            }
            else if (key == VRConstants.IniNames.MoveDirection ||
                     key == VRConstants.IniNames.RotationType ||
                     key == VRConstants.IniNames.SnapRotationAmount ||
                     key == VRConstants.IniNames.SmoothRotationSpeed ||
                     key == VRConstants.IniNames.SitStand)
            {
                // Just update everything.
                ApplyComfortSettings();
            }
            else if (key == VRConstants.IniNames.PhysicalHands)
            {
                ApplyPhysicalHands();
            }
        }

        /// <summary>
        /// Immersion setting "physical hands" from Gothic.ini. Off: hands follow the controllers directly.
        /// </summary>
        private void ApplyPhysicalHands()
        {
            var isPhysical = _configService.Gothic.GetInt(VRConstants.IniNames.PhysicalHands,
                VRConstants.PhysicalHandsDefaultValue) != 0;

            foreach (var hand in _physicsHands)
                hand.IsPhysical = isPhysical;
        }

        /// <summary>
        /// Sit/stand, move direction and turning from Gothic.ini.
        /// </summary>
        /// <param name="useDefaultValues">Our player is created before Gothic inis are loaded. We therefore need
        /// to set some default values.</param>
        private void ApplyComfortSettings(bool useDefaultValues = false)
        {
            if (useDefaultValues)
            {
                _rig.ApplyComfortSettings(VrSitStand.PlayerHeight, VrMoveDirection.Camera, VrTurnType.Snap, 45f, 90f);
                return;
            }

            var gothicIni = _configService.Gothic;
            var sitStand = (VrSitStand)gothicIni.GetInt(VRConstants.IniNames.SitStand, (int)VrSitStand.PlayerHeight);
            var moveDirection =
                (VrMoveDirection)gothicIni.GetInt(VRConstants.IniNames.MoveDirection, (int)VrMoveDirection.Camera);
            var turnType = (VrTurnType)gothicIni.GetInt(VRConstants.IniNames.RotationType, (int)VrTurnType.Snap);

            var snapSetting = gothicIni.GetInt(VRConstants.IniNames.SnapRotationAmount,
                VRConstants.SnapRotationDefaultValue);
            // e.g., 20° = 5° + 3*5°
            var snapDegrees = VRConstants.SnapRotationAmountSettingTickAmount +
                              VRConstants.SnapRotationAmountSettingTickAmount * snapSetting;

            var smoothSetting = gothicIni.GetFloat(VRConstants.IniNames.SmoothRotationSpeed,
                VRConstants.SmoothRotationDefaultValue);
            // e.g., 50 = 5 + 90 * 0.5f
            var smoothSpeed = VRConstants.SmoothRotationMinSpeed +
                              VRConstants.SmoothRotationMaxAdditionalSpeed * smoothSetting;

            _rig.ApplyComfortSettings(sitStand, moveDirection, turnType, snapDegrees, smoothSpeed);
        }

        private void SetRenderDistance(int value)
        {
            // Starting with value=0 (20%) and ending with value=14 (300%)
            _rig.Camera.farClipPlane = GothicIniConfig.IniVisualRangeFactor * (value + 1);
        }

        public GameObject GetCurrentPlayerController()
        {
            return _player.gameObject;
        }

        /// <summary>
        /// Disable certain actions to keep player stuck in current position.
        /// </summary>
        public void LockPlayerInPlace()
        {
            // HINT: Disable physics
            // We can't disable physics as it would prevent the teleport from finishing (HVR checks for IsGrounded
            // every frame). Therefore, we need to ground the player always on a plane and disable movement only.
            _rig.MovementEnabled = false;
            _rig.TurnEnabled = false;
            _rig.TeleportEnabled = false;
        }

        public void UnlockPlayer()
        {
            _rig.MovementEnabled = true;
            _rig.TurnEnabled = true;
            _rig.TeleportEnabled = true;
        }

        public void SetWalkingControls()
        {
            _rig.SetGrabbingEnabled(true);

            _rig.CanCrouch = true;
            _rig.CanJump = true;
            _rig.TeleportEnabled = true;
            _rig.StepOffset = _defaultStepOffset;
        }

        public void SetWaterWalkingControls()
        {
            _rig.SetGrabbingEnabled(true);

            _rig.CanCrouch = false;
            _rig.CanJump = false;
            _rig.TeleportEnabled = false;
            _rig.StepOffset = _player.SwimDiveStepHeight;
        }

        public void SetSwimmingControls()
        {
            // Disable grabbing of objects (as in G1)
            _rig.SetGrabbingEnabled(false);

            _rig.CanCrouch = false;
            _rig.CanJump = false;
            _rig.TeleportEnabled = false;
            _rig.StepOffset = _player.SwimDiveStepHeight;
        }

        public void SetDivingControls()
        {
            // Same as swimming. Vertical movement is done by VRSwimDive.
            SetSwimmingControls();
        }

        public void TeleportPlayerTo(Vector3 position, Quaternion rotation = default)
        {
            _rig.Teleport(position, rotation);
        }

        public void KeepPlayerWithin(Vector3 center, float softRadius, float radius)
        {
            var controller = _rig?.CharacterController;
            if (controller == null)
                return;

            // The frame's movement is read from the position of the previous call. After a gap (new dialog, a frame
            // without a call) there is nothing to compare against.
            var position = _rig.Root.position;
            var frameMovement = _keepWithinLastFrame == Time.frameCount - 1
                ? position - _keepWithinLastPosition
                : Vector3.zero;
            _keepWithinLastFrame = Time.frameCount;
            _keepWithinLastPosition = position;

            // Falling or on a ladder: a sideways push would fight gravity/the climbing hands.
            if (!_rig.IsGrounded || _rig.IsClimbing)
                return;

            var offset = position - center;
            offset.y = 0f;
            var distance = offset.magnitude;
            if (distance <= softRadius || distance < Mathf.Epsilon)
                return;

            var outward = offset / distance;

            // All movement slows down the farther out the player is: full speed at softRadius, down to the minimum at
            // radius, so walking back in speeds up steadily. Only walking further outward goes down to a full stop.
            var speed = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(softRadius, radius, distance));
            var movingSpeed = Mathf.Max(speed, _keepWithinMinSpeed);

            frameMovement.y = 0f;
            var radialStep = Vector3.Dot(frameMovement, outward);
            var sidewaysMovement = frameMovement - outward * radialStep;
            var radialSpeed = radialStep > 0f ? speed : movingSpeed;

            var correction = -outward * (radialStep * (1f - radialSpeed)) - sidewaysMovement * (1f - movingSpeed);
            var heldDistance = distance - radialStep * (1f - radialSpeed);

            // Still beyond the edge after that (pushed by an NPC, room-scale step): drift back instead of snapping,
            // which made the camera bounce.
            var overshoot = heldDistance - radius;
            if (overshoot > 0f)
                correction -= outward * Mathf.Min(overshoot, _keepWithinPullBackSpeed * Time.deltaTime);

            if (correction == Vector3.zero)
                return;

            controller.Move(correction);
            _keepWithinLastPosition = _rig.Root.position;
        }

        public void InitUIInteraction()
        {
            // Find all ui canvases and add them to the laser pointers (to activate the red laser for clicking/grabbing)
            // WARNING: This looks through all opened scene trees and can become quite slow. Execute very carefully!
            _vrUiPointers.RegisterAllCanvases();
        }

        public void IntroduceChapter(string chapter, string text, string texture, string wav, int time)
        {
            var playerScene = SceneManager.GetSceneByName(Constants.ScenePlayer);

            // Check if we already loaded the Chapter change prefab
            var chapterPrefab = playerScene.GetRootGameObjects()
                .FirstOrDefault(i => i.GetComponentInChildren<VRIntroduceChapter>());

            if (chapterPrefab == null)
            {
                chapterPrefab = _resourceCacheService.TryGetPrefabObject(PrefabType.StoryIntroduceChapter);
                SceneManager.MoveGameObjectToScene(chapterPrefab, playerScene);
            }

            chapterPrefab.GetComponent<VRIntroduceChapter>().DisplayIntroduction(chapter, text, texture, wav, time);
        }

        public void DisableMenus()
        {
            _player.IsMenuButtonEnabled = false;
        }

        public void EnableMenus()
        {
            _player.IsMenuButtonEnabled = true;
        }
    }
}
