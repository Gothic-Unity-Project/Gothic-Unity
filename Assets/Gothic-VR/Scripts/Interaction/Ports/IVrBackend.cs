using Gothic.VR.Interaction.Components;
using Reflex.Core;
using UnityEngine;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// A VR interaction framework that provides the implementations of our VR ports.
    /// The backend is created by ReflexVRSceneInstaller.
    /// </summary>
    public interface IVrBackend
    {
        string Name { get; }

        /// <summary>
        /// Registers the backend's implementations of IVrInput, IVrHaptics, IVrUiPointers and IVrSfxPlayer.
        /// IVrRig isn't registered here, as it lives on the player prefab.
        /// </summary>
        void InstallBindings(ContainerBuilder containerBuilder);

        /// <summary>
        /// Whether the rig (on a player prefab in Player.unity / Lab.unity) belongs to this backend.
        /// Used to pick the backend's player prefab. null (e.g., a rig whose scripts are missing) returns false.
        /// </summary>
        bool OwnsRig(IVrRig rig);

        /// <summary>
        /// Makes a part of a Gothic model a handle that a hand grabs at runtime (a water pipe's mouthpiece): adds a
        /// VrInteractable and the backend's parts to go. The grab holds it without moving it - the game moves it
        /// itself, like VrHinge a door - and only a hand right at it takes it, one hand at a time. The hands grab it
        /// by grabTrigger, a trigger collider on go, which a mob's own interactable above doesn't count.
        /// go must be inactive: inject and activate it afterwards. null if the backend can't (logged by the caller).
        /// </summary>
        VrInteractable AddHandle(GameObject go, Collider grabTrigger)
        {
            return null;
        }
    }
}
