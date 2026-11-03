using UnityEngine;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// Gothic's part of the rig's locomotion near ledges (ledge climbing): the backend asks it on every press of the
    /// jump button, right before jumping, and for every teleport target above a jump's reach.
    /// Set with IVrRig.SetLedgeHandler().
    /// </summary>
    public interface IVrLedgeHandler
    {
        /// <summary>
        /// true: the press did something else (e.g. a comfort climb), the backend doesn't jump.
        /// </summary>
        bool TryHandleJump();

        /// <summary>
        /// Height in m the player shall rise. 0 or less: the backend's normal jump.
        /// </summary>
        float GetJumpRise();

        /// <summary>
        /// The point is on top of a ledge the player can climb from where they stand (teleport target).
        /// </summary>
        bool IsClimbableTop(Vector3 point);
    }
}
