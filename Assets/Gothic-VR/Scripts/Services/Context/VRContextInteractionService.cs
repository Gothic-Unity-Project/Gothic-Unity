#if GOTHIC_HVR_INSTALLED
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
using Gothic.VR.Adapters.HVROverrides;
using Gothic.VR.Adapters.Marvin;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Adapters.UI;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using ZenKit.Vobs;
using HurricaneVR.Framework.Core.UI;
using HurricaneVRExtensions.Simulator;
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
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VideoService _videoService;
        [Inject] private readonly MarvinService _marvinService;

        private const string _contextName = "VR";
        // Meters per second the player is pulled back when they ended up beyond KeepPlayerWithin's radius.
        private const float _keepWithinPullBackSpeed = 1f;
        // Share of the normal speed left at KeepPlayerWithin's radius for walking back in or sideways.
        private const float _keepWithinMinSpeed = 0.25f;

        private VRPlayerController _playerController;
        private GameObject _marvinRoot;
        private int _keepWithinLastFrame = -1;
        private Vector3 _keepWithinLastPosition;

        public VRContextInteractionService()
        {
            this.Inject();

            _vrWeaponService.Init();

            GlobalEventDispatcher.ScriptPlayVideo.AddListener(fileName => VRCinema.Play(_videoService, fileName));
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
            GlobalEventDispatcher.ScriptExitGame.AddListener(VRCinema.QuitAfterVideos);
            // DeveloperConfig.EnableVrTrade: the trade counter between the hero and the trader.
            GlobalEventDispatcher.TradeStarted.AddListener(session =>
                Gothic.VR.Adapters.Trade.VRTradeCounter.Spawn(session, _playerController.Camera));
            
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(OnLoadingSceneLoaded);
            GlobalEventDispatcher.GothicInisInitialized.AddListener(() =>
            {
                SetRenderDistance(_configService.Gothic.IniVisualRange);
                _playerController.SetNormalControls();
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
            _playerController.MenuHandler.gameObject.SetActive(false);
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

            // HVR Player
            _playerController = activeScene.GetComponentInChildren<VRPlayerController>(true)!;
            _playerController.transform.parent.parent.gameObject.SetActive(true);
            _playerController.SetNormalControls(true);

            // XRDeviceSimulator (Editor: DeveloperConfig, builds: GameSettings.json)
            var isSimulatorEnabled = _configService.EffectiveEnableVRDeviceSimulator;
            if (isSimulatorEnabled)
                StopXRForSimulator();
            var simulatorGO = activeScene.GetComponentInChildren<HVRBodySimulator>(true)!;
            simulatorGO.gameObject.SetActive(isSimulatorEnabled);

            // Marvin Mode
            _marvinRoot = activeScene.GetComponentInChildren<MarvinRootHandler>(true)!.gameObject;
            ApplyMarvinVisibility();

            // PrintScreen() messages in front of the camera
            activeScene.GetComponentInChildren<VRPrintScreen>(true)!.Init();
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

            var pointer = HVRInputModule.Instance.Pointers.FirstOrDefault(i => i.PointerEventData == eventData);
            if (pointer == null)
            {
                Logger.LogWarning("[VRContextInteractionService] No VR pointer found for the Marvin logo click.",
                    LogCat.Ui);
                return;
            }

            ReflexProjectInstaller.DIContainer.Resolve<VrHapticsService>()
                .Vibrate(pointer.HandSide, VrHapticsService.VibrationType.Info);
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
        }

        /// <summary>
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

        private void SetRenderDistance(int value)
        {
            // Starting with value=0 (20%) and ending with value=14 (300%)
            _playerController.Camera.GetComponent<Camera>().farClipPlane = GothicIniConfig.IniVisualRangeFactor * (value + 1);
        }

        public GameObject GetCurrentPlayerController()
        {
            return _playerController.gameObject;
        }

        public VRPlayerController GetVRPlayerController()
        {
            return _playerController;
        }

        public VRPlayerInputs GetVRPlayerInputs()
        {
            return _playerController.VrInputs;
        }

        public void LockPlayerInPlace(bool allowTurning = false)
        {
            _playerController.SetLockedControls(allowTurning);
        }

        public void UnlockPlayer()
        {
            _playerController.SetUnlockedControls();
        }

        public void SetWalkingControls()
        {
            _playerController.SetWalkingControls();
        }

        public void SetWaterWalkingControls()
        {
            _playerController.SetWaterWalkingControls();
        }

        public void SetSwimmingControls()
        {
            _playerController.SetSwimmingControls();
        }

        public void SetDivingControls()
        {
            _playerController.SetDivingControls();
        }

        public void TeleportPlayerTo(Vector3 position, Quaternion rotation = default)
        {
            // DeveloperConfig.EnableTeleportKeepsHeldItems: what the hands hold goes into the backpack first - a held
            // rune stayed at the old place (or vanished) and the hero stayed in magic mode.
            if (_configService.Dev.EnableTeleportKeepsHeldItems)
                StoreHeldItemsInBackpack();

            _playerController.Teleporter.Teleport(position);

            // Changing the rotation inside HVRTeleporter didn't work for y-axis. Therefore, setting it now.
            _playerController.transform.rotation = rotation;
        }

        /// <summary>
        /// Like putting the held items into the backpack (see VRItemUser.Equip): release, add to the inventory, remove
        /// the world item.
        /// </summary>
        public void StoreHeldItemsInBackpack()
        {
            // A rune held with both hands is one item - stored once.
            var stored = new System.Collections.Generic.HashSet<Object>();
            foreach (var hand in new[] { _playerController.LeftHand, _playerController.RightHand })
            {
                var grabbable = hand != null ? hand.GrabbedTarget : null;
                if (grabbable == null || !stored.Add(grabbable))
                    continue;
                var container = grabbable != null ? grabbable.GetComponentInParent<VobLoader>()?.Container : null;
                var item = container?.VobAs<IItem>();
                if (item == null)
                    continue;

                // Items held in a hand already count as inventory - releasing takes them out again (no duplicates).
                grabbable.ForceRelease();

                var instanceName = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
                _saveGameService.UntrackLooseItem(container);
                _playerService.AddItem(instanceName, Mathf.Max(1, item.Amount));
                _vobService.RemoveWorldItem(container);
                Logger.Log($"[Teleport] {hand.name}: {instanceName} x{Mathf.Max(1, item.Amount)} put into the backpack",
                    LogCat.VR);
            }
        }

        public void KeepPlayerWithin(Vector3 center, float softRadius, float radius)
        {
            var controller = _playerController != null ? _playerController.CharacterController : null;
            if (controller == null)
                return;

            // The frame's movement is read from the position of the previous call. After a gap (new dialog, a frame
            // without a call) there is nothing to compare against.
            var position = _playerController.transform.position;
            var frameMovement = _keepWithinLastFrame == Time.frameCount - 1
                ? position - _keepWithinLastPosition
                : Vector3.zero;
            _keepWithinLastFrame = Time.frameCount;
            _keepWithinLastPosition = position;

            // Falling or on a ladder: a sideways push would fight gravity/the climbing hands.
            if (!_playerController.IsGrounded || _playerController.IsClimbing)
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
            _keepWithinLastPosition = _playerController.transform.position;
        }

        public void InitUIInteraction()
        {
            // Find all ui canvases and add to HVR Input module (To activate red laser pointer for clicking/grabbing)
            // WARNING: As it leverages FindObjectsOfType which looks through all opened scenes trees, this can become quite slow. Execute very carefully!
            var allCanvases = Object.FindObjectsOfType<Canvas>(true);
            HVRInputModule.Instance.UICanvases = allCanvases.ToList();
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
            _playerController.VrInputs.IsMenuButtonEnabled = false;
        }

        public void EnableMenus()
        {
            _playerController.VrInputs.IsMenuButtonEnabled = true;
        }
    }
}
#endif
