using Gothic.Core.Manager;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Sits on the player rig and listens to the hand and distance grabbers of both hands.
    /// </summary>
    public class VRPlayerItemAdapter : MonoBehaviour
    {
        [Inject] private readonly MarvinService _marvinService;

        private IVrRig _rig;


        private void Awake()
        {
            _rig = GetComponent<IVrRig>();
            foreach (var grabber in _rig.HandGrabbers)
                grabber.BeforeGrabbed += OnBeforeGrabbed;
        }

        private void OnDestroy()
        {
            if (_rig == null)
                return;

            foreach (var grabber in _rig.HandGrabbers)
                grabber.BeforeGrabbed -= OnBeforeGrabbed;
        }

        /// <summary>
        /// We need to set isKinematic=false in BeforeGrabbed, otherwise we get a warning at OnGrabbed event time afterward.
        /// </summary>
        private void OnBeforeGrabbed(VrGrabArgs args)
        {
            // Ignore grabbing once, if MarvinSelectionMode is active.
            if (_marvinService.IsMarvinSelectionMode)
                return;

            // OnGrabbed is normally called multiple times. Even after an object is already socketed. If so, then let's stop Grab behaviour.
            // If we sock an object on our hips or backpack etc.
            if (args.Kind == VrGrabberKind.Socket || args.Interactable == null)
                return;

            // In Gothic, Items have no physics when lying around. We need to activate physics for HVR to properly move items into our hands.
            if (args.Interactable.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = false;
            }
        }

    }
}
