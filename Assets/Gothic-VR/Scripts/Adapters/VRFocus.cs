#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Extensions;
using Gothic.VR.Adapters.Vob;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using LogCat = Gothic.Core.Logging.LogCat;

namespace Gothic.VR.Adapters
{
    /// <summary>
    /// Handles focus brightness and name visibility for VOBs and NPCs.
    /// We leverage HVRGrabbable's events to show canvas of object name and alter brightness on all objects' mesh renderers.
    ///
    /// Order of use is always:
    /// 1. HoverEnter -> We hover from far or near (e.g. grab distance without pulling towards us)
    /// 2. Grabbed -> Object is being Grabbed for movement/rotation
    /// 3. HoverExit -> We might still grab the object, but the Hover from our hand stops
    /// </summary>
    public class VRFocus : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly DynamicMaterialService _dynamicMaterialService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly Gothic.Core.Services.Npc.HeroPerceptionService _heroPerceptionService;
        [Inject] private readonly Gothic.Core.Services.Caches.VmCacheService _vmCacheService;

        private static Camera _mainCamera;

        private static bool _featureBrightenUp;
        private static bool _featureShowName;

        [SerializeField] private GameObject _nameCanvas;

        // The amount the shown name was made with - a stack changing while hovered (split) updates the name.
        private int _shownAmount = -1;

        /// <summary>
        /// The hover name (with the amount) is shown right now.
        /// </summary>
        public bool IsNameShown => _isHovered && _nameCanvas != null && _nameCanvas.activeSelf;

        private bool _isHovered;
        private Renderer _cachedObjectRenderer;
        private bool _mobActivated;
        private const float _npcFocusBrightness = 2.5f;
        private bool _isNpc => GetComponent<VRNpc>() != null;

        private void Awake()
        {
            // DEBUG - Use this to enable brightening up all rendered objects if you want to check the attack window.
            {
                // GlobalEventDispatcher.FightWindowInitial.AddListener(_ => OnHoverEnter(1f));
                // GlobalEventDispatcher.FightWindowAttack.AddListener(_ => OnHoverEnter(10f));
                // GlobalEventDispatcher.FightWindowWaitingForCombo.AddListener(_ => OnHoverEnter(50f));
                // GlobalEventDispatcher.FightWindowCombo.AddListener(_ => OnHoverEnter(100f));
                // GlobalEventDispatcher.FightWindowComboFailed.AddListener(_ => OnHoverEnter(0.5f));
            }
        }

        private void Start()
        {
            _nameCanvas.SetActive(false);
            var grabbable = GetComponent<HVRGrabbable>();
            grabbable?.Grabbed.AddListener(OnGrabbed);
            grabbable?.Released.AddListener(OnReleased);

            // V1: mobs used with a melee tool (e.g. G2 treasure X marks + pickaxe) can be dug up by hitting them.
            if (_configService.Dev.EnableDigSpots &&
                VRDigSpot.IsDigSpot(GetComponentInParent<VobLoader>()?.Container, _vmCacheService) &&
                GetComponent<VRDigSpot>() == null)
            {
                gameObject.AddComponent<VRDigSpot>();
            }
        }

        private void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            var vobLoader = GetComponentInParent<VobLoader>();
            if (vobLoader == null || vobLoader.Container.Vob is not IInteractiveObject mob) return;

            // Like the engine using a mob: chest lids and doors too (owners react, B_AssessUseMob).
            _heroPerceptionService.OnHeroUsesMob(mob);

            if (_mobActivated) return;

            // IDoor uses HVRPhysicsDoor for interaction — skip the mover-trigger path.
            // Beds are oCMobDoors too (BED*/BEDHIGH* visuals), but get used like other mobs (SLEEPABIT_S1).
            if (vobLoader.Container.Vob is IDoor && !IsBed(vobLoader.Container.Vob)) return;

