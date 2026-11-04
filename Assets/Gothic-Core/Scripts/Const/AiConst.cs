namespace Gothic.Core.Const
{
    /// <summary>
    /// Tunable values of the NPC AI (senses, perceptions, AI_* actions). Decisions behind them:
    /// Docs/adr/2026-10-31-npc-perceptions.md
    /// </summary>
    public static class AiConst
    {
        // Senses
        public const float HearRangeFactor = 0.5f; // Hearing reaches half the senses range (tuned in play)
        public const float SmellRangeMeters = 3f; // Smell reaches 3 m (tuned in play)

        // Perceptions
        // PERC_MOVENPC: cap so Gothic's PERC_DIST_DIALOG (5 m) doesn't make NPCs react from meters away in VR.
        public const float MoveNpcMaxRangeMeters = 1f;

        // AI_* actions
        public const float QuickLookSeconds = 2f; // AI_Quicklook: the head turns back by itself after this time
        public const float DetectItemRadiusMeters = 20f; // Wld_DetectItem search radius
    }
}
