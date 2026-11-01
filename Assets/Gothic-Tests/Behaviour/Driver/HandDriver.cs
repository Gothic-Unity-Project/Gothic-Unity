using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;

namespace Gothic.Tests.Behaviour.Driver
{
    /// <summary>
    /// The player's hands without a VR backend: a grab puts the item's body into the hand (kinematic, at the tracked
    /// controller pose of IVrRig) and raises VrInteractable's own grab events, like the backend does for a hand
    /// grabber. From there the game's chain runs as in play: VRVobItem, VRPlayerService.SetGrab (inventory, theft
    /// perception), VRTorchLighter (TorchService.SetHeldTorches). The item doesn't follow a moving hand.
    /// </summary>
    public class HandDriver
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VobService _vobService;

        private readonly Dictionary<VrHandSide, (VrInteractable Interactable, VrGrabArgs Args)> _held = new();


        /// <summary>
        /// The item grabbed last in this scenario, held or not.
        /// </summary>
        public VobContainer LastGrabbed { get; private set; }

        private IVrRig Rig => _vrPlayerService.VRContextInteractionService.Rig ??
                              throw new AssertionException("No VR rig - the scenarios run in VR (the simulator)");

        /// <summary>
        /// A new item of the instance (not a world item, like one out of the backpack), made right at the hand.
        /// </summary>
        public VobContainer CreateItem(string instance, VrHandSide side)
        {
            var hand = GetHand(side);
            return _vobService.CreateItem(new Item
            {
                Name = instance,
                Instance = instance,
                Visual = new VisualMesh(),
                Amount = 1,
                Position = hand.position.ToZkVector(),
                Rotation = hand.rotation.ToZkMatrix()
            });
        }

        public void Grab(VrHandSide side, VobContainer item)
        {
            if (_held.ContainsKey(side))
                throw new AssertionException($"The {side} hand holds something already");

            var interactable = item.Go != null ? VrInteractable.Find(item.Go.transform) : null;
            if (interactable == null)
                throw new AssertionException($"{item.Go?.name ?? item.Vob.Name} can't be grabbed (no VrInteractable, " +
                    "not built yet?)");

            var hand = GetHand(side);
            if (interactable.TryGetComponent<Rigidbody>(out var body))
                body.isKinematic = true;
            interactable.transform.SetPositionAndRotation(hand.position, hand.rotation);

            var args = new VrGrabArgs(VrGrabberKind.Hand, side, interactable, hand.gameObject);
            interactable.RaiseBeforeGrabbed(args);
            interactable.RaiseGrabbed(args);
            _held[side] = (interactable, args);
            LastGrabbed = item;
        }

        /// <summary>
        /// Lets go: the item falls by physics, as dropped.
        /// </summary>
        /// <returns>False if the hand held nothing.</returns>
        public bool Release(VrHandSide side)
        {
            if (!_held.Remove(side, out var held))
                return false;

            if (held.Interactable == null)
                return true;

            held.Interactable.RaiseReleased(held.Args);
            if (held.Interactable.TryGetComponent<Rigidbody>(out var body))
                body.isKinematic = false;
            return true;
        }

        /// <summary>
        /// What the game says the hand holds (VRPlayerService), null for an empty hand.
        /// </summary>
        public VobContainer GetHeld(VrHandSide side)
        {
            var item = side == VrHandSide.Left ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight;
            return item != null ? item.GetComponentInParent<VobLoader>()?.Container : null;
        }

        private Transform GetHand(VrHandSide side)
        {
            return side == VrHandSide.Left ? Rig.LeftHand : Rig.RightHand;
        }
    }
}
