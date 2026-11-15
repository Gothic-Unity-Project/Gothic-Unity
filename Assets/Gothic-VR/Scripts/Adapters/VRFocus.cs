using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Npc;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Services;
using Reflex.Attributes;
using TMPro;
using UnityEngine;

namespace Gothic.VR.Adapters
{
    /// <summary>
    /// Handles focus brightness and name visibility for VOBs and NPCs.
    /// We leverage VrInteractable's events to show canvas of object name and alter brightness on all objects'
    /// mesh renderers.
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
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly HeroPerceptionService _heroPerceptionService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private static Camera _mainCamera;

        private static bool _featureBrightenUp;
        private static bool _featureShowName;

        [SerializeField] private GameObject _nameCanvas;

        /// <summary>
        /// The hover name (with the amount) is shown right now.
        /// </summary>
        public bool IsNameShown => IsHighlighted && _nameCanvas != null && _nameCanvas.activeSelf;

        private bool _isHovered;
        // A thrown spell's target (VRPlayerService.SpellLockTarget): highlighted while locked, even when no hand hovers
        // it. Set on HeroSpellTargetLocked, dropped in LateUpdate once the service's target is another.
        private bool _isLocked;
        // An NPC's own container, cached on the first lock event.
        private NpcContainer _npcContainer;
        private float _hoverBrightness;
        private bool IsHighlighted => _isHovered || _isLocked;
        private Renderer _cachedObjectRenderer;
        // The name's size at a hand's reach - a locked target's name grows with the distance (LateUpdate).
        private Vector3 _nameCanvasScale;
        private RectTransform _nameCanvasRect;
        private bool _isNpc;
        private VrInteractable _interactable;

        private void Awake()
        {
            _isNpc = GetComponent<VRNpc>() != null;
            _nameCanvasScale = _nameCanvas.transform.localScale;
            _nameCanvasRect = _nameCanvas.transform as RectTransform;

            _interactable = VrInteractable.Find(this);
            _interactable.HoverEntered += OnHoverEnter;
            _interactable.HoverExited += OnHoverExit;
            _interactable.Grabbed += OnGrabbed;

            if (_isNpc)
                GlobalEventDispatcher.HeroSpellTargetLocked.AddListener(OnHeroSpellTargetLocked);

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
        }

        private void OnDestroy()
        {
            if (_isNpc)
                GlobalEventDispatcher.HeroSpellTargetLocked.RemoveListener(OnHeroSpellTargetLocked);

            if (_interactable == null)
                return;

            _interactable.HoverEntered -= OnHoverEnter;
            _interactable.HoverExited -= OnHoverExit;
            _interactable.Grabbed -= OnGrabbed;
        }

        /// <summary>
        /// Like the engine using a mob: grabbing it (chest lid, door, bed, wheel, ...) tells the NPCs around
        /// (PERC_ASSESSUSEMOB - owners react). VRFocus sits on every grabbable mob, VRMobInteraction not on
        /// lidded chests/doors.
        /// </summary>
        private void OnGrabbed(VrGrabArgs args)
        {
            if (!args.IsHandOrDistance)
                return;

            var vobLoader = GetComponentInParent<VobLoader>();
            if (vobLoader?.Container?.Vob is not ZenKit.Vobs.IInteractiveObject mob)
                return;

            // Using the mob itself (wheels, levers, lidless containers, ...) is VRMobInteraction's.
            _heroPerceptionService.OnHeroUsesMob(mob);
        }

        private void OnHoverEnter(VrGrabArgs _)
        {
            // The hero's focus (PlayerService.FocusNpc) - e.g. Marvin's "kill" hits the NPC the hand pointed at.
            // A thrown spell's lock (VrSpellLockDomain) owns the focus while it lasts: hovering others doesn't take it.
            if (_isNpc && !_vrPlayerService.IsSpellLockActive)
            {
                var loader = GetComponentInParent<NpcLoader>();
                if (loader != null && loader.Container != null)
                    _playerService.FocusNpc = loader.Container;
            }

            OnHoverEnter(GetDefaultBrightness());
        }

        public void OnHoverEnter(float shaderPropertyFocusBrightnessValue)
        {
            _hoverBrightness = shaderPropertyFocusBrightnessValue;
            _isHovered = true;
            ApplyHighlight();
        }

