using Gothic.Core.Models.Container;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using ZenKit.Daedalus;

namespace Gothic.VR.Models.FavoritesRing
{
    /// <summary>
    /// A held item touching a favorites ring slot: what letting go there would do (VrFavoritesService.TryPutIn).
    /// </summary>
    public class FavoritesRingPutInCandidate
    {
        public VobContainer Container;
        public ItemInstance Item;
        public string InstanceName;
        public FavoriteSlotKind Kind;
        public FavoritesRingSlotView Target;
        public FavoritesPutInAction Action;
        public ItemInstance Replaced;
        public float Time;
        public VrHandSide Side;
        public VrInteractable Grabbable;
    }
}
