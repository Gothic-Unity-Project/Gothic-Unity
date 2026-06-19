#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Extensions;
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

        private static Camera _mainCamera;

        private static bool _featureBrightenUp;
        private static bool _featureShowName;

        [SerializeField] private GameObject _nameCanvas;

        private bool _isHovered;
        private Renderer _cachedObjectRenderer;
        private bool _mobActivated;

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
        }

        private void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_mobActivated) return;
            var vobLoader = GetComponentInParent<VobLoader>();
            if (vobLoader?.Container.VobAs<IInteractiveObject>() == null) return;

            _mobActivated = true;
            Logger.Log($"[VRFocus.OnGrabbed] mob={vobLoader.gameObject.name}", LogCat.Ai);
            _vrPlayerService.HandleMobGrab(vobLoader);
        }

        private void OnReleased(HVRGrabberBase _, HVRGrabbable __)
        {
            _mobActivated = false;
        }

        public void OnHoverEnter(HVRGrabberBase _, HVRGrabbable __)
        {
            OnHoverEnter(Constants.ShaderPropertyFocusBrightnessValue);
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

        private void LateUpdate()
        {
            if (!_isHovered)
                return;

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
            _dynamicMaterialService.ResetAllDynamicValues(gameObject);

            _isHovered = false;
        }

        private void SetFocusName()
        {
            _nameCanvas.SetActive(true);

            var vobContainer = GetComponentInParent<VobLoader>()?.Container;
            if (vobContainer != null)
            {
                _nameCanvas.GetComponentInChildren<TMP_Text>().text = vobContainer.Props.GetFocusName();
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
