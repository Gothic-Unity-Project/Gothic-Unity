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
        Grab = 1 << 3,
        // Getting away from the spot - turning on it stays possible.
        Walking = Move | Teleport,
        // Staying on the spot - the hands stay free.
        InPlace = Move | Turn | Teleport,
        All = Move | Turn | Teleport | Grab
    }
}
