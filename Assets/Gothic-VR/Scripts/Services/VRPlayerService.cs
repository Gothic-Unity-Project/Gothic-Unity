using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Services.Context;
using Gothic.Core;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Manager;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.VR.Adapters.Vob;
using Gothic.VR.Adapters.Vob.Container;
using Gothic.Core.Models.Container;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using EventType = ZenKit.EventType;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Contains global states about the VR player (items in hands).
    /// </summary>
    public class VRPlayerService
    {
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly DocService _docService;
        [Inject] private readonly MobInteractionService _mobInteractionService;

        public VRContextInteractionService VRContextInteractionService =>
            _contextInteractionService.GetImpl<VRContextInteractionService>();

        public GameObject GrabbedItemLeft;
        public GameObject GrabbedItemRight;

        private IVrInput _vrInput;

        /// <summary>
        /// Keyboard and mouse instead of controllers (IVrInput.IsSimulator).
        /// </summary>
        public bool IsSimulator => (_vrInput ??= ReflexProjectInstaller.DIContainer.Resolve<IVrInput>()).IsSimulator;

        public enum HandType
        {
            Left,
            Right
        }

        // Who currently holds an item: hands (per side) and sockets. Decides which collisions the item ignores.
        private readonly Dictionary<VrInteractable, HashSet<(VrGrabberKind, VrHandSide)>> _grabbers = new();

        public bool IsDualGrabbed => GrabbedItemLeft != null && GrabbedItemLeft == GrabbedItemRight;

        /// <summary>
        /// An item was grabbed by a hand (directly or by distance). Sockets and other grabbers are ignored.
        /// </summary>
        public void SetGrab(VrGrabArgs args)
        {
            if (!args.IsHandOrDistance)
                return;

            var grabbable = args.Interactable;
            if (args.HandSide == VrHandSide.Left)
            {
                // If we did remote grabbing, this function is called twice (remote grabber+hand grabber).
                // As we already count remote grabbing as "in inventory", we skip it the second time.
                if (GrabbedItemLeft == grabbable.gameObject)
                    return;

                GrabbedItemLeft = grabbable.gameObject;
            }
            else
            {
                // If we did remote grabbing, this function is called twice (remote grabber+hand grabber).
                // As we already count remote grabbing as "in inventory", we skip it the second time.
                if (GrabbedItemRight == grabbable.gameObject)
                    return;

                GrabbedItemRight = grabbable.gameObject;
            }

            // If we grabbed the element with second hand, return.
            if (IsDualGrabbed)
                return;

            // Otherwise alter inventory count
            var vobItem = grabbable.GetComponentInParent<VobLoader>()?.Container.VobAs<IItem>();
            if (vobItem == null) return;
            var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
            _playerService.AddItem(instanceName, vobItem.Amount);
        }

        /// <summary>
        /// An item was released by a hand (directly or by distance). Sockets and other grabbers are ignored.
        /// </summary>
        public void UnsetGrab(VrGrabArgs args)
        {
            if (!args.IsHandOrDistance)
                return;

            var dualGrabPrev = IsDualGrabbed;
            var grabbable = args.Interactable;
            if (args.HandSide == VrHandSide.Left)
            {
                // If we did remote grabbing, this function is called twice (remote grabber+hand grabber).
                // As we already count remote grabbing as "in inventory", we skip it the second time.
                if (GrabbedItemLeft == null)
                    return;

                GrabbedItemLeft = null;
            }
            else
            {
                // If we did remote grabbing, this function is called twice (remote grabber+hand grabber).
                // As we already count remote grabbing as "in inventory", we skip it the second time.
                if (GrabbedItemRight == null)
                    return;

                GrabbedItemRight = null;
            }

            // If we removed one hand from our item.
            if (dualGrabPrev)
                return;

            // Otherwise alter inventory count
            var vobItem = grabbable.GetComponentInParent<VobLoader>()?.Container.VobAs<IItem>();
            if (vobItem == null) return;
            var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
            _playerService.RemoveItem(instanceName, vobItem.Amount);
        }

        /// <summary>
        /// The player collides with VobItems (stuck at a sword in the ground like in Gothic). Items in our hands or
        /// sockets (hips, back) would push our own CharacterController, so they ignore it while being held.
        ///
        /// Physical hands (like HVR): only the hand holding an item ignores it (a held sword would push the hand
        /// holding it). The other hand still collides with it and can push it. Socketed items ignore both hands,
        /// otherwise reaching for the hip or backpack pushes them around.
        /// </summary>
        public void UpdatePlayerCollision(VrGrabArgs args, bool isGrabbed)
        {
            var grabbable = args.Interactable;
            if (!_grabbers.TryGetValue(grabbable, out var grabbers))
            {
                grabbers = new HashSet<(VrGrabberKind, VrHandSide)>();
                _grabbers[grabbable] = grabbers;
            }

            // Sockets and other grabbers count as one (their HandSide is meaningless).
            var key = args.IsHandOrDistance ? (args.Kind, args.HandSide) : (VrGrabberKind.Socket, VrHandSide.Left);
            if (isGrabbed)
                grabbers.Add(key);
            else
                grabbers.Remove(key);

            if (grabbers.Count == 0)
                _grabbers.Remove(grabbable);

            var isSocketed = grabbers.Contains((VrGrabberKind.Socket, VrHandSide.Left));
            var context = VRContextInteractionService;
            foreach (var hand in context.PhysicsHands)
            {
                var isHolding = grabbers.Contains((VrGrabberKind.Hand, hand.Side))
                                || grabbers.Contains((VrGrabberKind.Distance, hand.Side));
                hand.IgnoreCollisions(grabbable.Colliders, isSocketed || isHolding);
            }

            var characterController = context.Rig?.CharacterController;
            if (characterController == null)
                return;

            var ignoreCharacterController = grabbers.Count > 0;
            foreach (var itemCollider in grabbable.Colliders)
            {
                if (itemCollider != null)
                    Physics.IgnoreCollision(characterController, itemCollider, ignoreCharacterController);
            }
        }

        /// <summary>
        /// The item is destroyed (e.g. put into a chest or eaten) while it might still be held.
        /// </summary>
        public void ForgetPlayerCollision(VrInteractable grabbable)
        {
            _grabbers.Remove(grabbable);
        }

        /// <summary>
        /// The hero grabbed a mob (wheel, lever, ...). The logic is platform neutral (MobInteractionService),
        /// only the dialog anchor next to the mob (and a bookstand's page) is VR specific.
        /// </summary>
        public void HandleMobGrab(VobLoader loader)
        {
            if (TryCloseMobDocument(loader))
                return;

            _mobInteractionService.HandleMobGrab(loader,
                () => CreateMobDialogAnchor(loader, _configService.Dev.EnableMobDialogOwnBounds),
                () => BeginMobDocument(loader));
        }

        /// <summary>
        /// A container without a lid (mummy) is a plain mob: the click shows/hides a loot backpack like a corpse.
        /// Chests with a lid (VRVobContainer) open physically.
        /// </summary>
        public void TryToggleStaticContainerLoot(VobLoader loader, IContainer container)
        {
            if (loader.GetComponentInChildren<VRVobContainer>(true) != null)
                return;

            if (!loader.TryGetComponent<VRStaticContainerLoot>(out var loot))
                loot = loader.gameObject.AddComponent<VRStaticContainerLoot>();
            loot.Toggle(container);
        }

        /// <summary>
        /// DeveloperConfig.EnableMobDocuments: an open page (bookstand) closes on the next touch.
        /// </summary>
        private bool TryCloseMobDocument(VobLoader loader)
        {
            if (!_configService.Dev.EnableMobDocuments)
                return false;

            var openDocument = loader.transform.Find(VRMobDocument.AnchorName);
            if (openDocument == null)
                return false;

            UnityEngine.Object.Destroy(openDocument.gameObject);
            Logger.Log($"[MobDialog] Closed the page of '{loader.name}'.", LogCat.Dialog);
            return true;
        }

        /// <summary>
        /// DeveloperConfig.EnableMobDocuments: a page next to the mob for a Doc_Show of its "[onStateFunc]_S1"
        /// (bookstands). Removed again if the script showed nothing.
        /// </summary>
        private Action BeginMobDocument(VobLoader loader)
        {
            if (!_configService.Dev.EnableMobDocuments)
                return null;

            var document = VRMobDocument.Create(loader.gameObject);
            _docService.PendingItemGo = document.gameObject;
            return () =>
            {
                _docService.PendingItemGo = null;
                if (document.HasPages)
                    Logger.Log($"[MobDialog] '{loader.name}' shows a page (Doc_Show).", LogCat.Dialog);
                else
                    UnityEngine.Object.Destroy(document.gameObject);
            };
        }

        /// <summary>
        /// Next to the mob on the player's left (= the mob's right, where NPC dialogs are), at eye height.
        /// </summary>
        private static GameObject CreateMobDialogAnchor(VobLoader loader, bool isOwnBoundsOnly)
        {
            var cam = Camera.main != null ? Camera.main.transform : loader.transform;

            var bounds = new Bounds(loader.transform.position, Vector3.one * 0.5f);
            var hasBounds = false;
            foreach (var mobRenderer in loader.GetComponentsInChildren<Renderer>())
            {
                // DeveloperConfig.EnableMobDialogOwnBounds: VOBs nested below the mob have their own loader. One of
                // Mroczne Tajemnice's anvils has three more below it - the anchor landed hundreds of meters away
                // ("hero farther than 5 m").
                if (isOwnBoundsOnly && mobRenderer.GetComponentInParent<VobLoader>(true) != loader)
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

            var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            var position = bounds.center - right * (Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.4f);
            position.y = cam.position.y - 0.2f;

            var anchor = new GameObject("MobDialogAnchor");
            anchor.transform.position = position;
            new GameObject("DialogMenuRootPos").transform.SetParent(anchor.transform, false);
            return anchor;
        }

        /// <summary>
        /// The hand as the player sees it (IVrRig.GetHandVisual).
        /// </summary>
        public GameObject GetHandModelGo(VrHandSide side)
        {
            var hand = VRContextInteractionService?.Rig?.GetHandVisual(side);
            return hand != null ? hand.gameObject : null;
        }

        // True during any active spell (telekinesis or combat targeting) — blocks NPC dialog.
        public bool IsSpellActive => IsTelekinesisActive || IsSpellTargetingActive;
        public bool IsSpellTargetingActive { get; private set; }

        // Telekinesis state — lives here so it survives VRRuneCaster destruction when one hand releases.
        public bool IsTelekinesisActive { get; private set; }
        public event System.Action TelekinesisDeactivated;

        // Spell targeting state (combat spells - extends the rune hand's distance grab only).
        private VrHandSide _spellTargetingSide;

        private IVrRig Rig => VRContextInteractionService?.Rig;

        /// <summary>
        /// Combat spells aim with the rune hand's distance grab (IVrRig.GetDistanceHovered), extended to the spell's
        /// range.
        /// </summary>
        public void ActivateSpellTargeting(VrHandSide runeHandSide, float range)
        {
            if (IsSpellTargetingActive || Rig == null)
                return;
            _spellTargetingSide = runeHandSide;
            Rig.SetDistanceGrabRange(runeHandSide, range);
            Logger.Log($"[SpellTarget] {runeHandSide} hand distance grab extended to {range} m", LogCat.VR);
            IsSpellTargetingActive = true;
        }

        public void DeactivateSpellTargeting()
        {
            if (!IsSpellTargetingActive)
                return;
            // Telekinesis may still use the longer range.
            if (!IsTelekinesisActive)
                Rig?.ResetDistanceGrabRange(_spellTargetingSide);
            IsSpellTargetingActive = false;
        }

        /// <summary>
        /// The NPC the hand's distance grab points at (spell targeting), null if none.
        /// </summary>
        public NpcContainer GetSpellTarget(VrHandSide side)
        {
            var hovered = Rig?.GetDistanceHovered(side);
            var npcLoader = hovered != null ? hovered.GetComponentInParent<NpcLoader>() : null;
            return npcLoader != null && npcLoader.Npc != null ? npcLoader.Npc.GetUserData() : null;
        }

        /// <summary>
        /// Both hands' distance grab reaches the spell's range - items far away are highlighted (VRFocus) and pulled
        /// with TryTelekinesisGrab. Ends with the first item pulled into a hand.
        /// </summary>
        public void ActivateTelekinesis(float range)
        {
            if (IsTelekinesisActive || Rig == null)
                return;

            foreach (var side in new[] { VrHandSide.Left, VrHandSide.Right })
            {
                Rig.SetDistanceGrabRange(side, range);
                Logger.Log($"[Telekinesis] {side} hand distance grab extended to {range} m", LogCat.VR);
            }
            foreach (var grabber in Rig.HandGrabbers)
                grabber.Grabbed += OnTelekinesisItemGrabbed;

            IsTelekinesisActive = true;
        }

        public void DeactivateTelekinesis()
        {
            if (!IsTelekinesisActive)
                return;

            var rig = Rig;
            if (rig != null)
            {
                foreach (var side in new[] { VrHandSide.Left, VrHandSide.Right })
                {
                    if (!IsSpellTargetingActive || side != _spellTargetingSide)
                        rig.ResetDistanceGrabRange(side);
                }
                foreach (var grabber in rig.HandGrabbers)
                    grabber.Grabbed -= OnTelekinesisItemGrabbed;
            }

            IsTelekinesisActive = false;
            TelekinesisDeactivated?.Invoke();
        }

        public bool TryTelekinesisGrab()
        {
            var rig = Rig;
            return rig != null && (rig.TryDistanceGrabHovered(VrHandSide.Right) ||
                                   rig.TryDistanceGrabHovered(VrHandSide.Left));
        }

        private void OnTelekinesisItemGrabbed(VrGrabArgs args)
        {
            if (args.Kind == VrGrabberKind.Distance)
                DeactivateTelekinesis();
        }
    }
}
