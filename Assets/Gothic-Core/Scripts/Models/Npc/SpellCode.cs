namespace Gothic.Core.Models.Npc
{
    /// <summary>
    /// What Spell_ProcessMana and Spell_ProcessMana_Release answer (SPL_* in G1 Spells_Params.d, G2 Constants.d).
    /// </summary>
    public enum SpellCode
    {
        // No more mana goes in. The hero keeps it ready until he lets go, an NPC casts.
        DontInvest = 0,
        ReceiveInvest = 1,
        SendCast = 2,
        // Ends the cast without effect.
        SendStop = 3,
        NextLevel = 4,
        // G2 charge spells: invest without the engine taking mana.
        CanInvestNoManaDec = 8
    }
}
