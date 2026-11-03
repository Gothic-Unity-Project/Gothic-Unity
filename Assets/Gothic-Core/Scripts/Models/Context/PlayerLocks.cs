using System;

namespace Gothic.Core.Models.Context
{
    /// <summary>
    /// Who keeps the player from moving right now. The player moves freely again once every owner unlocked.
    /// </summary>
    [Flags]
    public enum PlayerLocks
    {
        None = 0,
        Loading = 1 << 0,
        Dialog = 1 << 1,
        Seat = 1 << 2,
        Cinema = 1 << 3,
        Knockout = 1 << 4,
        Death = 1 << 5,
        // A spell's state on the hero (sleep, freeze) - HeroSpellStateDomain.
        SpellState = 1 << 6
    }
}
