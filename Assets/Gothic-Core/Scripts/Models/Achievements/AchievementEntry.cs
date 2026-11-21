namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// Read model for the UI (toast, menu page in VR and Flat): one achievement with its texts in the current
    /// language and its state in the profile. Built by AchievementService.GetEntries().
    /// </summary>
    public class AchievementEntry
    {
        public AchievementDefinition Definition;
        public string Id;
        public string Title;
        public string Description;

        /// <summary>
        /// A texture of the installation, or null (placeholder). Item icons aren't rendered yet.
        /// </summary>
        public string IconTexture;

        /// <summary>
        /// An item instance drawn as the icon (ItemIconService), or null. Wins over IconTexture when it renders.
        /// </summary>
        public string IconItem;

        /// <summary>
        /// Show "?" instead of title and description while it's locked.
        /// </summary>
        public bool IsHidden;

        /// <summary>
        /// A script symbol it needs is missing (mod, renamed script) - it can't be earned.
        /// </summary>
        public bool IsUnavailable;

        public bool IsUnlocked;

        /// <summary>
        /// Only meaningful if IsUnlocked.
        /// </summary>
        public AchievementTier Tier;

        /// <summary>
        /// False: the file was edited or copied (broken seal) - greyed out with a broken seal, not counted.
        /// </summary>
        public bool IsSealed;

        public string UnlockedDateUtc;

        /// <summary>
        /// For counters (e.g. 7/20). ProgressTarget 0 = no progress to show.
        /// </summary>
        public int ProgressCurrent;

        public int ProgressTarget;
    }
}
