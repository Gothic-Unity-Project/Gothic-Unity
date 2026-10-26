using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Services;
using Gothic.VR.Adapters.Vob;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Services;
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
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly Gothic.Core.Services.Player.PlayerService _playerService;
        [Inject] private readonly Gothic.Core.Services.Npc.HeroPerceptionService _heroPerceptionService;

        private static Camera _mainCamera;

        private static bool _featureBrightenUp;
        private static bool _featureShowName;

        [SerializeField] private GameObject _nameCanvas;

        private const string _nameTextName = "NameText";
        private TMP_Text _nameText;

        // The amount the shown name was made with - a stack changing while hovered (split) updates the name.
        private int _shownAmount = -1;

        /// <summary>
        /// The hover name (with the amount) is shown right now.
        /// </summary>
        public bool IsNameShown => _isHovered && _nameCanvas != null && _nameCanvas.activeSelf;

        private bool _isHovered;
        private Renderer _cachedObjectRenderer;
        private bool _mobActivated;
        private bool _isNpc;
        private VrInteractable _interactable;

        private void Awake()
        {
            _isNpc = GetComponent<VRNpc>() != null;

            _interactable = VrInteractable.Find(this);
            _interactable.HoverEntered += OnHoverEnter;
            _interactable.HoverExited += OnHoverExit;

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
            if (_interactable == null)
                return;
            _interactable.Grabbed += OnGrabbed;
            _interactable.Released += OnReleased;
            RefreshGrabColliders();
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            if (!args.IsHandOrDistance)
                return;

            var vobLoader = GetComponentInParent<VobLoader>();
            if (vobLoader == null || vobLoader.Container.Vob is not IInteractiveObject mob) return;

            // Like the engine using a mob: chest lids and doors too (owners react, B_AssessUseMob).
            _heroPerceptionService.OnHeroUsesMob(mob);

            // Using the mob itself (wheels, levers, shrines, beds, ...) is VRMobInteraction's - containers aren't.
            if (_mobActivated || mob is not IContainer container)
                return;

            _mobActivated = true;
            _vrPlayerService.TryToggleStaticContainerLoot(vobLoader, container);
        }

        private void OnReleased(VrGrabArgs _)
        {
            _mobActivated = false;
        }

        private void OnDestroy()
        {
            if (_interactable == null)
                return;

            _interactable.HoverEntered -= OnHoverEnter;
            _interactable.HoverExited -= OnHoverExit;
            _interactable.Grabbed -= OnGrabbed;
            _interactable.Released -= OnReleased;
        }

        private void OnHoverEnter(VrGrabArgs _)
        {
            // The hero's focus (PlayerService.FocusNpc) - e.g. Marvin's "kill" hits the NPC the hand pointed at.
            if (_isNpc && _configService.Dev.EnableMarvinKill)
            {
                var loader = GetComponentInParent<NpcLoader>();
                var npc = loader != null && loader.Npc != null ? loader.Container : null;
                if (npc != null)
                    _playerService.FocusNpc = npc;
            }


            OnHoverEnter(_isNpc
                ? Constants.ShaderPropertyFocusBrightnessNpcValue
                : Constants.ShaderPropertyFocusBrightnessValue);
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

        private void OnHoverExit(VrGrabArgs _)
        {
            if (_featureBrightenUp)
            {
                _dynamicMaterialService.ResetDynamicValue(gameObject, Constants.ShaderPropertyFocusBrightness, Constants.ShaderPropertyFocusBrightnessDefault);
            }

            _nameCanvas.SetActive(false);
            _isHovered = false;
        }

        /// <summary>
        /// DeveloperConfig.EnableMobColliderRefresh: the grabbable collects its colliders in Awake - when the prefab is
        /// instantiated, before the mesh is built. Mobs whose mesh sits on a child bone (G2 Irdorath's touchplates:
        /// BIP01 TOUCHPLATE -> BIP01 PLATE) only had the prefab's empty MeshCollider, nothing to hover or grab.
        /// </summary>
        private void RefreshGrabColliders()
        {
            if (_isNpc || GetComponent<VRVobItem>() != null || !_configService.Dev.EnableMobColliderRefresh)
                return;

            var before = _interactable.Colliders.Count;
            _interactable.SetupColliders(isChildLinked: true);
            if (_interactable.Colliders.Count == before)
                return;

            Logger.Log($"[VRFocus] '{name}' grab colliders {before} -> {_interactable.Colliders.Count} (mesh built " +
                       "after the grabbable's Awake)", LogCat.VR);
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

        /// <summary>
        /// The canvas can hold more texts (e.g. HP numbers on the health bar) - the name goes into >NameText<.
        /// </summary>
        private TMP_Text GetNameText()
        {
            if (_nameText != null)
                return _nameText;

            var nameTextTransform = _nameCanvas.transform.FindChildRecursively(_nameTextName);
            if (nameTextTransform != null)
                _nameText = nameTextTransform.GetComponent<TMP_Text>();
            if (_nameText == null)
                _nameText = _nameCanvas.GetComponentInChildren<TMP_Text>();
            return _nameText;
        }

        private void SetFocusName()
        {
            _nameCanvas.SetActive(true);

            var vobContainer = GetComponentInParent<VobLoader>()?.Container;
            if (vobContainer != null)
            {
                GetNameText().text = vobContainer.Props.GetFocusName();
                _shownAmount = GetShownItemAmount();
                return;
            }

            var npcLoader = GetComponentInParent<NpcLoader>();
            if (npcLoader != null)
            {
                GetNameText().text = npcLoader.Npc.GetUserData().PrefabProps.GetFocusName();
                return;
            }
        }
    }
}
