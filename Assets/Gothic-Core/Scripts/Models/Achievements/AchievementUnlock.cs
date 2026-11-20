using Newtonsoft.Json;

namespace Gothic.Core.Models.Achievements
{
    /// <summary>
    /// An earned achievement in the profile: in which tier, when, in which run and after which save.
    /// Chain is the seal of this entry and the sealed entries before it (AchievementSealDomain).
    /// </summary>
    public class AchievementUnlock
    {
        public string Id;
        public AchievementTier Tier;
        public string DateUtc;
        public string RunId;
        public int SaveNo;
        public string Chain;

        /// <summary>
        /// Checked when the profile is loaded (or set when earned in this session). Unsealed entries stay visible,
        /// don't count for completion and are written without Chain until they're earned again.
        /// </summary>
        [JsonIgnore]
        public bool IsSealed;
    }
}
