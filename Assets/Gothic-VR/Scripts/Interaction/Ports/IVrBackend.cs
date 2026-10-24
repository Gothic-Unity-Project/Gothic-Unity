using Reflex.Core;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// A VR interaction framework that provides the implementations of our VR ports.
    /// The active backend is selected by VRContextBootstrap.
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
    }
}
