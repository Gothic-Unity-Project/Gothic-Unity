#if GOTHIC_HVR_INSTALLED
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets.HurricaneVR.Framework.Shared.Utilities;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Inventory;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Adapters.HVROverrides;
using Gothic.VR.Adapters.Trade;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Sockets;
using HurricaneVR.Framework.Core.UI;
using HurricaneVR.Framework.Core.Utils;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Vobs;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Inventory view with 9 sockets, categories and pages. The hero's backpack by default; the same prefab is spawned
    /// as an NPC's loot backpack (SetNpcOwner) - tinted, showing and changing the NPC's items.
    /// </summary>
    [RequireComponent(typeof(HVRSocketable))]
    public class VRBackpack : MonoBehaviour
    {
        [SerializeField] private TMP_Text _categoryText;
        [SerializeField] private TMP_Text _pagerText;
        [SerializeField] private HVRSocketContainer _socketContainer;

        private bool _isZenKitBootstrapped;
        private int _currentPage = 1;
        private int _totalPages;
        private VmGothicEnums.InvCats _selectedCategory =  VmGothicEnums.InvCats.InvWeapon;
        private bool _tempIgnoreSocketing;
        private IInventoryOwner _owner;
        private NpcContainer _trader;
        private Coroutine _refresh;
        private bool _isRefreshDirty;
        private static readonly Color _npcBackpackTint = new(0.55f, 0.75f, 1f);
        
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;

        private const string _equippedLabelName = "EquippedLabel";

        private IInventoryOwner Owner => _owner ??= new PlayerInventoryOwner();

        public bool IsNpcBackpack => _owner != null && !_owner.IsHero;

        /// <summary>
        /// A trader's goods during a trade (VRTradeCounter): items taken out are VRTradeGoods until paid.
        /// </summary>
        public bool IsTradeBackpack => _trader != null;

        private const string _prefabPath = "VR/Prefabs/Player-Elements/BackPack";

        /// <summary>
        /// A copy of the hero's backpack prefab (NPC loot, trader goods). spawnedRoot is the prefab root to destroy.
        /// </summary>
        public static VRBackpack SpawnCopy(Vector3 position, Quaternion rotation, string goName,
            out GameObject spawnedRoot)
        {
            spawnedRoot = null;
            var prefab = Resources.Load<GameObject>(_prefabPath);
            if (prefab == null)
            {
                Gothic.Core.Logging.Logger.LogWarning($"[VRBackpack] Prefab {_prefabPath} not found.",
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

            // The prefab's GrabColliders hold one empty entry: that turns on HVR's grab collider filter, which then lets
            // no collider through - the hero's backpack is only taken from the shoulder socket, a loot backpack lies
            // on the ground and couldn't be grabbed (nor force grabbed).
            var grabbable = GetComponent<HVRGrabbable>();
            if (grabbable != null)
            {
                grabbable.GrabColliders = System.Array.Empty<Collider>();
                grabbable.SetupGrabColliders();
            }
            Tint(_npcBackpackTint);
            RegisterUiCanvases();
            Init();
            UpdateInventoryView();
        }

        /// <summary>
        /// Removes a spawned NPC backpack (and its prefab root) without its socketed items counting as taken out.
        /// </summary>
        public void Despawn(GameObject spawnedRoot)
        {
            if (_refresh != null)
                StopCoroutine(_refresh);
            _tempIgnoreSocketing = true;
            ClearSockets();
            this.ExecuteNextUpdate(() =>
            {
                // A stopped refresh left the draw sounds off.
                _vrWeaponService.DrawSoundsActive = true;
                Destroy(spawnedRoot != null ? spawnedRoot : gameObject);
            });
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.NpcInventoryChanged.RemoveListener(OnNpcInventoryChanged);
            GlobalEventDispatcher.ZenKitBootstrapped.RemoveListener(Init);

            if (IsNpcBackpack && HVRInputModule.Instance != null)
            {
                foreach (var canvas in GetComponentsInChildren<Canvas>(true))
                    HVRInputModule.Instance.RemoveCanvas(canvas);
            }
        }

        /// <summary>
        /// The UI laser only hits canvases HVRInputModule knows - they are collected once when the world loads
        /// (InitUIInteraction), a spawned loot backpack's page/category buttons weren't clickable.
        /// </summary>
        private void RegisterUiCanvases()
        {
            if (HVRInputModule.Instance == null)
                return;
            foreach (var canvas in GetComponentsInChildren<Canvas>(true))
                HVRInputModule.Instance.AddCanvas(canvas);
        }

        /// <summary>
        /// The trader whose goods this backpack shows (trade counter), null otherwise.
        /// </summary>
        public NpcContainer Trader => _trader;

        /// <summary>
        /// The socketed stack nearest to a hand - taking pieces off it with the split button (VRStackSplitter).
        /// </summary>
        public HVRGrabbable FindStackNear(Vector3 position, float maxDistance)
        {
            HVRGrabbable nearest = null;
            var nearestDistance = maxDistance;
            foreach (var socket in _socketContainer.Sockets)
            {
                if (socket == null || !socket.IsGrabbing || socket.GrabbedTarget == null)
                    continue;
                var distance = Vector3.Distance(socket.GrabbedTarget.transform.position, position);
                if (distance > nearestDistance)
                    continue;
                nearest = socket.GrabbedTarget;
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
        private bool TryReturnTradeGoods(HVRGrabbable grabbable)
        {
            var tradeGoods = grabbable.GetComponentInParent<VRTradeGoods>();
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

        private void ReturnToHero(HVRGrabbable grabbable, VobContainer vobContainer)
        {
            _tempIgnoreSocketing = true;
            grabbable.ForceRelease();
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

        
        private void Start()
        {
            GlobalEventDispatcher.ZenKitBootstrapped.AddListener(Init);

            // V1, toggled at runtime via DeveloperConfig.EnableBackpackVacuum. Not for an NPC's loot backpack.
            if (!IsNpcBackpack && GetComponent<VRBackpackVacuum>() == null)
                gameObject.AddComponent<VRBackpackVacuum>();

            // V1, toggled at runtime via DeveloperConfig.EnableItemDetailsPopup.
            if (GetComponent<VRItemDetailsPopup>() == null)
                gameObject.AddComponent<VRItemDetailsPopup>();
        }

        private void Init()
        {
            var socketable = GetComponent<HVRSocketable>();

            socketable.UnsocketedClip = _audioService.CreateAudioClip(_audioService.InvOpen.File);
            socketable.SocketedClip = _audioService.CreateAudioClip(_audioService.InvClose.File);

            _isZenKitBootstrapped = true;
        }

        public void OnBackpackGrabbedToShoulder(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            // Nothing to do for now.
            // We could potentially remove the meshes inside Backpack or simply hide it from rendering later.
        }
        
        public void OnBackpackReleasedFromShoulder(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (grabber is not VRShoulderSocket && grabber is not HVRShoulderSocket)
                return;

            UpdateInventoryView();
        }

        /// <summary>
        /// When putting into holster, culling is already deactivated.
        /// We simply need to tell the game: add inventory amount.
        /// </summary>
        public void OnItemPutIntoHolster(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            // Unpaid trader goods don't become the hero's.
            if (TryReturnTradeGoods(grabbable))
                return;

            var vobLoader = grabbable.GetComponentInParent<VobLoader>();
            var vobContainer = vobLoader.Container;

            _playerService.AddItem(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));
        }


        public void OnItemPutIntoBackpack(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_tempIgnoreSocketing)
                return;

            var vobLoader = grabbable.GetComponentInParent<VobLoader>(true);
            var vobContainer = vobLoader.Container;

            var tradeGoods = grabbable.GetComponentInParent<VRTradeGoods>();
            if (!IsNpcBackpack && TryReturnTradeGoods(grabbable))
                return;
            if (IsTradeBackpack)
            {
                // The hero's own item doesn't go into the trader's goods - back into the hero's inventory.
                if (tradeGoods == null)
                {
                    ReturnToHero(grabbable, vobContainer);
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
            if (!IsNpcBackpack)
                UpdateInventoryView();
        }

        /// <summary>
        /// When putting out of holster, culling is already deactivated.
        /// We simply need to tell the game: substract inventory amount.
        /// </summary>
        public void OnItemPutOutOfHolster(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_tempIgnoreSocketing)
                return;

            var vobLoader = grabbable.GetComponentInParent<VobLoader>();
            if (vobLoader == null)
                return;
            var vobContainer = vobLoader.Container;

            _playerService.RemoveItem(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));
        }

        public void OnItemPutOutOfBackpack(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_tempIgnoreSocketing)
                return;

            var vobLoader = grabbable.GetComponentInParent<VobLoader>();
            var vobContainer = vobLoader.Container;

            _vobMeshCullingService.AddCullingEntry(vobContainer);
            _saveGameService.CurrentWorldData.Vobs.Add(vobContainer.Vob);
            _saveGameService.TrackLooseItem(vobContainer);

            // Taking the last one of an equipped item out takes it off (IInventoryOwner.Remove).
            Owner.Remove(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));

            if (!IsNpcBackpack)
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
            _tempIgnoreSocketing = true;
            _vrWeaponService.DrawSoundsActive = false;
            
            ClearSockets();
            yield return null; // Releasing and destroying objects takes until the next frame.

            RefillSockets(inventory);
            yield return null;

            _tempIgnoreSocketing = false;
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
                var label = socket.transform.Find(_equippedLabelName);
                if (label != null)
                    Destroy(label.gameObject);

                // Nothing inside socket
                if (!socket.IsGrabbing)
                    continue;

                // Destroy VobLoader GO after releasing it from slot (proper event handling)
                var heldRoot = socket.HeldObject.transform.parent.gameObject;
                socket.ForceRelease();
                heldRoot.SetActive(false); // Disable it, as it would be with scale 1 in front of our camera for 1 second.
                // We need to wait until next frame to ensure HVR has removed the item from socket.
                this.ExecuteNextUpdate(() => Destroy(heldRoot));
            }
        }

        private void RefillSockets(List<ContentItem> inventory)
        {
            var startIndex = _currentPage * 9 - 9;
            var count = Mathf.Min(9, inventory.Count - startIndex);
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
                var grabbable = vobContainer.Go.GetComponentInChildren<HVRGrabbable>();
                _socketContainer.TryAddGrabbable(grabbable);

                if (Owner.IsEquipped(item.Name))
                {
                    var socket = _socketContainer.Sockets.FirstOrDefault(s => s.GrabbedTarget == grabbable);
                    if (socket != null)
                        AddEquippedLabel(socket.transform);
                }
            }
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
#endif
