using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Finds the hinge (door wing, chest lid) of a mob.
    /// </summary>
    public static class VrHinges
    {
        /// <summary>
        /// The first IVrHinge on or below the component (inactive GameObjects included). Null if there is none.
        /// </summary>
        public static IVrHinge Find(Component root)
        {
            return root != null ? root.GetComponentInChildren<IVrHinge>(true) : null;
        }
    }
}