            _mobActivated = true;
            Logger.Log($"[VRFocus.OnGrabbed] mob={vobLoader.gameObject.name}", LogCat.Ai);
            _vrPlayerService.HandleMobGrab(vobLoader);
        }

        private static bool IsBed(IVirtualObject vob)
        {
            var visualName = vob.Visual?.Name;
            return !string.IsNullOrEmpty(visualName) && visualName.StartsWithIgnoreCase("BED");
        }

        private void OnReleased(HVRGrabberBase _, HVRGrabbable __)
        {
            _mobActivated = false;
        }

        public void OnHoverEnter(HVRGrabberBase _, HVRGrabbable __)
        {
            // The item brightness made highlighted NPCs glow comically - their textures are much brighter.
            OnHoverEnter(_isNpc ? _npcFocusBrightness : Constants.ShaderPropertyFocusBrightnessValue);
        }

        public void OnHoverEnter(float shaderPropertyFocusBrightnessValue)
        {
            // GameObjects are loaded while Loading.scene is active (different Camera), but not the General.scene.
            // We therefore need to set the camera at this time earliest.
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;

                // Features also need to be fetched once only.
                _featureBrightenUp = _configService.Dev.BrightenUpHoveredVOBs;
                _featureShowName = _configService.Dev.ShowNamesOnHoveredVOBs;
            }

            if (_cachedObjectRenderer == null)
                _cachedObjectRenderer = GetComponentInChildren<Renderer>();

            if (_featureBrightenUp)
                _dynamicMaterialService.SetDynamicValue(gameObject, Constants.ShaderPropertyFocusBrightness, shaderPropertyFocusBrightnessValue);

            if (_featureShowName)
                SetFocusName();

            _isHovered = true;
        }

        public void OnHoverExit(HVRGrabberBase _, HVRGrabbable __)
        {
            if (_featureBrightenUp)
            {
                _dynamicMaterialService.ResetDynamicValue(gameObject, Constants.ShaderPropertyFocusBrightness, Constants.ShaderPropertyFocusBrightnessDefault);
            }

            _nameCanvas.SetActive(false);
            _isHovered = false;
        }

        private void OnEnable()
        {
            Application.onBeforeRender += UpdateNameCanvas;
        }

        /// <summary>
        /// Runs after every LateUpdate: sitting NPCs get their BIP01 yaw inverted in AnimationSystem.LateUpdate
        /// (_isSittingInverted). Placed in our own LateUpdate, the label was turned with that bone afterwards and
        /// showed its mirrored back side.
        /// </summary>
        private void UpdateNameCanvas()
        {
            if (!_isHovered || _nameCanvas == null || _cachedObjectRenderer == null)
                return;

            if (_nameCanvas.activeSelf && GetShownItemAmount() != _shownAmount)
                SetFocusName();

            // Calculate direction from parent object to camera
            var directionToCamera = (_mainCamera.transform.position - transform.position).normalized;

            // Position canvas at the top of bounds, shifted toward camera
            _nameCanvas.transform.position = new Vector3(
                _cachedObjectRenderer.bounds.center.x,
                _cachedObjectRenderer.bounds.max.y,
                _cachedObjectRenderer.bounds.center.z
            );

            // Rotate to face camera
            _nameCanvas.transform.rotation = Quaternion.LookRotation(-directionToCamera);
        }

        /// <summary>
        /// Reset everything (e.g. when GO is culled out.)
        /// </summary>
        private void OnDisable()
        {
            Application.onBeforeRender -= UpdateNameCanvas;
            _dynamicMaterialService.ResetAllDynamicValues(gameObject);

            _isHovered = false;
        }

        private int GetShownItemAmount()
        {
            return GetComponentInParent<VobLoader>()?.Container?.Vob is ZenKit.Vobs.IItem item ? item.Amount : -1;
        }

        private void SetFocusName()
        {
            _nameCanvas.SetActive(true);

            var vobContainer = GetComponentInParent<VobLoader>()?.Container;
            if (vobContainer != null)
            {
                _nameCanvas.GetComponentInChildren<TMP_Text>().text = vobContainer.Props.GetFocusName();
                _shownAmount = GetShownItemAmount();
                return;
            }

            var npcLoader = GetComponentInParent<NpcLoader>();
            if (npcLoader != null)
            {
                _nameCanvas.GetComponentInChildren<TMP_Text>().text = npcLoader.Npc.GetUserData().PrefabProps.GetFocusName();
                return;
            }
        }
    }
}
#endif
