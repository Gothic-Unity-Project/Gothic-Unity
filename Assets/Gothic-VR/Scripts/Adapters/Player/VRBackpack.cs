using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Models.Context;
using Gothic.VR.Adapters.Trade;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The player's backpack (inventory pages) and holsters. The backpack itself is a VrInteractable with an
    /// IVrSocketContainer for its slots. Items put into holsters or slots are added to the hero's inventory.
    /// While the backpack is on the shoulder, its slots detect nothing (VrSocketSlot; HVR does it itself).
    /// The same prefab is spawned as an NPC's loot backpack (SetNpcOwner) - tinted, showing and changing the NPC's
    /// items - and as a trader's goods (SetTradeOwner).
    /// </summary>
    public class VRBackpack : MonoBehaviour
    {
        [SerializeField] private TMP_Text _categoryText;
        [SerializeField] private TMP_Text _pagerText;

        [Tooltip("Hip and shoulder holsters (IVrSocket). Items inside count as inventory.")]
        [SerializeField] private GameObject[] _holsterSockets;

        [Tooltip("Backpack slots (IVrSocket) which add/remove items to/from the inventory when used.")]
        [SerializeField] private GameObject[] _inventorySockets;

        private VrInteractable _interactable;
        private IVrSocketContainer _socketContainer;
        private readonly List<IVrSocket> _holsters = new();
        private readonly List<IVrSocket> _slots = new();

        private bool _isZenKitBootstrapped;
        private int _currentPage = 1;
        private int _totalPages;
        private VmGothicEnums.InvCats _selectedCategory =  VmGothicEnums.InvCats.InvWeapon;
        private bool _tempIgnoreSocketing;
        // Quitting: the slots let go of their items while the DI containers are gone already - nothing to count.
        private static bool _isQuitting;
        private IInventoryOwner _owner;
        private NpcContainer _trader;
        private Coroutine _refresh;
        private bool _isRefreshDirty;
        private static readonly Color _npcBackpackTint = new(0.55f, 0.75f, 1f);

        // While the page is re-stacked, the slot events of the items we take out and put in ourselves are ignored.
        // Otherwise, we create a loop. Items the player moves meanwhile still count.
        private readonly HashSet<VrInteractable> _refreshRemovals = new();
        private readonly HashSet<VrInteractable> _refreshPlacements = new();
        private int _runningRefreshes;
        
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly ConfigService _configService;

        // DeveloperConfig.EnableLootTakeAll: the green "X" above the right page arrow of a loot backpack.
        private const string _takeAllButtonName = "TakeAllButton";
        private static readonly Color _takeAllColor = new(0.2f, 0.85f, 0.25f);
        private GameObject _takeAllButton;

        private const string _equippedLabelName = "EquippedLabel";

        private IInventoryOwner Owner => _owner ??= new PlayerInventoryOwner();

        public bool IsNpcBackpack => _owner != null && !_owner.IsHero;

        // An NPC owner fires NpcInventoryChanged on every change; a container (mummy) has no NPC - refresh ourselves.
        private bool IsRefreshedByNpcEvent => IsNpcBackpack && _owner.Npc != null;

        /// <summary>
        /// A trader's goods during a trade (VRTradeCounter): items taken out are VRTradeGoods until paid.
        /// </summary>
        public bool IsTradeBackpack => _trader != null;

        private const string _prefabPathHvr = "VR/Prefabs/Player-Elements/BackPack";
        private const string _prefabPathXri = "VR/Prefabs/Player-Elements/BackPack XRI";

        /// <summary>
        /// Resources path of the active backend's backpack prefab (copies for NPC loot, trader goods, purses).
        /// </summary>
        public static string PrefabPath =>
            ReflexProjectInstaller.DIContainer?.Resolve<IVrBackend>()?.Kind == VrInteractionBackend.Xri
                ? _prefabPathXri
                : _prefabPathHvr;

        /// <summary>
        /// A copy of the hero's backpack prefab (NPC loot, trader goods). spawnedRoot is the prefab root to destroy.
        /// </summary>
        public static VRBackpack SpawnCopy(Vector3 position, Quaternion rotation, string goName,
            out GameObject spawnedRoot)
        {
            spawnedRoot = null;
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Gothic.Core.Logging.Logger.LogWarning($"[VRBackpack] Prefab {PrefabPath} not found.",
                    Gothic.Core.Logging.LogCat.VR);
                return null;
            }

            spawnedRoot = Instantiate(prefab, position, rotation);
            spawnedRoot.name = goName;
            spawnedRoot.Inject();
            var backpack = spawnedRoot.GetComponentInChildren<VRBackpack>(true);
            if (backpack == null)
                Destroy(spawnedRoot);
            return backpack;
        }

        /// <summary>
        /// Turns this backpack into the loot backpack of an NPC (call right after spawning it).
        /// </summary>
        public void SetNpcOwner(NpcContainer npc)
        {
            SetOwner(new NpcInventoryOwner(npc));
        }

        /// <summary>
        /// Turns this backpack into the loot of a container without a lid (e.g. a mummy - VRStaticContainerLoot).
        /// </summary>
        public void SetContainerOwner(IInventoryOwner containerItems)
        {
            SetOwner(containerItems);
        }

        /// <summary>
        /// Turns this backpack into a trader's goods (without his currency and equipped items).
        /// </summary>
        public void SetTradeOwner(NpcContainer trader, IInventoryOwner traderGoods)
        {
            _trader = trader;
            SetOwner(traderGoods);
        }

        private void SetOwner(IInventoryOwner owner)
        {
            _owner = owner;
            SelectFirstFilledCategory();
            GlobalEventDispatcher.NpcInventoryChanged.AddListener(OnNpcInventoryChanged);

#if GOTHIC_HVR_INSTALLED
            // The prefab's GrabColliders hold one empty entry: that turns on HVR's grab collider filter, which then lets
            // no collider through - the hero's backpack is only taken from the shoulder socket, a loot backpack lies
            // on the ground and couldn't be grabbed (nor force grabbed).
            var grabbable = GetComponent<HurricaneVR.Framework.Core.HVRGrabbable>();
            if (grabbable != null)
            {
                grabbable.GrabColliders = System.Array.Empty<Collider>();
                grabbable.SetupGrabColliders();
            }
#endif
            Tint(_npcBackpackTint);
            RegisterUiCanvases();
            Init();
            UpdateInventoryView();
            CreateTakeAllButton();
        }

        /// <summary>
        /// DeveloperConfig.EnableLootTakeAll: loot of a dead NPC or of a container without a lid (mummy) gets a green "X"
        /// above the right page arrow - a copy of that arrow, same font and look. Not for knocked out NPCs (they're
        /// robbed item by item) nor for traders.
        /// </summary>
        private void CreateTakeAllButton()
        {
            if (_configService == null || !_configService.Dev.EnableLootTakeAll || _takeAllButton != null ||
                IsTradeBackpack || _owner == null || _owner.IsHero)
                return;
            if (_owner.Npc != null && _owner.Npc.Props.BodyState != VmGothicEnums.BodyState.BsDead)
                return;

            var arrow = FindButton(nameof(OnNextPageClick)) ?? FindButton(nameof(OnNextCategoryClick));
            if (arrow == null)
            {
                Logger.LogWarning("[VRBackpack] No arrow button found - loot backpack without the take all button.",
                    LogCat.VR);
                return;
            }

            _takeAllButton = Instantiate(arrow.gameObject, arrow.transform.parent);
            _takeAllButton.name = _takeAllButtonName;
            if (_takeAllButton.transform is RectTransform rect && arrow.transform is RectTransform arrowRect)
                rect.anchoredPosition = arrowRect.anchoredPosition + new Vector2(0f, arrowRect.rect.height * 1.2f);
            else
                _takeAllButton.transform.localPosition = arrow.transform.localPosition + Vector3.up * 0.06f;

            var button = _takeAllButton.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(OnTakeAllClick);

            var texts = _takeAllButton.GetComponentsInChildren<TMP_Text>(true);
            foreach (var text in texts)
            {
                text.text = "X";
                text.color = _takeAllColor;
            }
            if (texts.Length == 0)
                Logger.LogWarning("[VRBackpack] The arrow button has no text - take all button without its X.", LogCat.VR);
        }

        private Button FindButton(string methodName)
        {
            foreach (var button in GetComponentsInChildren<Button>(true))
            {
                if (button.name == _takeAllButtonName)
                    continue;
                for (var i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    if (button.onClick.GetPersistentMethodName(i) == methodName)
                        return button;
                }
            }
            return null;
        }

        /// <summary>
        /// DeveloperConfig.EnableLootTakeAll: everything the loot holds goes into the hero's inventory at once.
        /// </summary>
        public void OnTakeAllClick()
        {
            if (_owner == null || _owner.IsHero || IsTradeBackpack)
                return;

            var hero = new PlayerInventoryOwner();
            var itemCount = 0;
            for (var category = VmGothicEnums.InvCats.InvWeapon; category < VmGothicEnums.InvCats.InvCatMax; category++)
            {
                foreach (var item in _owner.GetInventory(category).ToList())
                {
                    if (item.Amount <= 0 || !_owner.CanTake(item.Name))
                        continue;
                    _owner.Remove(item.Name, item.Amount);
                    hero.Add(item.Name, item.Amount);
                    itemCount += item.Amount;
                }
            }

            var clip = _audioService.CreateAudioClip(_audioService.InvOpen.File);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
            UpdateInventoryView();
            foreach (var backpack in FindObjectsByType<VRBackpack>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!backpack.IsNpcBackpack && !backpack.IsTradeBackpack)
                    backpack.UpdateInventoryView();
            }
            Logger.Log($"[VRBackpack] Took all from the loot: {itemCount} item(s)", LogCat.VR);
        }

        /// <summary>
        /// Removes a spawned NPC backpack (and its prefab root) without its socketed items counting as taken out.
        /// </summary>
        public void Despawn(GameObject spawnedRoot)
        {
            if (_refresh != null)
                StopCoroutine(_refresh);
            _runningRefreshes = 0;
            _tempIgnoreSocketing = true;
            ClearSockets();
            StartCoroutine(DestroyNextFrame(spawnedRoot != null ? spawnedRoot : gameObject, () =>
            {
                // A stopped refresh left the draw sounds off.
                _vrWeaponService.DrawSoundsActive = true;
            }));
        }

        /// <summary>
        /// The UI laser only hits registered canvases - they are collected once when the world loads
        /// (InitUIInteraction), a spawned loot backpack's page/category buttons weren't clickable.
        /// </summary>
        private void RegisterUiCanvases()
        {
            var uiPointers = ReflexProjectInstaller.DIContainer?.Resolve<IVrUiPointers>();
            if (uiPointers == null)
                return;
            foreach (var canvas in GetComponentsInChildren<Canvas>(true))
                uiPointers.RegisterCanvas(canvas);
        }

        private void UnregisterUiCanvases()
        {
            var uiPointers = ReflexProjectInstaller.DIContainer?.Resolve<IVrUiPointers>();
            if (uiPointers == null)
                return;
            foreach (var canvas in GetComponentsInChildren<Canvas>(true))
                uiPointers.UnregisterCanvas(canvas);
        }

        /// <summary>
        /// The trader whose goods this backpack shows (trade counter), null otherwise.
        /// </summary>
        public NpcContainer Trader => _trader;

        /// <summary>
        /// DeveloperConfig.EnableBackpackEquipToggle: [E] labels after equipping from outside (VRBackpackEquipToggle).
        /// </summary>
        public void RefreshInventoryView()
        {
            UpdateInventoryView();
        }

        /// <summary>
        /// The socketed stack nearest to a hand - taking pieces off it with the split button (VRStackSplitter).
        /// </summary>
        public VrInteractable FindStackNear(Vector3 position, float maxDistance)
        {
            VrInteractable nearest = null;
            var nearestDistance = maxDistance;
            foreach (var socket in _socketContainer.Sockets)
            {
                if (socket == null || !socket.IsOccupied || socket.Held == null)
                    continue;
                var distance = Vector3.Distance(socket.Held.transform.position, position);
                if (distance > nearestDistance)
                    continue;
                nearest = socket.Held;
                nearestDistance = distance;
            }
            return nearest;
        }

        /// <summary>
        /// Pieces taken off a socketed stack leave the owner's inventory (its view shows the new amount already).
        /// </summary>
        public void RemoveTakenPieces(string itemInstanceName, int amount)
        {
            Owner.Remove(itemInstanceName, amount);
        }

        /// <summary>
        /// Unpaid trader goods put into the hero's backpack/holster go back to the trader.
        /// </summary>
        private bool TryReturnTradeGoods(VrInteractable interactable)
        {
            var tradeGoods = interactable.GetComponentInParent<VRTradeGoods>();
            if (tradeGoods == null || tradeGoods.IsSettled)
                return false;

            Gothic.Core.Logging.Logger.Log("[Trade] Unpaid goods put into the hero's backpack - back to the trader",
                Gothic.Core.Logging.LogCat.VR);
            // Releasing it from our socket mustn't count as taken out of the hero's inventory (it never got in).
            _tempIgnoreSocketing = true;
            tradeGoods.ReturnToTrader();
            _tempIgnoreSocketing = false;
            return true;
        }

        private void ReturnToHero(VrInteractable interactable, VobContainer vobContainer)
        {
            _tempIgnoreSocketing = true;
            interactable.ForceRelease();
            _tempIgnoreSocketing = false;

            var item = vobContainer.VobAs<IItem>();
            _playerService.AddItem(vobContainer.Vob.Name, Mathf.Max(1, item?.Amount ?? 1));
            VRTradeGoods.RemoveFromWorld(vobContainer);
        }

        /// <summary>
        /// An NPC's backpack opens on its first category with items - a wolf has only meat, no paging through the empty
        /// weapon/armor/magic pages first.
        /// </summary>
        private void SelectFirstFilledCategory()
        {
            for (var category = VmGothicEnums.InvCats.InvWeapon; category <= VmGothicEnums.InvCats.InvMisc; category++)
            {
                if (Owner.GetInventory(category).Count == 0)
                    continue;
                _selectedCategory = category;
                _currentPage = 1;
                return;
            }
        }

        private void OnNpcInventoryChanged(NpcContainer npc)
        {
            if (_owner != null && npc == _owner.Npc)
                UpdateInventoryView();
        }

        private void Tint(Color tint)
        {
            foreach (var meshRenderer in GetComponentsInChildren<Renderer>(true))
            {
                if (meshRenderer.GetComponent<TMP_Text>() != null)
                    continue;
                foreach (var material in meshRenderer.materials)
                {
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", material.GetColor("_BaseColor") * tint);
                    else if (material.HasProperty("_Color"))
                        material.color *= tint;
                }
            }
        }

        
        private void Awake()
        {
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
            _isQuitting = false;
            _interactable = GetComponent<VrInteractable>();
            _socketContainer = GetComponent<IVrSocketContainer>();
            CollectSockets(_holsterSockets, _holsters);
            CollectSockets(_inventorySockets, _slots);

            _interactable.Grabbed += OnBackpackGrabbedToShoulder;
            _interactable.Released += OnBackpackReleasedFromShoulder;

            foreach (var holster in _holsters)
            {
                holster.Grabbed += OnItemPutIntoHolster;
                holster.Released += OnItemPutOutOfHolster;
            }

            foreach (var slot in _slots)
            {
                slot.Grabbed += OnItemPutIntoBackpack;
                slot.Released += OnItemPutOutOfBackpack;
            }
        }

        private void Start()
        {
            GlobalEventDispatcher.ZenKitBootstrapped.AddListener(Init);

            // V1, toggled at runtime via DeveloperConfig.EnableBackpackVacuum. Not for an NPC's loot backpack.
            if (!IsNpcBackpack && GetComponent<VRBackpackVacuum>() == null)
                gameObject.AddComponent<VRBackpackVacuum>();

            // V1, toggled at runtime via DeveloperConfig.EnableItemDetailsPopup.
            if (GetComponent<VRItemDetailsPopup>() == null)
                gameObject.AddComponent<VRItemDetailsPopup>();

            // V1, toggled at runtime via DeveloperConfig.EnableBackpackItemPassThrough.
            if (GetComponent<VRBackpackItemPassThrough>() == null)
                gameObject.AddComponent<VRBackpackItemPassThrough>();
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.NpcInventoryChanged.RemoveListener(OnNpcInventoryChanged);
            GlobalEventDispatcher.ZenKitBootstrapped.RemoveListener(Init);
            if (IsNpcBackpack)
                UnregisterUiCanvases();

            if (_interactable != null)
            {
                _interactable.Grabbed -= OnBackpackGrabbedToShoulder;
                _interactable.Released -= OnBackpackReleasedFromShoulder;
            }

            foreach (var holster in _holsters)
            {
                holster.Grabbed -= OnItemPutIntoHolster;
                holster.Released -= OnItemPutOutOfHolster;
            }

            foreach (var slot in _slots)
            {
                slot.Grabbed -= OnItemPutIntoBackpack;
                slot.Released -= OnItemPutOutOfBackpack;
            }
        }

        private static void OnApplicationQuitting()
        {
            _isQuitting = true;
        }

        private static void CollectSockets(GameObject[] socketGos, List<IVrSocket> result)
        {
            if (socketGos == null)
                return;

            foreach (var socketGo in socketGos)
            {
                if (socketGo != null && socketGo.TryGetComponent(out IVrSocket socket))
                    result.Add(socket);
            }
        }

        private void Init()
        {
            _interactable.SetSocketSounds(
                socketed: _audioService.CreateAudioClip(_audioService.InvClose.File),
                unsocketed: _audioService.CreateAudioClip(_audioService.InvOpen.File));

            _isZenKitBootstrapped = true;
        }

        private void OnBackpackGrabbedToShoulder(VrGrabArgs args)
        {
            SetHoldingHandCollisions(args, true);

            // We could potentially remove the meshes inside Backpack or simply hide it from rendering later.
            if (IsShoulderSocket(args))
                SetSlotDetectionEnabled(false);
        }

        private void OnBackpackReleasedFromShoulder(VrGrabArgs args)
        {
            SetHoldingHandCollisions(args, false);

            if (!IsShoulderSocket(args))
                return;

            SetSlotDetectionEnabled(true);
            UpdateInventoryView();
        }

        /// <summary>
        /// DeveloperConfig.EnableBackpackHandPassThrough: while a hand holds the backpack, both physical hands ignore its
        /// whole hull - the holding hand and the backpack pushed each other (the backpack shook), and the free hand got
        /// stuck on the hull when reaching for the slots. Back to normal once no hand holds it.
        /// </summary>
        private void SetHoldingHandCollisions(VrGrabArgs args, bool isHeld)
        {
            if (!args.IsHandOrDistance || _configService == null || !_configService.Dev.EnableBackpackHandPassThrough)
                return;

            var context = _vrPlayerService.VRContextInteractionService;
            var hands = context?.PhysicsHands;
            if (hands == null || hands.Count == 0)
                return;

            // The other hand may still hold it.
            var isAnyHandHolding = isHeld || (context.Rig != null && context.Rig.HandGrabbers.Any(i =>
                i.HandSide != args.HandSide && i.Held == _interactable));

            var hull = GetComponentsInChildren<Collider>(true)
                .Where(i => !i.isTrigger && i.GetComponentInParent<VobLoader>() == null)
                .ToList();
            foreach (var hand in hands)
                hand.IgnoreCollisions(hull, isAnyHandHolding);
        }

        private static bool IsShoulderSocket(VrGrabArgs args)
        {
            if (args.Kind != VrGrabberKind.Socket || args.GrabberGo == null)
                return false;

            return args.GrabberGo.TryGetComponent(out IVrSocket socket) && socket.IsShoulder;
        }

        /// <summary>
        /// Backend-neutral slots only. HVR's backpack slots stop detecting on their own (ParentDisablesGrab).
        /// </summary>
        private void SetSlotDetectionEnabled(bool isEnabled)
        {
            foreach (var socket in _slots)
            {
                if (socket is VrSocketSlot slot)
                    slot.SetDetectionEnabled(isEnabled);
            }
        }

        /// <summary>
        /// When putting into holster, culling is already deactivated.
        /// We simply need to tell the game: add inventory amount.
        /// </summary>
        private void OnItemPutIntoHolster(VrGrabArgs args)
        {
            // Unpaid trader goods don't become the hero's.
            if (TryReturnTradeGoods(args.Interactable))
                return;

            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>();
            var vobContainer = vobLoader.Container;

            _playerService.AddItem(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));
            TrackHolsteredMeleeWeapon(vobContainer, true);
        }

        /// <summary>
        /// A melee weapon at the belt counts as the hero's equipped weapon for the scripts (Ulu-Mulu: orcs tolerate him).
        /// </summary>
        private void TrackHolsteredMeleeWeapon(VobContainer vobContainer, bool isHolstered)
        {
            var item = vobContainer?.GetItemInstance();
            if (item == null || item.MainFlag != (int)VmGothicEnums.ItemFlags.ItemKatNf)
                return;

            if (isHolstered)
                _npcHelperService.HeroHolsteredMeleeWeapons.Add(item);
            else
                _npcHelperService.HeroHolsteredMeleeWeapons.Remove(item);
        }


        private void OnItemPutIntoBackpack(VrGrabArgs args)
        {
            if (_isQuitting || _tempIgnoreSocketing || _refreshPlacements.Contains(args.Interactable))
                return;

            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>(true);
            var vobContainer = vobLoader.Container;

            var tradeGoods = args.Interactable.GetComponentInParent<VRTradeGoods>();
            if (!IsNpcBackpack && TryReturnTradeGoods(args.Interactable))
                return;
            if (IsTradeBackpack)
            {
                // The hero's own item doesn't go into the trader's goods - back into the hero's inventory.
                if (tradeGoods == null)
                {
                    ReturnToHero(args.Interactable, vobContainer);
                    return;
                }
                // Laid back into the goods: counted below, nothing left to return.
                tradeGoods.IsSettled = true;
            }

            _saveGameService.UntrackLooseItem(vobContainer);
            _vobMeshCullingService.RemoveCullingEntry(vobContainer);
            _vobService.UntrackVobFromCache(vobContainer);

            Owner.Add(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));

            // An NPC owner fires NpcInventoryChanged, which refreshes us.
            if (!IsRefreshedByNpcEvent)
                UpdateInventoryView();
        }

        /// <summary>
        /// When putting out of holster, culling is already deactivated.
        /// We simply need to tell the game: substract inventory amount.
        /// </summary>
        private void OnItemPutOutOfHolster(VrGrabArgs args)
        {
            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>();
            if (vobLoader == null)
                return;
            var vobContainer = vobLoader.Container;
            TrackHolsteredMeleeWeapon(vobContainer, false);

            if (_isQuitting || _tempIgnoreSocketing)
                return;

            _playerService.RemoveItem(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));
        }

        private void OnItemPutOutOfBackpack(VrGrabArgs args)
        {
            if (_isQuitting || _tempIgnoreSocketing || _refreshRemovals.Contains(args.Interactable))
                return;

            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>();
            var vobContainer = vobLoader.Container;

            _vobMeshCullingService.AddCullingEntry(vobContainer);
            _saveGameService.CurrentWorldData.Vobs.Add(vobContainer.Vob);
            _saveGameService.TrackLooseItem(vobContainer);

            // Taking the last one of an equipped item out takes it off (IInventoryOwner.Remove).
            Owner.Remove(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));

            if (!IsRefreshedByNpcEvent)
                UpdateInventoryView();
        }
        
        public void OnPrevPageClick()
        {
            _currentPage--;
            if (_currentPage < 1)
                _currentPage = 1;

            UpdateInventoryView();
        }

        public void OnNextPageClick()
        {
            _currentPage++;
            if (_currentPage > _totalPages)
                _currentPage = _totalPages;

            UpdateInventoryView();
        }

        public void OnNextCategoryClick()
        {
            if (_selectedCategory >= VmGothicEnums.InvCats.InvMisc)
                return;

            _selectedCategory++;

            OnCategoryClick();
        }

        public void OnPrevCategoryClick()
        {
            if (_selectedCategory <= VmGothicEnums.InvCats.InvWeapon)
                return;

            _selectedCategory--;

            OnCategoryClick();
        }

        private void OnCategoryClick()
        {
            _currentPage = 1;

            UpdateInventoryView();
        }

        private void UpdateInventoryView()
        {
            // Quitting: the shoulder socket lets go of the backpack too - no refresh without the DI containers.
            if (_isQuitting)
                return;

            // A refresh is still re-stacking the sockets: run once more after it (two at once doubled the items).
            if (_refresh != null)
            {
                _isRefreshDirty = true;
                return;
            }

            var inventory = Owner.GetInventory(_selectedCategory);

            // Equipped items first (stable order otherwise), so they're always on the first page with their [E] badge.
            inventory = inventory.OrderByDescending(i => Owner.IsEquipped(i.Name)).ToList();

            // Subtract amount of held items from inventory - they count as the hero's inventory (VRPlayerService).
            if (Owner.IsHero)
            {
                SubtractItemFromHand(inventory, _vrPlayerService.GrabbedItemLeft);
                SubtractItemFromHand(inventory, _vrPlayerService.GrabbedItemRight);
            }

            UpdateCategoryText();
            UpdatePagerText(inventory);
            _refresh = StartCoroutine(UpdateSockets(inventory));
        }

        private void UpdateCategoryText()
        {
            _categoryText.text = _vmService.InventoryCategories[(int)_selectedCategory];
        }

        private void UpdatePagerText(List<ContentItem> inventory)
        {
            if (inventory.Count == 0)
                _totalPages = 1;
            else
                _totalPages = Mathf.CeilToInt((float)inventory.Count / 9);

            if (_currentPage > _totalPages)
                _currentPage = _totalPages;

            _pagerText.text = $"{_currentPage}/{_totalPages}";
        }

        private IEnumerator UpdateSockets(List<ContentItem> inventory)
        {
            yield return null;

            // While we re-stack items into slots, we need to ignore their events. Otherwise, we create a loop.
            _runningRefreshes++;
            _vrWeaponService.DrawSoundsActive = false;

            ClearSockets();
            yield return null; // Releasing and destroying objects takes until the next frame.

            // A throw here used to leave _refresh set forever - every later refresh only got marked dirty and the
            // backpack UI froze (categories, pages, re-opening).
            try
            {
                RefillSockets(inventory);
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[VRBackpack] Refilling page {_currentPage}/{_totalPages} failed: {e}", LogCat.VR);
            }
            yield return null;

            // Backends may raise the slot events a frame later (or more than once). Forget the items afterwards.
            _runningRefreshes--;
            if (_runningRefreshes > 0)
                yield break;

            _refreshRemovals.Clear();
            _refreshPlacements.Clear();
            _vrWeaponService.DrawSoundsActive = true;

            _refresh = null;
            if (_isRefreshDirty)
            {
                _isRefreshDirty = false;
                UpdateInventoryView();
            }
        }

        private void ClearSockets()
        {
            foreach (var socket in _socketContainer.Sockets)
            {
                var label = socket.Transform.Find(_equippedLabelName);
                if (label != null)
                    Destroy(label.gameObject);

                // Nothing inside socket
                if (!socket.IsOccupied)
                    continue;

                // Destroy VobLoader GO after releasing it from slot (proper event handling)
                var heldRoot = socket.Held.transform.parent.gameObject;
                _refreshRemovals.Add(socket.Held);
                socket.ForceRelease();
                heldRoot.SetActive(false); // Disable it, as it would be with scale 1 in front of our camera for 1 second.
                // We need to wait until next frame to ensure the socket has removed the item.
                StartCoroutine(DestroyNextFrame(heldRoot));
            }
        }

        private void RefillSockets(List<ContentItem> inventory)
        {
            // The page can move on while this refresh waits (next page clicked meanwhile): keep it in range.
            var lastPage = Mathf.Max(1, Mathf.CeilToInt(inventory.Count / 9f));
            _currentPage = Mathf.Clamp(_currentPage, 1, lastPage);
            _pagerText.text = $"{_currentPage}/{lastPage}";

            var startIndex = _currentPage * 9 - 9;
            var count = Mathf.Max(0, Mathf.Min(9, inventory.Count - startIndex));
            var items = inventory.GetRange(startIndex, count);

            foreach (var item in items)
            {
                var vobContainer = _vobService.CreateItem(new Item
                {
                    Name = item.Name,
                    Visual = new VisualMesh(),
                    Instance = item.Name,
                    Amount = item.Amount
                });

                vobContainer.Go.GetComponentInChildren<Rigidbody>().isKinematic = false; // Get rid of isKinematic warnings as the Grab is done "physically".
                if (IsTradeBackpack)
                    vobContainer.Go.AddComponent<VRTradeGoods>().Init(_trader);
                var interactable = vobContainer.Go.GetComponentInChildren<VrInteractable>();
                _refreshPlacements.Add(interactable);
                _socketContainer.TryPlace(interactable, silent: true);

                if (Owner.IsEquipped(item.Name))
                {
                    var socket = _socketContainer.Sockets.FirstOrDefault(s => s.Held == interactable);
                    if (socket != null)
                        AddEquippedLabel(socket.Transform);
                }
            }
        }

        private static IEnumerator DestroyNextFrame(GameObject go, System.Action afterwards = null)
        {
            yield return null;
            afterwards?.Invoke();
            Destroy(go);
        }

        /// <summary>
        /// Same [E] badge as in the NPC loot panel (VRNpcLoot).
        /// </summary>
        private void AddEquippedLabel(Transform socket)
        {
            const float worldScale = 0.013f;
            const float worldOffsetUp = 0.06f;

            var labelGo = new GameObject(_equippedLabelName);
            labelGo.transform.SetParent(socket, false);
            labelGo.transform.position = socket.position + socket.up * worldOffsetUp;
            labelGo.transform.localScale = Vector3.one * (worldScale / Mathf.Max(0.0001f, socket.lossyScale.x));

            var tmp = labelGo.AddComponent<TextMeshPro>();
            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font != null)
                tmp.font = font;
            tmp.text = "[E]";
            tmp.fontSize = 12;
            tmp.color = new Color(1f, 0.65f, 0f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.fontStyle = FontStyles.Bold;

            // Always readable, however the backpack is held.
            labelGo.AddComponent<Gothic.VR.Adapters.UI.VRBillboard>();
        }

        private void SubtractItemFromHand(List<ContentItem> inventory, GameObject handItem)
        {
            if (handItem == null)
                return;

            var vobLoader = handItem.GetComponentInParent<VobLoader>();
            if (vobLoader == null)
                return;

            var item = vobLoader.Container?.VobAs<IItem>();

            if (item == null)
                return;
            
            var matchingItem = inventory.FirstOrDefault(x => x.Name == item.Instance);

            // == nothing found
            if (matchingItem == null)
                return;
            
            matchingItem.Amount -= item.Amount;
            if (matchingItem.Amount <= 0)
            {
                inventory.Remove(matchingItem);
            }
        }
    }
}
