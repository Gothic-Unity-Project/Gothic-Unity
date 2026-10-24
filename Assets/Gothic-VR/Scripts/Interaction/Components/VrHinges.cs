using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Finds the hinge (door wing, chest lid) of the active VR backend. Shared prefabs may carry one per backend.
    /// </summary>
    public static class VrHinges
    {
        /// <summary>
        /// The first active IVrHinge on or below the component. Null if none of them is active (e.g. a door on a
        /// backend without hinges yet).
        /// </summary>
        public static IVrHinge FindActive(Component root)
        {
            if (root == null)
                return null;

            foreach (var hinge in root.GetComponentsInChildren<IVrHinge>(true))
            {
                if (hinge.IsActive)
                    return hinge;
            }

            return null;
        }
    }
}
