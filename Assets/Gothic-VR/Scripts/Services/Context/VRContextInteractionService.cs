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
using Gothic.Core.Services.Player;
using Gothic.VR.Adapters.Marvin;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Adapters.UI;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using ZenKit.Vobs;
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
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly VobService _vobService;
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

            // DeveloperConfig.EnableVrHeroBody: the hero's own body + arms under the VR head (one per world scene).
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(() =>
            {
                if (_configService.Dev.EnableVrHeroBody)
                    new UnityEngine.GameObject("_VRHeroBodyController").AddComponent<VRHeroBody>();
                // DeveloperConfig.EnableStackSplit (checked at runtime): taking pieces off held stacks.
                new UnityEngine.GameObject("_VRStackSplitter").AddComponent<Gothic.VR.Adapters.Vob.VobItem.VRStackSplitter>();
                // DeveloperConfig.EnableHeroPersonalSpace (checked at runtime): NPC bodies stay out of the camera.
                new UnityEngine.GameObject("_HeroPersonalSpace").AddComponent<Gothic.Core.Adapters.Npc.HeroPersonalSpace>();
                ApplyHudOnTop();
                // DeveloperConfig.HandLightInfluence: skin colored hands, lights tint them only a little.
                VRHandSkinLight.Apply(_configService);
                // DeveloperConfig.EnableMarvinFallRescue (checked at runtime, Marvin mode only): out of the world -> START.
                new UnityEngine.GameObject("_MarvinFallRescue").AddComponent<Gothic.VR.Adapters.Marvin.MarvinFallRescue>();
            });
            // DeveloperConfig.EnableVrTrade: the trade counter between the hero and the trader.
            GlobalEventDispatcher.TradeStarted.AddListener(session =>
                Gothic.VR.Adapters.Trade.VRTradeCounter.Spawn(session, _rig.Head));
            
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(OnLoadingSceneLoaded);
            GlobalEventDispatcher.GothicInisInitialized.AddListener(() =>
            {
                SetRenderDistance(_configService.Gothic.IniVisualRange);
                ApplyComfortSettings();
                ApplyPhysicalHands();
                ApplyMarvinVisibility();
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
            if (_configService.EffectiveEnableVRDeviceSimulator || XRGeneralSettings.Instance.Manager.activeLoader == null)
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

            // VR Player: Gothic part and the backend's rig live on the same GameObject. The scene contains one
            // (inactive) player prefab per VR backend. We use the one of the active backend and remove the others, so
            // that scene-wide lookups (dialog, canvases, ...) find only ours.
            var players = activeScene.GetRootGameObjects()
                .SelectMany(i => i.GetComponentsInChildren<VRPlayer>(true))
                .ToList();
            _player = players.FirstOrDefault(i => _vrBackend.OwnsRig(i.GetComponent<IVrRig>()));
            if (_player == null)
            {
                var message = $"No VR player with a rig of the active VR backend ({_vrBackend.Name}) found. " +
                              "Check DeveloperConfig.VrInteractionBackend and the player prefabs in Player.unity.";
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

            // XRDeviceSimulator (Editor: DeveloperConfig, builds: GameSettings.json)
            var isSimulatorEnabled = _configService.EffectiveEnableVRDeviceSimulator;
            if (isSimulatorEnabled)
                StopXRForSimulator();
            _vrInput.SetDeviceSimulatorActive(isSimulatorEnabled);

            // Marvin Mode
            // Searched inside our player only: the other backends' players are destroyed at the end of this frame.
            _marvinRoot = playerRoot.GetComponentInChildren<MarvinRootHandler>(true)!.gameObject;
            _marvinRoot.SetActive(_marvinService.IsMarvinMode);
            if (developerConfig.EnableUiGrabBars)
                AddMarvinGrabBar();

            // DeveloperConfig.EnableVrSpellRing: the favorites ring, in the player's space - it moves with him.
            if (developerConfig.EnableVrSpellRing)
            {
                var favoritesRing = new GameObject("_FavoritesRing").AddComponent<VRFavoritesRing>();
                favoritesRing.transform.SetParent(_player.transform, false);
            }

            // DeveloperConfig.EnableVrPickpocket: purses on the hips of NPCs who can't see the sneaking hero (G1).
            if (developerConfig.EnableVrPickpocket)
            {
                var pickpocket = new GameObject("_Pickpocket").AddComponent<VRPickpocket>();
                pickpocket.transform.SetParent(_player.transform, false);
            }

            // DeveloperConfig.EnableVrHeroBlock: both hands on a melee weapon + both triggers = parry.
            if (developerConfig.EnableVrHeroBlock)
            {
                var heroBlock = new GameObject("_HeroBlock").AddComponent<VRHeroBlock>();
                heroBlock.transform.SetParent(_player.transform, false);
            }

            // DeveloperConfig.EnableVisualFxEarthquake: FX_EarthQuake rumbles the controllers.
            if (developerConfig.EnableVisualFxEarthquake)
            {
                var earthquakeRumble = new GameObject("_EarthquakeRumble").AddComponent<VREarthquakeRumble>();
                earthquakeRumble.transform.SetParent(_player.transform, false);
            }

            // DeveloperConfig.EnableBackpackEquipToggle: a trigger click at a worn item in the backpack equips it.
            if (developerConfig.EnableBackpackEquipToggle)
            {
                var equipToggle = new GameObject("_BackpackEquipToggle").AddComponent<VRBackpackEquipToggle>();
                equipToggle.transform.SetParent(_player.transform, false);
            }

            // DeveloperConfig.EnableWeaponGripGrab: the first hand on a weapon closes around its grip.
            if (developerConfig.EnableWeaponGripGrab)
            {
                var gripGrab = new GameObject("_WeaponGripGrab").AddComponent<VRWeaponGripGrab>();
                gripGrab.transform.SetParent(_player.transform, false);
            }
            ApplyMarvinVisibility();

            // The hero's walk/run/sneak state, his footsteps and the speed potion's faster movement (any backend).
            _player.gameObject.AddComponent<VRHeroMovement>();

            // PrintScreen() messages in front of the camera
            activeScene.GetComponentInChildren<VRPrintScreen>(true)!.Init();
        }

        /// <summary>
        /// DeveloperConfig.EnableUiGrabBars: grabbed, the Marvin panel leaves the left hand and stays where it's put
        /// in the player's space (it moves and turns with him) - one element with its keyboard (it follows the panel's
        /// KeyboardAnchor), so the bar is upright at the side. Released in the sphere above the left hand, it docks back.
        /// </summary>
        private void AddMarvinGrabBar()
        {
            var panel = _marvinRoot.transform;
            var hand = panel.parent;
            var dockedPosition = panel.localPosition;
            var dockedRotation = panel.localRotation;
            var dockedScale = panel.localScale;
            var playerSpace = _player.transform;

            var bar = VRGrabBar.Create((RectTransform)panel, panel, true,
                () =>
                {
                    if (panel.parent != playerSpace)
                        panel.SetParent(playerSpace, true);
                },
                hand,
                () =>
                {
                    panel.SetParent(hand, false);
                    panel.SetLocalPositionAndRotation(dockedPosition, dockedRotation);
                    panel.localScale = dockedScale;
                    Logger.Log("[VRContextInteractionService] Marvin panel docked to the hand", LogCat.VR);
                });

            // Centered where the panel meets its keyboard (KeyboardAnchor below the panel) - one bar for both.
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = barRect.anchorMax = new Vector2(1f, 0f);
            barRect.sizeDelta = new Vector2(barRect.sizeDelta.x, ((RectTransform)panel).rect.height);
        }

        private void OnMarvinModeChanged(bool isActive)
        {
            ApplyMarvinVisibility();
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
        
        /// <summary>
        /// Builds start XR on startup. With a headset (or SteamVR) running, the headset would still drive the camera
        /// next to the simulator - stop XR so mouse and keyboard own the rig and the window shows the game.
        /// </summary>
        private void StopXRForSimulator()
        {
            var settings = XRGeneralSettings.Instance;
            var manager = settings != null ? settings.Manager : null;
            if (manager == null || manager.activeLoader == null)
                return;

            manager.StopSubsystems();
            manager.DeinitializeLoader();
            Logger.Log("[VRDeviceSimulator] XR stopped, the simulator drives the player.", LogCat.VR);
        }

        private void PlayerPrefsUpdated(string key, object value)
        {
            if (key == GothicIniConfig.IniKeyVisualRange)
            {
                SetRenderDistance(int.Parse((string)value));
            }
            else if (key == VRConstants.IniNames.MarvinPanel)
            {
                ApplyMarvinVisibility();
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
        /// DeveloperConfig.EnableHudAlwaysOnTop: the hero's bars (on the wrists - the other hand and the world cut
        /// through them) draw on top of everything, like the dialog box. The menus have their VRUiOnTop in the prefab,
        /// it is the template for the bars' materials.
        /// </summary>
        private void ApplyHudOnTop()
        {
            if (!_configService.Dev.EnableHudAlwaysOnTop)
                return;

            var template = Object.FindFirstObjectByType<VRUiOnTop>(FindObjectsInactive.Include);
            if (template == null)
            {
                Logger.LogWarning("[VRUiOnTop] No on-top UI in the scene to copy - status bars stay depth tested.",
                    LogCat.VR);
                return;
            }

            var roots = new System.Collections.Generic.HashSet<GameObject>();
            foreach (var bar in Object.FindObjectsByType<Gothic.Core.Adapters.UI.StatusBars.StatusBarAdapter>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (bar.IsPlayer)
                    roots.Add(GetRootCanvas(bar.transform) ?? bar.gameObject);
            }
            foreach (var root in roots)
            {
                UseMaterialTextureAsSprite(root);
                template.CopyTo(root);
            }

            Logger.Log($"[VRUiOnTop] HUD drawn on top: {string.Join(", ", roots.Select(r => r.name))}", LogCat.VR);
        }

        /// <summary>
        /// The bars' fill is an Image with a white sprite and the Gothic texture (BAR_HEALTH.TGA) in its URP material.
        /// The on-top shader samples the Canvas texture - the sprite's - so the fill came out white. A sprite of the
        /// material's texture gives the Canvas the right one (URP keeps sampling the material either way).
        /// </summary>
        private static void UseMaterialTextureAsSprite(GameObject root)
        {
            foreach (var image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                var texture = image.material != null ? image.material.mainTexture as Texture2D : null;
                if (texture == null || image.sprite == null || image.sprite.texture == texture)
                    continue;

                image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                Logger.Log($"[VRUiOnTop] {image.name}: sprite from its material's texture '{texture.name}'", LogCat.VR);
            }
        }

        private static GameObject GetRootCanvas(Transform transform)
        {
            GameObject root = null;
            for (var current = transform; current != null; current = current.parent)
            {
                if (current.TryGetComponent<Canvas>(out _))
                    root = current.gameObject;
            }
            return root;
        }

        /// <summary>
        /// Our Immersion option "Marvin" (Gothic.ini, on by default) hides/shows the Marvin panel - only while Marvin
        /// mode is on (MarvinService.IsMarvinMode). The panel can't do it itself: it is inactive then.
        /// </summary>
        private void ApplyMarvinVisibility()
        {
            if (_marvinRoot == null || _configService.Gothic == null)
                return;

            var isShown = _marvinService.IsMarvinMode &&
                          _configService.Gothic.GetInt(VRConstants.IniNames.MarvinPanel, 1) == 1;
            _marvinRoot.SetActive(isShown);
            Logger.Log($"[VRContextInteractionService] Marvin panel {(isShown ? "shown" : "hidden")}", LogCat.VR);
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
        public void LockPlayerInPlace(bool allowTurning = false)
        {
            // HINT: Disable physics
            // We can't disable physics as it would prevent the teleport from finishing (HVR checks for IsGrounded
            // every frame). Therefore, we need to ground the player always on a plane and disable movement only.
            _rig.MovementEnabled = false;
            _rig.TurnEnabled = allowTurning;
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
            // DeveloperConfig.EnableTeleportKeepsHeldItems: what the hands hold goes into the backpack first - a held
            // rune stayed at the old place (or vanished) and the hero stayed in magic mode.
            if (_configService.Dev.EnableTeleportKeepsHeldItems)
                StoreHeldItemsInBackpack();

            _rig.Teleport(position, rotation);
        }

        /// <summary>
        /// Like putting the held items into the backpack (see VRItemUser.Equip): release, add to the inventory, remove
        /// the world item.
        /// </summary>
        public void StoreHeldItemsInBackpack()
        {
            // A rune held with both hands is one item - stored once.
            var stored = new System.Collections.Generic.HashSet<VrInteractable>();
            foreach (var hand in _rig.HandGrabbers.Where(i => i.Kind == VrGrabberKind.Hand).ToList())
            {
                var interactable = hand.Held;
                if (interactable == null || !stored.Add(interactable))
                    continue;
                var container = interactable.GetComponentInParent<VobLoader>()?.Container;
                var item = container?.VobAs<IItem>();
                if (item == null)
                    continue;

                // Items held in a hand already count as inventory - releasing takes them out again (no duplicates).
                interactable.ForceRelease();

                var instanceName = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
                _saveGameService.UntrackLooseItem(container);
                _playerService.AddItem(instanceName, Mathf.Max(1, item.Amount));
                _vobService.RemoveWorldItem(container);
                Logger.Log($"[Teleport] {hand.HandSide}: {instanceName} x{Mathf.Max(1, item.Amount)} put into the backpack",
                    LogCat.VR);
            }
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
