using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Vobs;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The player's backpack (inventory pages) and holsters. The backpack itself is a VrInteractable with an
    /// IVrSocketContainer for its slots. Items put into holsters or slots are added to the hero's inventory.
    /// While the backpack is on the shoulder, its slots detect nothing (VrSocketSlot; HVR does it itself).
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

        
        private void Awake()
        {
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
        }

        private void OnDestroy()
        {
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
            // We could potentially remove the meshes inside Backpack or simply hide it from rendering later.
            if (IsShoulderSocket(args))
                SetSlotDetectionEnabled(false);
        }

        private void OnBackpackReleasedFromShoulder(VrGrabArgs args)
        {
            if (!IsShoulderSocket(args))
                return;

            SetSlotDetectionEnabled(true);
            UpdateInventoryView();
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
            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>();
            var vobContainer = vobLoader.Container;

            _playerService.AddItem(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));
        }


        private void OnItemPutIntoBackpack(VrGrabArgs args)
        {
            if (_refreshPlacements.Contains(args.Interactable))
                return;

            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>(true);
            var vobContainer = vobLoader.Container;

            _vobMeshCullingService.RemoveCullingEntry(vobContainer);
            _saveGameService.CurrentWorldData.Vobs.Remove(vobContainer.Vob);

            _playerService.AddItem(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));

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

            _playerService.RemoveItem(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));
        }

        private void OnItemPutOutOfBackpack(VrGrabArgs args)
        {
            if (_refreshRemovals.Contains(args.Interactable))
                return;

            var vobLoader = args.Interactable.GetComponentInParent<VobLoader>();
            var vobContainer = vobLoader.Container;

            _vobMeshCullingService.AddCullingEntry(vobContainer);
            _saveGameService.CurrentWorldData.Vobs.Add(vobContainer.Vob);

            _playerService.RemoveItem(vobContainer.Vob.Name, Mathf.Max(1, vobContainer.VobAs<IItem>().Amount));

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
            var inventory = _playerService.GetInventory(_selectedCategory);

            // Subtract amount of held items from inventory
            SubtractItemFromHand(inventory, _vrPlayerService.GrabbedItemLeft);
            SubtractItemFromHand(inventory, _vrPlayerService.GrabbedItemRight);

            UpdateCategoryText();
            UpdatePagerText(inventory);
            StartCoroutine(UpdateSockets(inventory));
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

            RefillSockets(inventory);
            yield return null;

            // Backends may raise the slot events a frame later (or more than once). Forget the items afterwards.
            _runningRefreshes--;
            if (_runningRefreshes > 0)
                yield break;

            _refreshRemovals.Clear();
            _refreshPlacements.Clear();
            _vrWeaponService.DrawSoundsActive = true;
        }

        private void ClearSockets()
        {
            foreach (var socket in _socketContainer.Sockets)
            {
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
                var interactable = vobContainer.Go.GetComponentInChildren<VrInteractable>();
                _refreshPlacements.Add(interactable);
                _socketContainer.TryPlace(interactable, silent: true);
            }
        }

        private static IEnumerator DestroyNextFrame(GameObject go)
        {
            yield return null;
            Destroy(go);
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
