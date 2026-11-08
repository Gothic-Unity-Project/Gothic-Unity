using System.Collections.Generic;
using System.Linq;
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
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Models;
using Reflex.Attributes;
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
        [Inject] private readonly ConfigService _configService;

        // Gothic.ini heroFallMode, read on first use and again when the menu changes it.
        private VrHeroFallMode? _heroFallMode;
        private bool _hasHeroFallBody;

        public VRPlayerService()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
        }

        public VRContextInteractionService VRContextInteractionService =>
            _contextInteractionService.GetImpl<VRContextInteractionService>();

        /// <summary>
        /// The player body of the active VR backend. null before the VR player is set up.
        /// </summary>
        public IVrRig Rig => VRContextInteractionService?.Rig;

        /// <summary>
        /// The direct grabbers of both hands (a distance grab ends in the hand too).
        /// </summary>
        public IEnumerable<IVrGrabber> GetHandGrabbers()
        {
            var rig = Rig;
            return rig == null
                ? Enumerable.Empty<IVrGrabber>()
                : rig.HandGrabbers.Where(i => i.Kind == VrGrabberKind.Hand);
        }

        /// <summary>
        /// The hand that holds the item right now - a direct grabber (Kind Hand), not a distance pull still flying it
        /// in. No allocations: asked every physics step (stab finish).
        /// </summary>
        public bool TryGetHoldingHand(VrInteractable interactable, out IVrGrabber hand)
        {
            hand = null;
            var rig = Rig;
            if (interactable == null || rig == null)
                return false;

            var grabbers = rig.HandGrabbers;
            for (var i = 0; i < grabbers.Count; i++)
            {
                if (grabbers[i].Kind != VrGrabberKind.Hand || grabbers[i].Held != interactable)
                    continue;
                hand = grabbers[i];
                return true;
            }
            return false;
        }

        /// <summary>
        /// The Gothic object (VOB, e.g. an item) the grabber holds. null: it holds nothing or no VOB.
        /// </summary>
        public VobContainer GetHeldContainer(IVrGrabber grabber)
        {
            var held = grabber?.Held;
            return held != null ? held.GetComponentInParent<VobLoader>()?.Container : null;
        }

        /// <summary>
        /// The backend's hand visual (the VR hand model) - VRHeroBody's arms reach for it.
        /// </summary>
        public GameObject GetHandModelGo(VrHandSide side)
        {
            var hand = VRContextInteractionService?.Rig?.GetHandVisual(side);
            return hand != null ? hand.gameObject : null;
        }

        /// <summary>
        /// The player's choice when the game takes the hero's body down - knocked out, dead, asleep (Gothic.ini
        /// [GOTHIC_VR_ACCESSIBILITY] heroFallMode). Its only reader; VrHeroDownPose and VrKnockoutView ask here.
        /// </summary>
        public VrHeroFallMode HeroFallMode
        {
            get
            {
                if (_heroFallMode.HasValue)
                    return _heroFallMode.Value;
                if (_configService.Gothic == null)
                    return (VrHeroFallMode)VRConstants.HeroFallModeDefaultValue;
                _heroFallMode = (VrHeroFallMode)_configService.Gothic.GetInt(VRConstants.IniNames.HeroFallMode,
                    VRConstants.HeroFallModeDefaultValue);
                return _heroFallMode.Value;
            }
        }

        /// <summary>
        /// The one owner of the camera height while the hero is down (knocked out, dead, asleep): true while a VR hero
        /// body that can fall with its camera exists and the player chose to move with it (HeroFallMode) -
        /// VrKnockoutView lowers the view itself (blink) only without it.
        /// </summary>
        public bool IsFallCameraOnHeroBody => _hasHeroFallBody && HeroFallMode == VrHeroFallMode.MoveWithBody;

        /// <summary>
        /// Only VrHeroDownPose writes it (on Bind, SetEnabled, Dispose): a VR hero body exists that can lie down.
        /// </summary>
        public void SetHeroFallBody(bool hasBody)
        {
            _hasHeroFallBody = hasBody;
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == VRConstants.IniNames.HeroFallMode)
                _heroFallMode = null;
        }

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

            // Every item in a hand is the hero's (inventory): NPCs neither detect nor take it.
            var container = grabbable.GetComponentInParent<VobLoader>().Container;
            container.IsHeldByPlayer = true;

            // If we grabbed the element with second hand, return.
            if (IsDualGrabbed)
                return;

            // Otherwise alter inventory count
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

            // Still the hero's while the other hand holds it.
            var container = grabbable.GetComponentInParent<VobLoader>().Container;
            container.IsHeldByPlayer = IsHeldInHand(grabbable);

            // If we removed one hand from our item.
            if (dualGrabPrev)
                return;

            // Otherwise alter inventory count
            // Released into a socket (holster, backpack): The socket owns the item (inventory) already or next.
            _playerService.DropItem(container, keepOutOfWorld: IsSocketed(grabbable));
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
