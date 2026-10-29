using Gothic.Core.Logging;
using Gothic.VR.Interaction.Ports;
using Reflex.Core;
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
    /// on the rig, ClimbInteractable + VrLadderExit on oCMobLadder), block hands (VrBlockHand under each
    /// controller) and physical hands (VrPhysicsHand next to VrBlockHand; held items follow it, contact haptics).
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
    }
}
