using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Caches;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Player;
using Gothic.VR.Adapters.Marvin;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Adapters.Trade;
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
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services.Context
{
    public class VRContextInteractionService : IContextInteractionService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly TradeService _tradeService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly VRHeldItemSyncService _vrHeldItemSyncService;
        [Inject] private readonly VRCinemaService _vrCinemaService;
        [Inject] private readonly VRGameViewFallbackService _vrGameViewFallbackService;
        [Inject] private readonly MarvinService _marvinService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly IVrUiPointers _vrUiPointers;
        // Also creates the service, which listens to the haptic events (fight, lock picking, physical hands).
        [Inject] private readonly VrHapticsService _vrHapticsService;
        [Inject] private readonly IVrBackend _vrBackend;

        private const string _contextName = "VR";

        // Loaded as VR/Prefabs/... (ResourceCacheService looks into the context's folder first).
        private const string _heroBodyPrefabPath = "Prefabs/Player-Elements/VRHeroBody";
        private const string _heroBodyName = "_VRHeroBodyController";

        // Alpha per second when the view is shown again (about 0.3 s).
        private const float _viewFadeInSpeed = 3f;

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
        private PlayerControls _blockedControls;
        // Teleporting is off in water (wading, swimming, diving), whatever the locks say.
        private bool _isTeleportAllowedHere = true;
        // Grabbing is off while swimming and diving (as in G1), whatever the locks say.
        private bool _isGrabbingAllowedHere = true;

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
            _vrHeldItemSyncService.Init();
            _vrCinemaService.Init();

            GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldSceneLoaded);
            // Items held in a hand don't pay (VRContextTradeService opens the counter).
            GlobalEventDispatcher.TradeStarted.AddListener(_ => _tradeService.GetHeldAmount = GetHeldAmount);
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(OnLoadingSceneLoaded);
            GlobalEventDispatcher.GothicInisInitialized.AddListener(() =>
            {
                SetRenderDistance(_configService.Gothic.IniVisualRange);
                ApplyComfortSettings();
                ApplyHeightOffset();
                ApplyPhysicalHands();
                ApplyControlLayout();
            });
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(PlayerPrefsUpdated);
            // A teleport spell: what the hands hold goes into the backpack first - a held rune stayed at the old
            // place (or vanished) and the hero stayed in magic mode. Not on scene teleports (loads).
            GlobalEventDispatcher.HeroScriptTeleporting.AddListener(StoreHeldItemsInBackpack);
            GlobalEventDispatcher.MarvinModeChanged.AddListener(OnMarvinModeChanged);
            GlobalEventDispatcher.MarvinLogoPoked.AddListener(OnMarvinLogoPoked);
        }

        public string GetContextName()
        {
            return _contextName;
        }

        /// <summary>
        /// DeveloperConfig.EnableVrHeroBody: the hero's own body + arms under the VR head. One per world scene: it's
        /// instantiated into the active (world) scene and goes with it.
        /// </summary>
        private void OnWorldSceneLoaded()
        {
            if (_configService.Dev.EnableVrHeroBody)
            {
                var heroBody = _resourceCacheService.TryGetPrefabObject(_heroBodyPrefabPath, _heroBodyName,
                    worldPositionStays: false);
                if (heroBody == null)
                    Logger.LogError($"[VRContextInteractionService] Hero body prefab >{_heroBodyPrefabPath}< not " +
                                    "found. No VR hero body.", LogCat.VR);
            }

            // Taking pieces off held stacks.
            new UnityEngine.GameObject("_VRStackSplitter").AddComponent<Gothic.VR.Adapters.Vob.VobItem.VRStackSplitter>();
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
            // A new rig starts unlocked - locks taken before (loading) apply to it too.
            ApplyBlockedControls();

            _physicsHands = _player.GetComponentsInChildren<VrPhysicsHand>(true);
            if (_configService.Gothic != null)
            {
                ApplyPhysicalHands();
                ApplyControlLayout();
            }

            // XRDeviceSimulator
            _vrInput.SetDeviceSimulatorActive(developerConfig.EnableVRDeviceSimulator);

            // A trigger click at a worn item in the backpack equips it.
            var equipToggle = new GameObject("_BackpackEquipToggle").AddComponent<VRBackpackEquipToggle>();
            equipToggle.transform.SetParent(_player.transform, false);

            // Editor: Game view while the headset renders nothing
            _vrGameViewFallbackService.Init();

            // The favorites ring, in the player's space - it moves with him. The "favoritesRing" option switches it.
            var favoritesRing = new GameObject("_FavoritesRing").AddComponent<VRFavoritesRing>();
            favoritesRing.transform.SetParent(_player.transform, false);

            // Purses on the hips of NPCs who can't see the sneaking hero (G1; G2: option pickpocketPurse).
            var pickpocket = new GameObject("_Pickpocket").AddComponent<VRPickpocket>();
            pickpocket.transform.SetParent(_player.transform, false);

            // The hero's walk/run/sneak state, his footsteps and the speed potion's faster movement (any backend).
            _player.gameObject.AddComponent<VRHeroMovement>();

            // Both hands on a melee weapon + both triggers = parry (option heroBlock, VRHeroBlockService).
            var heroBlock = new GameObject("_HeroBlock").AddComponent<VRHeroBlock>();
            heroBlock.transform.SetParent(_player.transform, false);

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
            else if (key == VRConstants.IniNames.HeightOffset)
            {
                // Not part of ApplyComfortSettings(): that one requests the tracking origin again, which recenters
                // the view in sitting mode on every slider step.
                ApplyHeightOffset();
            }
            else if (key == VRConstants.IniNames.PhysicalHands)
            {
                ApplyPhysicalHands();
            }
            else if (key == VRConstants.IniNames.ControllerHand ||
                     key == VRConstants.IniNames.JumpButton ||
                     key == VRConstants.IniNames.MenuButton)
            {
                ApplyControlLayout();
            }
        }

        /// <summary>
        /// Accessibility setting "height offset" from Gothic.ini. Slider value 0...1 maps to -1m...+1m.
        /// </summary>
        private void ApplyHeightOffset()
        {
            var setting = _configService.Gothic.GetFloat(VRConstants.IniNames.HeightOffset,
                VRConstants.HeightOffsetDefaultValue);
            var meters = (Mathf.Clamp01(setting) * 2f - 1f) * VRConstants.HeightOffsetMaxMeters;

            _rig.SetHeightOffset(meters);
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
        /// Game settings "controller hand", "jump button" and "menu button" from Gothic.ini.
        /// </summary>
        private void ApplyControlLayout()
        {
            var gothicIni = _configService.Gothic;
            _vrInput.ApplyControlLayout(VrControlLayout.FromSettings(
                gothicIni.GetInt(VRConstants.IniNames.ControllerHand, VRConstants.ControllerHandDefaultValue),
                gothicIni.GetInt(VRConstants.IniNames.JumpButton, VRConstants.JumpButtonDefaultValue),
                gothicIni.GetInt(VRConstants.IniNames.MenuButton, VRConstants.MenuButtonDefaultValue)));
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
                // The device simulator's head has no height: with the player's real height (floor tracking) the camera
                // sat on the floor and the wall fade blacked out the game version / profile menu. Seated, the rig puts
                // the eyes at their default height. Gothic.ini's setting takes over once it's loaded.
                var defaultSitStand = _configService.Dev.EnableVRDeviceSimulator
                    ? VrSitStand.Sitting
                    : VrSitStand.PlayerHeight;
                _rig.ApplyComfortSettings(defaultSitStand, VrMoveDirection.Camera, VrTurnType.Snap, 45f, 90f);
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
        /// Physics stays on while locked: a teleport only finishes once the player is grounded. Only the controls are
        /// switched off.
        /// </summary>
        public void SetBlockedControls(PlayerControls blockedControls)
        {
            _blockedControls = blockedControls;
            ApplyBlockedControls();
        }

        private void ApplyBlockedControls()
        {
            // The player can be gone already, e.g. a video's cinema unlocks while the player is destroyed.
            if (_rig == null || _player == null)
                return;

            _rig.MovementEnabled = (_blockedControls & PlayerControls.Move) == 0;
            _rig.TurnEnabled = (_blockedControls & PlayerControls.Turn) == 0;
            _rig.TeleportEnabled = IsTeleportAllowed();
            _rig.SetGrabbingEnabled(IsGrabbingAllowed());
        }

        private bool IsTeleportAllowed()
        {
            return _isTeleportAllowedHere && (_blockedControls & PlayerControls.Teleport) == 0;
        }

        private bool IsGrabbingAllowed()
        {
            return _isGrabbingAllowedHere && (_blockedControls & PlayerControls.Grab) == 0;
        }

        public void SetWalkingControls()
        {
            _isGrabbingAllowedHere = true;
            _rig.SetGrabbingEnabled(IsGrabbingAllowed());

            _rig.CanCrouch = true;
            _rig.CanJump = true;
            _isTeleportAllowedHere = true;
            _rig.TeleportEnabled = IsTeleportAllowed();
            _rig.StepOffset = _defaultStepOffset;
        }

        public void SetWaterWalkingControls()
        {
            _isGrabbingAllowedHere = true;
            _rig.SetGrabbingEnabled(IsGrabbingAllowed());

            _rig.CanCrouch = false;
            _rig.CanJump = false;
            _isTeleportAllowedHere = false;
            _rig.TeleportEnabled = false;
            _rig.StepOffset = _player.SwimDiveStepHeight;
        }

        public void SetSwimmingControls()
        {
            // Disable grabbing of objects (as in G1)
            _isGrabbingAllowedHere = false;
            _rig.SetGrabbingEnabled(false);

            _rig.CanCrouch = false;
            _rig.CanJump = false;
            _isTeleportAllowedHere = false;
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

        /// <summary>
        /// How many of an item the hands hold (TradeService: held currency doesn't pay).
        /// </summary>
        private int GetHeldAmount(string instanceName)
        {
            var amount = 0;
            foreach (var container in _vrPlayerService.HeldItems)
            {
                if (container.Vob is IItem item && item.GetSymbolName().EqualsIgnoreCase(instanceName))
                    amount += Mathf.Max(1, item.Amount);
            }
            return amount;
        }

        /// <summary>
        /// Like putting the held items into the backpack (VRPlayerService.StoreHeldItem).
        /// </summary>
        public void StoreHeldItemsInBackpack()
        {
            if (_rig == null)
                return;
            // A rune held with both hands is one item - stored once.
            var stored = new HashSet<VrInteractable>();
            foreach (var hand in _rig.HandGrabbers.Where(i => i.Kind == VrGrabberKind.Hand).ToList())
            {
                var interactable = hand.Held;
                if (interactable == null || !stored.Add(interactable))
                    continue;
                var container = interactable.GetComponentInParent<VobLoader>()?.Container;
                var item = container?.Vob as IItem;
                if (item == null)
                {
                    Logger.LogWarning($"[Teleport] {hand.HandSide}: {interactable.name} is no item - stays in hand.",
                        LogCat.VR);
                    continue;
                }

                // Unpaid trader goods aren't the hero's: let go, they go back to the trader (VRTradeGoods).
                if (VRTradeGoods.IsUnpaid(interactable))
                {
                    interactable.ForceRelease();
                    Logger.Log($"[Teleport] {hand.HandSide}: unpaid trader goods let go", LogCat.VR);
                    continue;
                }

                var instanceName = item.GetSymbolName();
                var amount = Mathf.Max(1, item.Amount);
                if (_vrPlayerService.StoreHeldItem(container))
                    Logger.Log($"[Teleport] {hand.HandSide}: {instanceName} x{amount} put into the backpack",
                        LogCat.VR);
            }
        }

        public void SetViewHidden(bool isHidden, GameObject visibleUi = null)
        {
            if (_rig == null)
            {
                var action = isHidden ? "hide" : "show";
                Logger.LogWarning($"[VRContextInteractionService] No VR rig to {action} the view.", LogCat.Loading);
                return;
            }

            // The fade covers everything but on-top UI. visibleUi goes on top like the game menu.
            if (isHidden && visibleUi != null)
            {
                var menuOnTop = _player.MenuHandler.GetComponentInChildren<VRUiOnTop>(true);
                if (menuOnTop != null)
                    menuOnTop.ApplyTo(visibleUi);
                else
                    Logger.LogWarning($"[VRContextInteractionService] No on-top UI on the menu, {visibleUi.name} " +
                                      "is hidden with the view.", LogCat.Loading);
            }

            _rig.Fade(isHidden ? 1f : 0f, isHidden ? float.MaxValue : _viewFadeInSpeed);
        }

        public void KeepPlayerWithin(Vector3 center, float softRadius, float radius)
        {
            var controller = _rig?.CharacterController;
            if (controller == null)
                return;

            // The frame's movement is read from the position of the previous call. After a gap (new dialog, a frame
            // without a call) there is nothing to compare against.
            // Measured at the head: a physical step moves it, but not the root. Turning rotates the root around the
            // head, so the root moves and the head doesn't.
            var position = _rig.Head.position;
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
            _keepWithinLastPosition = _rig.Head.position;
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
