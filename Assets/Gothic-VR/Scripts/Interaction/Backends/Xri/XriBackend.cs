using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using Reflex.Core;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// XR Interaction Toolkit backend. Always compiled, as XRI is a free package.
    /// Implemented: input, haptics, SFX, UI laser pointers, the player rig (XriRig on "VR - Player XRI.prefab",
    /// with ScreenFade/WallFade on its camera), the desktop simulator and item grabbing (Near-Far interactor
    /// per hand, GothicGrabInteractable + XriInteractableBridge on oCItem), using mobs/NPCs and the socket core
    /// (VrSocketSlot + XriSocketBridge, "VR Socket Slot.prefab"), the backpack ("BackPack XRI.prefab") on the
    /// shoulder and the hip/shoulder holsters (VrBodyAnchor "Shoulders"/"Waist" on the rig), chest slots
    /// (VrChestSlots, lid opened by hand: VrHinge), NPC loot slots, doors (VrHinge) and ladders (ClimbProvider
    /// on the rig, ClimbInteractable + VrLadderExit on oCMobLadder), ledges (VrLedgeReach on the rig moves a
    /// ClimbInteractable ("VR Ledge Anchor XRI.prefab") under the hands, VrLedgeLocomotion makes GothicJumpProvider
    /// jump as high as Gothic's ledges need and lets teleports reach climbable tops), block hands (VrBlockHand
    /// under each controller), physical hands (VrPhysicsHand next to VrBlockHand; held items follow it, contact
    /// haptics) and handles on Gothic models made at runtime (AddHandle: a water pipe's mouthpiece).
    /// Missing: teleport/ladder/swim handling of physical hands (5d).
    /// </summary>
    public class XriBackend : IVrBackend
    {
        public string Name => "XR Interaction Toolkit";

        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            Logger.LogWarning("XRI backend is work in progress: rig, locomotion, input, haptics, UI laser pointers, " +
                              "screen fades, the desktop simulator, grabbing (items, mobs, NPCs), the " +
                              "backpack, holsters, chest slots, doors, chest lids, ladders, NPC loot, block hands " +
                              "and physical hands (held items, contact haptics, teleports, ladders, swimming) " +
                              "work.", LogCat.VR);

            containerBuilder.AddSingleton(typeof(XriInput), typeof(IVrInput), typeof(XriInput));
            containerBuilder.AddSingleton(typeof(XriHaptics), typeof(IVrHaptics));
            containerBuilder.AddSingleton(typeof(XriUiPointers), typeof(IVrUiPointers));
            containerBuilder.AddSingleton(typeof(XriSfxPlayer), typeof(IVrSfxPlayer));
        }

        public bool OwnsRig(IVrRig rig)
        {
            return rig is XriRig;
        }

        /// <summary>
        /// VrInteractable, a GothicGrabInteractable set up as a handle (its Rigidbody comes with it) and the bridge -
        /// in this order, as their Awake (on activation) looks for each other. The XR interactable stays disabled until
        /// the bridge's Start has collected the grab trigger (like in the prefabs).
        /// </summary>
        public VrInteractable AddHandle(GameObject go, Collider grabTrigger)
        {
            if (go == null || grabTrigger == null || go.activeSelf)
            {
                var goName = go != null ? go.name : "none";
                Logger.LogError($"[XRI] AddHandle needs an inactive GameObject and a grab trigger ({goName}).",
                    LogCat.VR);
                return null;
            }

            var interactable = go.AddComponent<VrInteractable>();
            var grab = go.AddComponent<GothicGrabInteractable>();
            grab.enabled = false;
            grab.SetUpAsHandle();
            go.AddComponent<XriInteractableBridge>().SetGrabTriggers(grabTrigger);
            return interactable;
        }
    }
}
