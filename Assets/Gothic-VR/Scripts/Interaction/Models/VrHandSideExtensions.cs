using Gothic.Core;

namespace Gothic.VR.Interaction.Models
{
    /// <summary>
    /// The only place where GlobalEventDispatcher.HandSide (fight events) and VrHandSide are mapped.
    /// </summary>
    public static class VrHandSideExtensions
    {
        /// <summary>
        /// false for HandSide.None and HandSide.Both, as they have no single VR hand.
        /// </summary>
        public static bool TryToVrHandSide(this GlobalEventDispatcher.HandSide handSide, out VrHandSide vrHandSide)
        {
            switch (handSide)
            {
                case GlobalEventDispatcher.HandSide.Left:
                    vrHandSide = VrHandSide.Left;
                    return true;
                case GlobalEventDispatcher.HandSide.Right:
                    vrHandSide = VrHandSide.Right;
                    return true;
                default:
                    vrHandSide = default;
                    return false;
            }
        }

        public static GlobalEventDispatcher.HandSide ToHandSide(this VrHandSide vrHandSide)
        {
            return vrHandSide == VrHandSide.Left
                ? GlobalEventDispatcher.HandSide.Left
                : GlobalEventDispatcher.HandSide.Right;
        }
    }
}
