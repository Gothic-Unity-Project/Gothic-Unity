using Gothic.Core.Models.Container;
using Gothic.VR.Interaction.Models;

namespace Gothic.VR.Models.FavoritesRing
{
    /// <summary>
    /// A stack being taken out of a favorites ring slot into a hand, more and more while the grip is held.
    /// </summary>
    public class FavoritesRingTransfer
    {
        public FavoritesRingSlotView Slot;
        public VobContainer Held;
        public VrHandSide Side;
        public float StartTime;
        public float Carry;
    }
}
