using System;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Inventory;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.Vob;
using Gothic.VR.Adapters.Vob.Container;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// What a VR grab on a mob does (VRMobInteraction is the only caller): use it (MobInteractionService), a
    /// bookstand's page, a lidless container's loot and the dialog anchor next to the mob.
    /// </summary>
    public class VRMobUseService
    {
        [Inject] private readonly MobInteractionService _mobInteractionService;
        [Inject] private readonly DocService _docService;
        [Inject] private readonly InventoryTransferService _inventoryTransferService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private const string _mobDocumentPrefabPath = "Prefabs/UI/MobDocument";

        /// <summary>
        /// The player's head, null before the VR rig is set up.
        /// </summary>
        private Transform Head => _vrPlayerService.Rig?.Head;

        /// <summary>
        /// The hero grabbed a mob (wheel, lever, ...). The logic is platform neutral (MobInteractionService),
        /// only the dialog anchor next to the mob is VR specific. A lidless container only toggles its loot.
        /// </summary>
        public void HandleMobGrab(VobLoader loader)
        {
            if (TryToggleStaticContainerLoot(loader))
                return;

            if (TryCloseMobDocument(loader))
                return;

            _mobInteractionService.HandleMobGrab(loader, () => CreateMobDialogAnchor(loader),
                () => BeginMobDocument(loader));
        }

        /// <summary>
        /// A container without a lid (mummy) is a plain mob: the click shows/hides a loot backpack like a corpse.
        /// Chests with a lid (VRVobContainer) open physically.
        /// </summary>
        private bool TryToggleStaticContainerLoot(VobLoader loader)
        {
            if (loader.Container?.Vob is not IContainer container || !container.IsLidlessContainer())
                return false;

            if (!loader.TryGetComponent<VRStaticContainerLoot>(out var loot))
                loot = loader.gameObject.AddComponent<VRStaticContainerLoot>();
            loot.Toggle(container, _inventoryTransferService.GetOwner(container), Head);
            return true;
        }

        /// <summary>
        /// An open page (bookstand) closes on the next touch.
        /// </summary>
        private bool TryCloseMobDocument(VobLoader loader)
        {
            VRMobDocument openDocument = null;
            foreach (var document in loader.GetComponentsInChildren<VRMobDocument>())
            {
                // A nested VOB (own loader) owns its page.
                if (document.GetComponentInParent<VobLoader>(true) != loader)
                    continue;

                openDocument = document;
                break;
            }
            if (openDocument == null)
                return false;

            UnityEngine.Object.Destroy(openDocument.gameObject);
            Logger.Log($"[MobDialog] Closed the page of '{loader.name}'.", LogCat.Dialog);
            return true;
        }

        /// <summary>
        /// A page next to the mob for a Doc_Show of its "[onStateFunc]_S1" (bookstands). Only created if the script
        /// shows something.
        /// </summary>
        private Action BeginMobDocument(VobLoader loader)
        {
            var isShown = false;
            _docService.SetDocumentTarget(() =>
            {
                // worldPositionStays: a scaled mob doesn't scale the page.
                var documentGo = _resourceCacheService.TryGetPrefabObject(_mobDocumentPrefabPath, "_MobDocument",
                    loader.gameObject);
                if (documentGo == null)
                    return null;

                documentGo.GetComponent<VRMobDocument>().Init(loader, Head);
                isShown = true;
                return documentGo;
            });
            return () =>
            {
                _docService.SetDocumentTarget(null);
                if (isShown)
                    Logger.Log($"[MobDialog] '{loader.name}' shows a page (Doc_Show).", LogCat.Dialog);
            };
        }

        /// <summary>
        /// Next to the mob on the player's left (= the mob's right, where NPC dialogs are), at eye height.
        /// </summary>
        private GameObject CreateMobDialogAnchor(VobLoader loader)
        {
            var head = Head != null ? Head : loader.transform;

            var bounds = new Bounds(loader.transform.position, Vector3.one * 0.5f);
            var hasBounds = false;
            foreach (var mobRenderer in loader.GetComponentsInChildren<Renderer>())
            {
                // VOBs nested below the mob have their own loader. One of Mroczne Tajemnice's (a Gothic 1 mod) anvils
                // has three more below it - the anchor landed hundreds of meters away ("hero farther than 5 m").
                if (mobRenderer.GetComponentInParent<VobLoader>(true) != loader)
                    continue;

                if (!hasBounds)
                {
                    bounds = mobRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(mobRenderer.bounds);
                }
            }

            var right = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
            var position = bounds.center - right * (Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.4f);
            position.y = head.position.y - 0.2f;

            var anchor = new GameObject("MobDialogAnchor");
            anchor.transform.position = position;
            new GameObject("DialogMenuRootPos").transform.SetParent(anchor.transform, false);
            return anchor;
        }
    }
}
