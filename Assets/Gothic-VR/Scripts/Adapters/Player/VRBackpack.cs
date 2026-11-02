using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Models.Context;
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
    /// items.
    /// Like held items (VRVobItem), the backpack ignores the hand holding it and, on the shoulder, both hands.
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
        private readonly Dictionary<Collider, Vector3> _grabVolumeSizes = new();

        private bool _isZenKitBootstrapped;
        private int _currentPage = 1;
        private int _totalPages;
        private VmGothicEnums.InvCats _selectedCategory =  VmGothicEnums.InvCats.InvWeapon;
        private bool _tempIgnoreSocketing;
        // Quitting: the slots let go of their items while the DI containers are gone already - nothing to count.
        private static bool _isQuitting;
        private IInventoryOwner _owner;
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
        /// Resources path of the backpack prefab (copies for NPC loot).
        /// </summary>
        public const string PrefabPath = "VR/Prefabs/Player-Elements/BackPack XRI";

        /// <summary>
        /// A copy of the hero's backpack prefab (NPC loot). spawnedRoot is the prefab root to destroy.
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

        private void SetOwner(IInventoryOwner owner)
        {
            _owner = owner;
            SelectFirstFilledCategory();
            GlobalEventDispatcher.NpcInventoryChanged.AddListener(OnNpcInventoryChanged);

            // The backpack's hull doesn't collide with the world (it's worn on the
            // shoulder) - a loot backpack fell through the ground. It stays where it is (and where it's let go: XRI
            // restores the kinematic state on drop).
            foreach (var body in GetComponentsInChildren<Rigidbody>())
                body.isKinematic = true;

            Tint(_npcBackpackTint);
            RegisterUiCanvases();
            Init();
            UpdateInventoryView();
            CreateTakeAllButton();
        }

        /// <summary>
        /// DeveloperConfig.EnableLootTakeAll: loot of a dead NPC or of a container without a lid (mummy) gets a green "X"
        /// above the right page arrow - a copy of that arrow, same font and look. Not for knocked out NPCs (they're
        /// robbed item by item).
        /// </summary>
        private void CreateTakeAllButton()
        {
            if (_configService == null || !_configService.Dev.EnableLootTakeAll || _takeAllButton != null ||
                _owner == null || _owner.IsHero)
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
            if (_owner == null || _owner.IsHero)
                return;

            var itemCount = TakeAll(_owner);
            var clip = _audioService.CreateAudioClip(_audioService.InvOpen.File);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
            // Taking everything may have closed the loot (empty corpse) and despawned this backpack.
            if (this != null && gameObject.activeInHierarchy)
                UpdateInventoryView();
            Logger.Log($"[VRBackpack] Took all from the loot: {itemCount} item(s)", LogCat.VR);
        }

        /// <summary>
        /// Everything the owner (NPC, chest) gives away goes into the hero's inventory - the loot's green X and
        /// DeveloperConfig.EnableCorpseHoldTakesAll. Returns the number of items taken.
        /// </summary>
        public static int TakeAll(IInventoryOwner from)
        {
            var hero = new PlayerInventoryOwner();
            var itemCount = 0;
            for (var category = VmGothicEnums.InvCats.InvWeapon; category < VmGothicEnums.InvCats.InvCatMax; category++)
            {
                foreach (var item in from.GetInventory(category).ToList())
                {
                    if (item.Amount <= 0 || !from.CanTake(item.Name))
                        continue;
                    from.Remove(item.Name, item.Amount);
                    hero.Add(item.Name, item.Amount);
                    itemCount += item.Amount;
                }
            }

            foreach (var backpack in FindObjectsByType<VRBackpack>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!backpack.IsNpcBackpack)
                    backpack.UpdateInventoryView();
            }
            return itemCount;
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

            // A grab took the backpack out of its prefab root (XRI
            // parents the held object to the scene) - destroying the root alone left it in the hand.
            var isDetached = spawnedRoot != null && !transform.IsChildOf(spawnedRoot.transform);
            if (isDetached && _interactable != null && _interactable.IsHeld)
                _interactable.ForceRelease();

            StartCoroutine(DestroyNextFrame(spawnedRoot != null ? spawnedRoot : gameObject, () =>
            {
                // A stopped refresh left the draw sounds off.
                _vrWeaponService.DrawSoundsActive = true;
                if (isDetached)
                {
                    Logger.Log($"[VRBackpack] Held loot backpack '{name}' removed with its loot", LogCat.VR);
                    Destroy(gameObject);
                }
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

            _interactable.Grabbed += OnBackpackGrabbed;
            _interactable.Released += OnBackpackReleased;

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

            // Items held near the backpack pass through it.
            if (GetComponent<VRBackpackItemPassThrough>() == null)
                gameObject.AddComponent<VRBackpackItemPassThrough>();
        }

        /// <summary>
        /// The item's instance symbol - the vob name isn't always the symbol (PlayerService.TryGetItemSymbol).
        /// </summary>
        private static string GetInstanceName(VobContainer vobContainer)
        {
            var item = vobContainer.VobAs<IItem>();
            return item?.GetSymbolName() ?? vobContainer.Vob.Name;
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.NpcInventoryChanged.RemoveListener(OnNpcInventoryChanged);
            GlobalEventDispatcher.ZenKitBootstrapped.RemoveListener(Init);
            if (IsNpcBackpack)
                UnregisterUiCanvases();

            if (_interactable != null)
            {
                _vrPlayerService?.ForgetPlayerCollision(_interactable);
                _interactable.Grabbed -= OnBackpackGrabbed;
                _interactable.Released -= OnBackpackReleased;
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

        private void OnBackpackGrabbed(VrGrabArgs args)
        {
            // The held backpack follows the physical hand. Colliding with it, it would push the hand away and spin.
            _vrPlayerService.UpdatePlayerCollision(args, true);
            SetHoldingHandCollisions(args, true);

            // We could potentially remove the meshes inside Backpack or simply hide it from rendering later.
            if (!IsShoulderSocket(args))
                return;

            SetSlotDetectionEnabled(false);
            SetShoulderGrabVolumes(true);
        }

        private void OnBackpackReleased(VrGrabArgs args)
        {
            _vrPlayerService.UpdatePlayerCollision(args, false);
            SetHoldingHandCollisions(args, false);

            if (!IsShoulderSocket(args))
                return;

            SetShoulderGrabVolumes(false);
            SetSlotDetectionEnabled(true);
            UpdateInventoryView();
        }

        /// <summary>
        /// DeveloperConfig.BackpackShoulderGrabScale: on the shoulder, the grab volumes (trigger colliders the hands
        /// grab it by) are larger - it had to be grabbed very precisely over the shoulder. Original size in a hand.
        /// </summary>
        private void SetShoulderGrabVolumes(bool isOnShoulder)
        {
            var scale = _configService != null ? _configService.Dev.BackpackShoulderGrabScale : 1f;
            if (isOnShoulder && (_grabVolumeSizes.Count > 0 || scale <= 1f))
                return;

            if (!isOnShoulder)
            {
                foreach (var pair in _grabVolumeSizes)
                    SetVolumeSize(pair.Key, pair.Value);
                _grabVolumeSizes.Clear();
                return;
            }

            foreach (var volume in _interactable.Colliders)
            {
                if (volume == null || !volume.isTrigger)
                    continue;

                var size = GetVolumeSize(volume);
                _grabVolumeSizes[volume] = size;
                SetVolumeSize(volume, size * scale);
            }
        }

        // Sphere/capsule: (radius, height, 0), box: size.
        private static Vector3 GetVolumeSize(Collider volume)
        {
            return volume switch
            {
                SphereCollider sphere => new Vector3(sphere.radius, 0f, 0f),
                CapsuleCollider capsule => new Vector3(capsule.radius, capsule.height, 0f),
                BoxCollider box => box.size,
                _ => Vector3.zero
            };
        }

        private static void SetVolumeSize(Collider volume, Vector3 size)
        {
            switch (volume)
            {
                case SphereCollider sphere:
                    sphere.radius = size.x;
                    break;
                case CapsuleCollider capsule:
                    capsule.radius = size.x;
                    capsule.height = size.y;
                    break;
                case BoxCollider box:
                    box.size = size;
                    break;
            }
        }

        /// <summary>
        /// While a hand holds the backpack, both physical hands ignore its
        /// whole hull - the holding hand and the backpack pushed each other (the backpack shook), and the free hand got
        /// stuck on the hull when reaching for the slots. Back to normal once no hand holds it.
        /// </summary>
        private void SetHoldingHandCollisions(VrGrabArgs args, bool isHeld)
        {
            if (!args.IsHandOrDistance)
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
        /// When putting into holster, culling is already paused (hand grab). The item is in the inventory now.
        /// </summary>
        private void OnItemPutIntoHolster(VrGrabArgs args)
        {
            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>();

            var vobContainer = vobLoader.Container;

            _playerService.PickUpItem(vobContainer);
        }

        private void OnItemPutIntoBackpack(VrGrabArgs args)
        {
            if (_isQuitting || _tempIgnoreSocketing || _refreshPlacements.Contains(args.Interactable))
                return;

            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>(true);
            var vobContainer = vobLoader.Container;

            // In a backpack slot, the item isn't part of the world's VOB list (PlayerService.PickUpItem's rule) - the
            // owner may be the hero, an NPC or a chest.
            _vobMeshCullingService.RemoveCullingEntry(vobContainer);
            _vobService.UntrackVobFromCache(vobContainer);

            Owner.Add(GetInstanceName(vobContainer), Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));

            // An NPC owner fires NpcInventoryChanged, which refreshes us.
            if (!IsRefreshedByNpcEvent)
                UpdateInventoryView();
        }

        /// <summary>
        /// A Gothic item (not the backpack, not a mob) that TryStash() takes.
        /// </summary>
        public bool CanStash(VrInteractable item)
        {
            return VrSocketSlot.IsItem(item);
        }

        /// <summary>
        /// Over-the-shoulder stash (VrShoulderStash): the item goes into the inventory like into a backpack slot, but
        /// leaves the world at once, the backpack stays on the shoulder.
        /// </summary>
        public bool TryStash(VrInteractable item)
        {
            if (!CanStash(item) || item.IsHeld)
                return false;

            var vobLoader = item.GetComponentInParent<VobLoader>(true);
            _playerService.PickUpItem(vobLoader.Container, removeCulling: true);

            vobLoader.gameObject.SetActive(false);
            StartCoroutine(DestroyNextFrame(vobLoader.gameObject));

            UpdateInventoryView();
            return true;
        }

        /// <summary>
        /// When putting out of holster, culling is still paused (the hand moves it next).
        /// If a hand holds the item already, it stays in the inventory.
        /// </summary>
        private void OnItemPutOutOfHolster(VrGrabArgs args)
        {
            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>();
            if (vobLoader == null)
                return;

            if (_isQuitting || _tempIgnoreSocketing)
                return;

            _playerService.DropItem(vobLoader.Container,
                keepOutOfWorld: _vrPlayerService.IsHeldInHand(args.Interactable));
        }

        private void OnItemPutOutOfBackpack(VrGrabArgs args)
        {
            if (_isQuitting || _tempIgnoreSocketing || _refreshRemovals.Contains(args.Interactable))
                return;

            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>();

            var vobContainer = vobLoader.Container;

            _vobMeshCullingService.AddCullingEntry(vobContainer);
            // Like PlayerService.DropItem: a hand holding it keeps it out of the world's VOB list.
            var worldVobs = _saveGameService.CurrentWorldData.Vobs;
            if (!_vrPlayerService.IsHeldInHand(args.Interactable) && !worldVobs.Contains(vobContainer.Vob))
                worldVobs.Add(vobContainer.Vob);

            // Taking the last one of an equipped item out takes it off (IInventoryOwner.Remove).
            Owner.Remove(GetInstanceName(vobContainer), Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));

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
                // RefillSockets created it through VobService.CreateItem (VobCache) - every refresh (vacuum, loot, NPC
                // inventory events) left a destroyed container in the cache.
                var heldContainer = socket.Held.transform.GetComponentInParent<VobLoader>(true)?.Container;
                if (heldContainer != null)
                    _vobService.UntrackVobFromCache(heldContainer);
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
