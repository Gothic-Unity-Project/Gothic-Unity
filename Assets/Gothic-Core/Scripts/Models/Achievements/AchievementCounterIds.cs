namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// Core's counter ids for AchievementService.Increment()/AddDistinct(). Modules prefix their own ("vr.*",
    /// "{modId}.*"), so a new counter needs no change in Core.
    /// </summary>
    public static class AchievementCounterIds
    {
        /// <summary>A lock opened with a lock pick (not with its key).</summary>
        public const string LockpickSuccess = "lockpick.success";

        /// <summary>Distinct: a document (book, letter, map) was shown. Key = DocModel.Identity.</summary>
        public const string DocumentShown = "document.shown";
    }
}
