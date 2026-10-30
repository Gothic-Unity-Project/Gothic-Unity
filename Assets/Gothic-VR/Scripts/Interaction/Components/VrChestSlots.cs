using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Content of a chest (oCMobContainer), independent of the lid: the lid (IVrHinge, VrHinge) opens and closes
    /// it by its Opened/Closed events.
    /// - The prefab carries a slot inventory (IVrSocketContainer) per VR backend. The active one is used, others
    ///   stay hidden.
    /// - First open: the slots are laid out to the size of the chest mesh and the content (IContainer.Contents) is
    ///   spawned into them, without socket sounds.
    /// - Closing hides the slots (and the items inside).
    /// </summary>
    public class VrChestSlots : MonoBehaviour
    {
        // Need to be updated if oCMobContainer.prefab is changed.
        private const int SlotRows = 3;
        private const int SlotsPerRow = 4;
        private const float SlotRadius = 0.1f;
        private const float SlotCollectionMargin = SlotRadius / 2;

        [Tooltip("Slot inventories (with an IVrSocketContainer), one per VR backend. The first active one is used. " +
                 "A BoxCollider below it (drop zone / collector) is resized to the chest.")]
        [SerializeField] private List<GameObject> _inventories = new();

        [Tooltip("Seconds after spawning the content until socket sounds play again (HVR snaps items in later).")]
        [SerializeField] private float _silentAfterSpawnTime = 1f;

        [Inject] private readonly VobService _vobService;

        private GameObject _inventoryGo;
        private IVrSocketContainer _container;
        private BoxCollider _collector;
        private bool _isResolved;
        private bool _isContentSpawned;
        private bool _isSpawning;
        private IVrHinge _hinge;
        private bool _isOpen;
        private readonly List<ContentItem> _contentNotInSlots = new();

        public bool IsOpen => _isOpen;

        /// <summary>
        /// Slots of the active backend. Null if the chest has no active inventory.
        /// </summary>
        public IVrSocketContainer Container
        {
            get
            {
                Resolve();
                return _container;
            }
        }

        private void Start()
        {
            Resolve();
            if (!_isOpen && _inventoryGo != null)
                _inventoryGo.SetActive(false);

            _hinge = VrHinges.FindActive(this);
            if (_hinge == null)
            {
                Logger.LogWarning($"[Chest] {name} has no active lid hinge. It can't be opened.", LogCat.VR);
                return;
            }

            _hinge.Opened += Open;
            _hinge.Closed += Close;
        }

        private void OnDestroy()
        {
            if (_hinge == null)
                return;

            _hinge.Opened -= Open;
            _hinge.Closed -= Close;
        }

        public void Open()
        {
            Resolve();
            if (_inventoryGo == null)
                return;

            _isOpen = true;
            _inventoryGo.SetActive(true);

            if (_isContentSpawned)
                return;

            _isContentSpawned = true;
            AlignSlots();
            TrackContentChanges();
            StartCoroutine(SpawnContent());
        }

        /// <summary>
        /// Once spawned, the slots are the content. IContainer.Contents follows them, so that save games store what's
        /// inside the chest (taken out by hand, put into the backpack, put in).
        /// </summary>
        private void TrackContentChanges()
        {
            if (_container == null)
                return;

            foreach (var socket in _container.Sockets)
            {
                socket.Grabbed += _ => UpdateContents();
                socket.Released += _ => UpdateContents();
            }
        }

        private void UpdateContents()
        {
            if (_isSpawning)
                return;

            var vobContainer = GetComponentInParent<VobLoader>()?.Container?.VobAs<IContainer>();
            if (vobContainer == null)
                return;

            var items = _container.Sockets
                .Select(i => i.Held == null ? null : i.Held.GetComponentInParent<VobLoader>()?.Container)
                .Where(i => i?.Vob?.Type == VirtualObjectType.oCItem)
                .Select(i => i.VobAs<IItem>())
                .Select(i => new ContentItem(i.Name, Math.Max(1, i.Amount)))
                .Concat(_contentNotInSlots);

            vobContainer.Contents = items.PackItems(',');
        }

        public void Close()
        {
            _isOpen = false;

            // Items are put into active slots only. The inventory is hidden once the spawning is done.
            if (_inventoryGo != null && !_isSpawning)
                _inventoryGo.SetActive(false);
        }

        private void Resolve()
        {
            if (_isResolved)
                return;

            _isResolved = true;
            foreach (var inventory in _inventories)
            {
                if (inventory == null)
                    continue;

                var container = inventory.GetComponent<IVrSocketContainer>();
                if (_container == null && container != null && container.IsActive)
                {
                    _container = container;
                    _inventoryGo = inventory;
                    _collector = inventory.GetComponentInChildren<BoxCollider>(true);
                    continue;
                }

                inventory.SetActive(false);
            }

            if (_container == null)
                Logger.LogWarning($"[Chest] {name} has no slot inventory for the active VR backend.", LogCat.VR);
        }

        /// <summary>
        /// Containers have different sizes. Re-align the prefab's slots to match their actual sizes.
        /// </summary>
        private void AlignSlots()
        {
            // We need to dynamically fetch the bounding box of main mesh as some chests have the correct one inside
            // BIP01 "CHEST_*_0" and others inside "ZM_CHEST*".
            var meshFilters = GetComponentsInChildren<MeshFilter>()
                .Where(i => i.GetComponentInParent<VrInteractable>() == null) // Not the lid, not items inside.
                .Where(i => i.gameObject.name.StartsWithIgnoreCase("ZM_") ||
                            i.gameObject.name.StartsWithIgnoreCase("BIP01")) // Gothic meshes only, no slot rings.
                .Where(i => !i.gameObject.name.ContainsIgnoreCase("LOCK")) // Ignore lock element
                .ToArray();

            MeshFilter usedMesh;
            if (meshFilters.Length == 0)
            {
                Logger.LogError($"No suitable mesh found for chest slot size calculation in {name}. Skipping...",
                    LogCat.VR);
                return;
            }
            else if (meshFilters.Length >= 2)
            {
                Logger.LogWarning($"More than one feasible MeshFilter for chest slot size calculation found in " +
                                  $"{name}. Leveraging first (1)ZM_* or (2)BIP01 one.", LogCat.VR);

                // Based on mesh check of all containers in G1, the highest priority for the right mesh is a ZM_* GO
                // if existing.
                var zmMesh = meshFilters.FirstOrDefault(i => i.gameObject.name.StartsWithIgnoreCase("ZM_"));
                usedMesh = zmMesh ?? meshFilters.First();
            }
            else
            {
                usedMesh = meshFilters.First();
            }

            var mainMeshBounds = usedMesh!.sharedMesh.bounds;

            // Set the collector's/drop zone's size to perfectly align with container.
            if (_collector != null)
                _collector.size = new Vector3(mainMeshBounds.size.x, _collector.size.y, mainMeshBounds.size.z);

            // Move all Slots nearly towards height of container (*1,5 == 75% close to top).
            _inventoryGo.transform.localPosition = new(0, mainMeshBounds.center.y * 1.5f, 0);

            var centeredMainBound = new Bounds(Vector3.zero, mainMeshBounds.size);

            // Align all slots in row+column based on actual sizes.
            var rowWidth = (centeredMainBound.size.x - 2 * SlotCollectionMargin) / SlotsPerRow;
            var rowDepth = (centeredMainBound.size.z - 2 * SlotCollectionMargin) / SlotRows;

            // Calculate center of a grid-element for a slot ring entry.
            var middleAlignmentX = (rowWidth - SlotRadius * 2) / 2;
            var middleAlignmentZ = (rowDepth - SlotRadius * 2) / 2;

            var currentRow = 0;
            var currentColumn = 0;

            foreach (var socket in _container.Sockets)
            {
                // SlotCollectionMargin - Left aligned margin for the whole collection
                // SlotRadius - Location is based on a 50% shift into the desired area as the drawing of a circle
                //              starts centered
                // middleAlignment* - Centered location within a grid for an element
                // min.x - Start point to calculate from left top
                socket.Transform.localPosition = new(
                    SlotCollectionMargin + SlotRadius + middleAlignmentX + centeredMainBound.min.x +
                    rowWidth * currentColumn,
                    0,
                    SlotCollectionMargin + SlotRadius + middleAlignmentZ + centeredMainBound.min.z +
                    rowDepth * currentRow
                );

                currentColumn++;
                if (currentColumn >= SlotsPerRow)
                {
                    currentColumn = 0;
                    currentRow++;
                }
            }
        }

        private IEnumerator SpawnContent()
        {
            var vobContainer = GetComponentInParent<VobLoader>()?.Container?.VobAs<IContainer>();
            var contentItems = (vobContainer?.Contents).UnpackItems();
            if (contentItems.Count == 0)
                yield break;

            _isSpawning = true;
            _container.SetSilent(true);

            var sockets = _container.Sockets;
            if (contentItems.Count > sockets.Count)
            {
                Logger.LogError("There are more items in a container than slots available. Please implement " +
                                "pagination to ensure everything is visible and can be grabbed!", LogCat.VR);
            }

            // Content without a free slot stays inside the chest.
            _contentNotInSlots.AddRange(contentItems.Skip(sockets.Count));

            for (var i = 0; i < Math.Min(contentItems.Count, sockets.Count); i++)
            {
                var item = SpawnItem(contentItems[i]);

                // Wait 1 frame to ensure our mesh bounds can be calculated by the slot.
                yield return null;

                PlaceIntoSlot(item);
            }

            // HVR snaps the items into place a bit later. They stay silent until then.
            yield return new WaitForSeconds(_silentAfterSpawnTime);

            _container.SetSilent(false);
            _isSpawning = false;
            UpdateContents();
            if (!_isOpen)
                _inventoryGo.SetActive(false);
        }

        private VobContainer SpawnItem(ContentItem contentItem)
        {
            var zkVob = new Item
            {
                Name = contentItem.Name,
                Amount = contentItem.Amount,
                Visual = new VisualMesh
                {
                    Name = contentItem.Name
                }
            };

            return _vobService.CreateItem(zkVob);
        }

        private void PlaceIntoSlot(VobContainer vobContainer)
        {
            var interactable = vobContainer?.Go.GetComponentInChildren<VrInteractable>(true);
            if (interactable == null || !_container.TryFindFree(interactable, out var socket) ||
                !socket.TryPlace(interactable, true))
            {
                Logger.LogWarning($"[Chest] Couldn't put {vobContainer?.Go.name} into a slot of {name}.", LogCat.VR);
                return;
            }

            // HVR sockets don't put the VobLoader at the slot's origin. VrSocketSlot centers the item itself.
            if (socket is not VrSocketSlot)
                vobContainer.Go.transform.localPosition = Vector3.zero;
        }
    }
}