        private void OnHoverExit(VrGrabArgs _)
        {
            _isHovered = false;
            ApplyHighlight();
        }

        /// <summary>
        /// A thrown spell got locked (VrSpellLockDomain): highlight this NPC if it is the target.
        /// </summary>
        private void OnHeroSpellTargetLocked(int _)
        {
            if (!isActiveAndEnabled)
                return;

            if (_npcContainer == null)
            {
                var loader = GetComponentInParent<NpcLoader>();
                _npcContainer = loader != null ? loader.Container : null;
            }

            if (_npcContainer != null && _npcContainer == _vrPlayerService.SpellLockTarget)
                SetLocked(true);
        }

        /// <summary>
        /// The NPC is (no longer) a thrown spell's locked target: brightness and name stay while locked,
        /// whether a hand hovers it or not - brighter than a hover, the name readable at the spell's range.
        /// </summary>
        private void SetLocked(bool isLocked)
        {
            _isLocked = isLocked;
            if (!isLocked)
                _nameCanvas.transform.localScale = _nameCanvasScale;
            ApplyHighlight();
        }

        /// <summary>
        /// The one place deciding brightness and name: highlighted while hovered or locked.
        /// </summary>
        private void ApplyHighlight()
        {
            EnsureInitialized();

            if (IsHighlighted)
            {
                if (_featureBrightenUp)
                    _dynamicMaterialService.SetDynamicValue(gameObject, Constants.ShaderPropertyFocusBrightness,
                        _isLocked ? VRConstants.Magic.LockFocusBrightness : _hoverBrightness);

                if (_featureShowName)
                    SetFocusName();
                return;
            }

            if (_featureBrightenUp)
            {
                _dynamicMaterialService.ResetDynamicValue(gameObject, Constants.ShaderPropertyFocusBrightness,
                    Constants.ShaderPropertyFocusBrightnessDefault);
            }

            _nameCanvas.SetActive(false);
            _nameCanvas.transform.localScale = _nameCanvasScale;
        }

        private float GetDefaultBrightness()
        {
            return _isNpc
                ? Constants.ShaderPropertyFocusBrightnessNpcValue
                : Constants.ShaderPropertyFocusBrightnessValue;
        }

        private void EnsureInitialized()
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
        }

        private void LateUpdate()
        {
            // The lock ended (VRPlayerService owns it): a reference check, only on the one locked NPC.
            if (_isLocked && _vrPlayerService.SpellLockTarget != _npcContainer)
                SetLocked(false);

            if (!IsHighlighted)
                return;

            var cameraPosition = _mainCamera.transform.position;
            var canvasTransform = _nameCanvas.transform;
            var bounds = _cachedObjectRenderer.bounds;

            // Position canvas at the top of bounds
            var namePosition = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);

            // Locked from afar: the name keeps the size it has at LockNameFullSizeDistance and stays above the head.
            if (_isLocked)
            {
                var sizeFactor = Mathf.Max(1f,
                    Vector3.Distance(cameraPosition, namePosition) / VRConstants.Magic.LockNameFullSizeDistance);
                canvasTransform.localScale = _nameCanvasScale * sizeFactor;
                if (_nameCanvasRect != null)
                    namePosition.y += _nameCanvasRect.rect.height * canvasTransform.lossyScale.y * 0.5f *
                                      (1f - 1f / sizeFactor);
            }

            canvasTransform.position = namePosition;

            // Rotate to face camera (direction from parent object to camera)
            canvasTransform.rotation = Quaternion.LookRotation(-(cameraPosition - transform.position).normalized);
        }

        /// <summary>
        /// Reset everything (e.g. when GO is culled out.)
        /// </summary>
        private void OnDisable()
        {
            _dynamicMaterialService.ResetAllDynamicValues(gameObject);

            _isHovered = false;
            _isLocked = false;
            _nameCanvas.transform.localScale = _nameCanvasScale;
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
                _nameCanvas.GetComponentInChildren<TMP_Text>().text =
                    npcLoader.Npc.GetUserData().PrefabProps.GetFocusName();
                return;
            }
        }
    }
}
