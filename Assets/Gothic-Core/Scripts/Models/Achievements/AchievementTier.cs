namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// The tier a run plays in (Docs/achievements.md, "Tiers"). A higher value is a better tier: an unlock earned in
    /// a better tier later replaces the one of a worse tier.
    /// </summary>
    public enum AchievementTier
    {
        /// <summary>Marvin mode was on in this run.</summary>
        Beliar = 0,

        /// <summary>Normal play with saves and loads.</summary>
        Adanos = 1,

        /// <summary>No cheats and the run never went back to an earlier save.</summary>
        Innos = 2
    }
}
