using System;

namespace Gothic.Core.Models.Context
{
    /// <summary>
    /// The player controls a lock can block.
    /// </summary>
    [Flags]
    public enum PlayerControls
    {
        None = 0,
        Move = 1 << 0,
        Turn = 1 << 1,
        Teleport = 1 << 2,
        // Getting away from the spot - turning on it stays possible.
        Walking = Move | Teleport,
        All = Move | Turn | Teleport
    }
}
