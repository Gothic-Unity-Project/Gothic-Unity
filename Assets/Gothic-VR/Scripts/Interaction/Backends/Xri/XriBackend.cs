using Gothic.Core.Logging;
using Gothic.Core.Models.Context;
using Gothic.VR.Interaction.Ports;
using Reflex.Core;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Xri
{
    /// <summary>
    /// XR Interaction Toolkit backend. Always compiled, as XRI is a free package.
    /// Implemented: input, haptics, SFX, UI laser pointers, the player rig (XriRig on "VR - Player XRI.prefab",
    /// with ScreenFade/WallFade on its camera), the XR Interaction Simulator and item grabbing (Near-Far interactor
    /// per hand, GothicGrabInteractable + XriInteractableBridge on oCItem), using mobs/NPCs and the socket core
    /// (VrSocketSlot + XriSocketBridge, "VR Socket Slot.prefab"), the backpack ("BackPack XRI.prefab") on the
    /// shoulder and the hip/shoulder holsters (VrBodyAnchor "Shoulders"/"Waist" on the rig), chest slots
    /// (VrChestSlots, lid opened by hand: VrHinge), NPC loot slots, doors (VrHinge) and ladders (ClimbProvider
    /// on the rig, ClimbInteractable + VrLadderExit on oCMobLadder).
    /// Missing: hands (5).
    /// </summary>
    public class XriBackend : IVrBackend
    {
        public string Name => "XR Interaction Toolkit";
        public VrInteractionBackend Kind => VrInteractionBackend.Xri;

        public void InstallBindings(ContainerBuilder containerBuilder)
        {
            Logger.LogWarning("XRI backend is work in progress: rig, locomotion, input, haptics, UI laser pointers, " +
                              "screen fades, the XR Interaction Simulator, grabbing (items, mobs, NPCs), the " +
                              "backpack, holsters, chest slots, doors, chest lids, ladders and NPC loot work. " +
                              "Hands (5) are missing.", LogCat.VR);

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
