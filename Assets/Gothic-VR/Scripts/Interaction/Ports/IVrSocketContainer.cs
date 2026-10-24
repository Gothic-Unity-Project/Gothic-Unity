using System.Collections.Generic;
using Gothic.VR.Interaction.Components;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// A group of sockets (backpack inventory, chest content).
    /// </summary>
    public interface IVrSocketContainer
    {
        IReadOnlyList<IVrSocket> Sockets { get; }

        /// <summary>
        /// Whether this container belongs to the active VR backend. Shared prefabs (e.g. chests) carry one container
        /// per backend.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// See SetSilent.
        /// </summary>
        bool IsSilent { get; }

        /// <summary>
        /// Finds the first free socket which accepts the interactable.
        /// </summary>
        bool TryFindFree(VrInteractable interactable, out IVrSocket socket);

        /// <summary>
        /// Places the interactable into the first free socket which accepts it.
        /// </summary>
        /// <param name="silent">If true, no "placed" sound is played.</param>
        bool TryPlace(VrInteractable interactable, bool silent = false);

        /// <summary>
        /// While silent, the sockets play no placed/removed sounds (e.g. while spawning the content of a chest).
        /// </summary>
        void SetSilent(bool isSilent);
    }
}
