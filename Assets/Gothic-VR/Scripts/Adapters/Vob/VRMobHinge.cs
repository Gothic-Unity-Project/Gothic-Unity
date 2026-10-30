using Gothic.Core.Models.Container;
using Gothic.VR.Interaction.Ports;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// The hinge of a door/chest lid follows the mob's current state (VobContainer.MobState, saved by
    /// MobStatesSaveSection): it's created open in state 1, and opening/closing it by hand sets the state.
    /// </summary>
    public static class VRMobHinge
    {
        public static void Follow(IVrHinge hinge, VobContainer mob)
        {
            if (hinge == null || mob == null)
                return;

            if (mob.MobState > 0)
                hinge.OpenInstantly();

            hinge.Opened += () => mob.MobState = 1;
            hinge.Closed += () => mob.MobState = 0;
        }
    }
}
