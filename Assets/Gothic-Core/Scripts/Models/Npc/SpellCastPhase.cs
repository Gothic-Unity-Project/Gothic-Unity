namespace Gothic.Core.Models.Npc
{
    public enum SpellCastPhase
    {
        // Mana goes in, one portion per C_Spell.time_per_mana.
        Investing,
        // The invest is over, the spell goes off (MagicService.CommitCast).
        Casting,
        // Ended without effect (no mana, the scripts stopped it, the hero let go too early).
        Stopped,
        Done
    }
}
