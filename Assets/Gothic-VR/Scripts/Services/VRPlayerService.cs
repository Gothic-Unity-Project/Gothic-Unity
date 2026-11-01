using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Logging;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Services.Context;
using Gothic.Core;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Contains global states about the VR player (items in hands).
    /// </summary>
    public class VRPlayerService
    {
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly MobInteractionService _mobInteractionService;
        [Inject] private readonly HeroPerceptionService _heroPerceptionService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly VobService _vobService;

        public VRContextInteractionService VRContextInteractionService =>
            _contextInteractionService.GetImpl<VRContextInteractionService>();

        public GameObject GrabbedItemLeft;
        public GameObject GrabbedItemRight;

        public enum HandType
        {
            Left,
            Right
        }

        // Who currently holds an item: hands (per side) and sockets. Decides which collisions the item ignores.
        private readonly Dictionary<VrInteractable, HashSet<(VrGrabberKind, VrHandSide)>> _grabbers = new();

        public bool IsDualGrabbed => GrabbedItemLeft != null && GrabbedItemLeft == GrabbedItemRight;

        public bool IsHeldInHand(VrInteractable grabbable)
        {
            return grabbable != null && (GrabbedItemLeft == grabbable.gameObject
                                         || GrabbedItemRight == grabbable.gameObject);
        }

        private bool IsSocketed(VrInteractable grabbable)
        {
            return _grabbers.TryGetValue(grabbable, out var grabbers)
                   && grabbers.Contains((VrGrabberKind.Socket, VrHandSide.Left));
        }

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
            var container = grabbable.GetComponentInParent<VobLoader>().Container;
            _playerService.PickUpItem(container);
            _heroPerceptionService.OnHeroTookItem(container.Vob);
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
            // Released into a socket (holster, backpack): The socket owns the item (inventory) already or next.
            _playerService.DropItem(grabbable.GetComponentInParent<VobLoader>().Container,
                keepOutOfWorld: IsSocketed(grabbable));
        }

        /// <summary>
        /// Like the engine, a use takes one item (a bite, a read sealed letter): a stack loses one and stays where it is,
        /// the last one leaves the hand, the inventory and the world's item list (found on BoroBongo's branch).
        /// </summary>
        public void UseUpItem(GameObject itemGo)
        {
            var container = itemGo.GetComponentInParent<VobLoader>()?.Container;
            if (container?.Vob is not IItem vobItem)
                return;

            var interactable = container.Go != null ? container.Go.GetComponentInChildren<VrInteractable>() : null;
            var isHeld = interactable != null &&
                         (GrabbedItemLeft == interactable.gameObject || GrabbedItemRight == interactable.gameObject);
            if (vobItem.Amount > 1)
            {
                vobItem.Amount--;
                // A held stack counts as inventory with its amount (SetGrab/UnsetGrab).
                if (isHeld)
                    _playerService.RemoveItem(!string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name);
                Logger.Log($"[VRPlayerService] One {vobItem.Name} used up, {vobItem.Amount} left", LogCat.VR);
                return;
            }

            // Letting go takes it out of the inventory like any dropped item.
            if (isHeld)
                interactable.ForceRelease();
            _vobService.RemoveWorldItem(container);
            Logger.Log($"[VRPlayerService] {vobItem.Name} used up", LogCat.VR);
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
        /// only the dialog anchor next to the mob is VR specific.
        /// </summary>
        public void HandleMobGrab(VobLoader loader)
        {
            var vob = loader.Container?.Vob;
            if (_mobInteractionService.IsItemExchangeMob(vob))
            {
                UseMobWithItem(vob);
                return;
            }

            _mobInteractionService.HandleMobGrab(loader, () => CreateMobDialogAnchor(loader));
        }

        /// <summary>
        /// A pan, forge, anvil, cooling barrel or grindstone: the item it needs is taken from the other hand if it holds
        /// one (it disappears, the new item goes to the inventory), else from the inventory. A held stack (five raw
        /// mutton from a chest) gives one item and stays in the hand with the rest.
        /// </summary>
        private void UseMobWithItem(IVirtualObject mob)
        {
            var neededItem = ((IInteractiveObject)mob).Item;
            var heldItem = FindHeldItem(neededItem);
            var vobLoader = heldItem != null ? heldItem.GetComponentInParent<VobLoader>() : null;
            var heldStack = vobLoader != null ? vobLoader.Container?.VobAs<IItem>() : null;

            // The grab put the whole stack into the inventory, so the used one is taken from there and the stack
            // shrinks by it - releasing it later takes only the rest out again (UnsetGrab).
            if (heldStack != null && heldStack.Amount > 1)
            {
                if (_mobInteractionService.TryExchangeMobItem(mob, false) != null)
                    heldStack.Amount--;
                return;
            }

            if (_mobInteractionService.TryExchangeMobItem(mob, heldItem != null) == null || heldItem == null)
                return;

            // Releasing it on destroy takes it out of the inventory (UnsetGrab). It doesn't lie in the world anymore.
            if (vobLoader != null && vobLoader.Container?.Vob != null)
                _saveGameService.CurrentWorldData.Vobs.Remove(vobLoader.Container.Vob);
            Object.Destroy(vobLoader != null ? vobLoader.gameObject : heldItem);
        }

        private GameObject FindHeldItem(string instanceName)
        {
            foreach (var held in new[] { GrabbedItemLeft, GrabbedItemRight })
            {
                var item = held != null ? held.GetComponentInParent<VobLoader>()?.Container?.VobAs<IItem>() : null;
                var name = item == null ? null : !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
                if (name != null && name.Equals(instanceName, StringComparison.OrdinalIgnoreCase))
                    return held;
            }

            return null;
        }

        /// <summary>
        /// Next to the mob on the player's left (= the mob's right, where NPC dialogs are), at eye height.
        /// </summary>
        private static GameObject CreateMobDialogAnchor(VobLoader loader)
        {
            var cam = Camera.main != null ? Camera.main.transform : loader.transform;

            var bounds = new Bounds(loader.transform.position, Vector3.one * 0.5f);
            var hasBounds = false;
            foreach (var mobRenderer in loader.GetComponentsInChildren<Renderer>())
            {
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
    }
}
