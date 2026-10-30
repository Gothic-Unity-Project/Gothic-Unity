using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Adapters.Vob;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Items the player carries on the VR rig: inside hip/shoulder holsters and in the hands. The rig isn't part of a
    /// world scene and survives loading a game, which is why its items need to follow the save game.
    ///
    /// Items in hands and holsters count as inventory (VRPlayerService.SetGrab, VRBackpack holster events). After
    /// loading, they're part of the loaded inventory already:
    /// - New game / load: holsters and hands are emptied without touching the inventory.
    /// - Saving: section "vr.carriedItems" inside UNITYSAVE.json stores which item is inside which holster. Hand
    ///   items aren't stored. They're inside the backpack after loading (a hand only keeps an item while its grip
    ///   is pressed).
    /// - Loading: once the world is loaded, the holster items are put back into their holsters.
    /// - Saving / leaving a world: the world's item list is synced with the scene (see SyncWorldItems()).
    /// </summary>
    public class VRCarriedItemsSaveService : ISaveStateSection
    {
        public string Key => "vr.carriedItems";
        public int Version => 1;

        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        private readonly List<IVrSocket> _holsters = new();

        // Socket events of our own placing/removing. Each one is consumed by the next matching event. Otherwise,
        // VRBackpack would change the inventory, which is loaded already.
        private readonly HashSet<VrInteractable> _silentPlacements = new();
        private readonly HashSet<VrInteractable> _silentReleases = new();

        private JArray _pendingHolsterItems;
        private bool _isInitialized;


        /// <summary>
        /// Called once the VR scope is built (ReflexVRSceneInstaller), i.e. before a save game can be loaded.
        /// </summary>
        public void Init()
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            _saveGameService.RegisterSection(this);
            GlobalEventDispatcher.StoreWorldStateStart.AddListener(SyncWorldItems);
            GlobalEventDispatcher.UnloadGameStart.AddListener(ClearHolstersAndHands);
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(RestorePendingHolsterItems);
        }

        /// <summary>
        /// Called by VRBackpack, which owns the holsters.
        /// </summary>
        public void RegisterHolsters(IEnumerable<IVrSocket> holsters)
        {
            _holsters.Clear();
            _holsters.AddRange(holsters);
        }

        /// <summary>
        /// True if the item was put into a holster by us (restored from a save game). Consumes the expectation.
        /// </summary>
        public bool ConsumeSilentPlacement(VrInteractable interactable)
        {
            return interactable != null && _silentPlacements.Remove(interactable);
        }

        /// <summary>
        /// True if the item was taken out of a holster by us (new game / loading). Consumes the expectation.
        /// </summary>
        public bool ConsumeSilentRelease(VrInteractable interactable)
        {
            return interactable != null && _silentReleases.Remove(interactable);
        }

        public JToken Capture()
        {
            var holsterItems = new JArray();
            for (var i = 0; i < _holsters.Count; i++)
            {
                var item = GetItem(_holsters[i].Held)?.VobAs<IItem>();
                if (item == null)
                    continue;

                holsterItems.Add(new JObject
                {
                    ["Slot"] = i,
                    ["Item"] = item.Instance,
                    ["Amount"] = Mathf.Max(1, item.Amount)
                });
            }

            return new JObject { ["Holsters"] = holsterItems };
        }

        public void Reset()
        {
            _pendingHolsterItems = null;
        }

        public void Restore(JToken data, int savedVersion)
        {
            // The world (and hero) doesn't exist yet.
            _pendingHolsterItems = data?["Holsters"] as JArray;
        }

        /// <summary>
        /// The scene decides which loaded item is part of the world. The event bookkeeping of backpack, holsters and
        /// hands misses cases (e.g. an item taken out of a chest and dropped):
        /// - inside a socket (holster, backpack, chest, loot slot) -> not part of the world. Holster/backpack items
        ///   are inventory, chest/loot slots belong to their container.
        /// - inside a hand -> inventory (VRPlayerService.SetGrab), not part of the world. When we leave the world, the
        ///   hand lets go of it silently: it moves along inside the backpack (see DiscardHandItems()).
        /// - lying around -> part of the world.
        /// Items which aren't loaded right now (culled) keep their state. Child VOBs which were never taken (e.g. an
        /// item lying on a mover) are part of the world through their parent.
        /// </summary>
        private void SyncWorldItems(bool isSaving)
        {
            var handItems = new[] { _vrPlayerService.GrabbedItemLeft, _vrPlayerService.GrabbedItemRight };
            foreach (var vrItem in Object.FindObjectsByType<VRVobItem>(FindObjectsSortMode.None))
            {
                var interactable = vrItem.GetComponent<VrInteractable>();
                var item = GetItem(interactable);
                if (item == null || item.ParentVob != null)
                    continue;

                var isWorldItem = !handItems.Contains(interactable.gameObject) && !interactable.IsHeld;
                if (isWorldItem)
                    _saveGameService.AddVobToWorld(item.Vob);
                else
                    _saveGameService.RemoveVobFromWorld(item.Vob);
            }

            if (!isSaving)
                DiscardHandItems();
        }

        private void ClearHolstersAndHands()
        {
            _silentPlacements.Clear();
            _silentReleases.Clear();

            foreach (var holster in _holsters)
            {
                if (!holster.IsOccupied)
                    continue;

                _silentReleases.Add(holster.Held);
                Discard(holster.Held);
            }

            DiscardHandItems();
        }

        /// <summary>
        /// Hand items are inventory already. Their GameObjects are destroyed without touching the inventory (a world
        /// scene would destroy them otherwise, and the release would remove them from the inventory).
        /// </summary>
        private void DiscardHandItems()
        {
            // UnsetGrab() only changes the inventory of a hand which still references the item.
            var leftHandItem = GetInteractable(_vrPlayerService.GrabbedItemLeft);
            var rightHandItem = GetInteractable(_vrPlayerService.GrabbedItemRight);
            _vrPlayerService.GrabbedItemLeft = null;
            _vrPlayerService.GrabbedItemRight = null;
            Discard(leftHandItem);
            Discard(rightHandItem);
        }

        private void RestorePendingHolsterItems()
        {
            if (_pendingHolsterItems == null)
                return;

            foreach (var entry in _pendingHolsterItems)
                RestoreHolsterItem(entry);

            _pendingHolsterItems = null;
        }

        private void RestoreHolsterItem(JToken entry)
        {
            var holsterIndex = entry.Value<int>("Slot");
            var itemName = entry.Value<string>("Item");
            if (holsterIndex < 0 || holsterIndex >= _holsters.Count || string.IsNullOrEmpty(itemName))
            {
                Logger.LogWarning($"[SaveGame] Holster entry {entry} can't be restored.", LogCat.VR);
                return;
            }

            var vobContainer = _vobService.CreateItem(itemName, Mathf.Max(1, entry.Value<int>("Amount")));
            var interactable = vobContainer.Go.GetComponentInChildren<VrInteractable>();
            if (interactable == null)
                return;

            // Get rid of isKinematic warnings as the Grab is done "physically" (like VRBackpack.RefillSockets).
            vobContainer.Go.GetComponentInChildren<Rigidbody>().isKinematic = false;
            _silentPlacements.Add(interactable);
            if (_holsters[holsterIndex].TryPlace(interactable, true))
                return;

            // The item stays inside the inventory, i.e. it's inside the backpack instead.
            Logger.LogWarning($"[SaveGame] {itemName} couldn't be put into holster {holsterIndex}. It's inside the " +
                              "backpack.", LogCat.VR);
            _silentPlacements.Remove(interactable);
            Discard(interactable);
        }

        /// <summary>
        /// Release the item without touching the inventory and destroy it.
        /// </summary>
        private void Discard(VrInteractable interactable)
        {
            if (interactable == null)
                return;

            interactable.ForceRelease();

            var loader = interactable.GetComponentInParent<VobLoader>();
            var root = loader != null ? loader.gameObject : interactable.gameObject;
            root.SetActive(false);

            // The socket/hand removes the item until the next frame.
            _unityMonoService.StartCoroutine(DestroyNextFrame(root));
        }

        private static IEnumerator DestroyNextFrame(GameObject go)
        {
            yield return null;
            Object.Destroy(go);
        }

        private static VrInteractable GetInteractable(GameObject go)
        {
            return go == null ? null : go.GetComponentInChildren<VrInteractable>();
        }

        private static VobContainer GetItem(VrInteractable interactable)
        {
            var vobContainer = interactable == null ? null : interactable.GetComponentInParent<VobLoader>()?.Container;
            return vobContainer?.Vob?.Type == VirtualObjectType.oCItem ? vobContainer : null;
        }
    }
}
