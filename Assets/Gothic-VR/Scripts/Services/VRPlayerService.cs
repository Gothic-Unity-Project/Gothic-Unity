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
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Reflex.Attributes;
using Gothic.VR.Interaction.Ports;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Extensions;
using UnityEngine;
using ZenKit.Vobs;
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
        [Inject] private readonly MobInteractionService _mobInteractionService;

        public VRContextInteractionService VRContextInteractionService =>
            _contextInteractionService.GetImpl<VRContextInteractionService>();

        /// <summary>
        /// The backend's hand visual (the VR hand model) - VRHeroBody's arms reach for it.
        /// </summary>
        public GameObject GetHandModelGo(VrHandSide side)
        {
            var hand = VRContextInteractionService?.Rig?.GetHandVisual(side);
            return hand != null ? hand.gameObject : null;
        }

        // True during any active spell (telekinesis or combat targeting) - blocks NPC dialog.
        public bool IsSpellActive => IsTelekinesisActive || IsSpellTargetingActive;
        public bool IsSpellTargetingActive { get; private set; }

        // Telekinesis state - lives here so it survives VRRuneCaster destruction when one hand releases.
        public bool IsTelekinesisActive { get; private set; }
        public event System.Action TelekinesisDeactivated;

        // Spell targeting state (combat spells - extends the rune hand's distance grab only).
        private VrHandSide _spellTargetingSide;
        private static readonly VrHandSide[] _hands = { VrHandSide.Left, VrHandSide.Right };

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

            foreach (var side in _hands)
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
                foreach (var side in _hands)
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
        /// <summary>
        /// The one owner of the camera height while the hero is down (knocked out, dead): true while a VR hero body
        /// that falls with its camera exists (VrHeroDownPose, option "Fall with the body") - VrKnockoutView lowers the
        /// view itself only without it.
        /// </summary>
        public bool IsFallCameraOnHeroBody { get; set; }

        /// <summary>
        /// The VR hero body lies or stands up (VrHeroDownPose) - crouching isn't sneaking meanwhile (VRHeroMovement).
        /// </summary>
        public bool IsHeroBodyDown { get; set; }

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
            _mobInteractionService.HandleMobGrab(loader, () => CreateMobDialogAnchor(loader));
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
