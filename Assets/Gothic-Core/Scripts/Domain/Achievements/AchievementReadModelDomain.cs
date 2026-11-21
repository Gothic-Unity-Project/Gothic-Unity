using System;
using System.Collections.Generic;
using Gothic.Core.Models.Achievements;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// Builds the read model VR and Flat UI share (AchievementEntry) from the definitions, the profile and the run.
    /// Pure: no ZenKit, no Unity.
    /// </summary>
    public class AchievementReadModelDomain
    {
        private readonly AchievementConditionDomain _conditionDomain = new();


        /// <param name="reader">Null outside a world: item progress shows 0 then.</param>
        public List<AchievementEntry> CreateEntries(List<AchievementDefinition> definitions,
            AchievementProfileData profile, AchievementRunState run, IAchievementStateReader reader, string language)
        {
            var entries = new List<AchievementEntry>(definitions.Count);
            run ??= new AchievementRunState();
            foreach (var definition in definitions)
            {
                var unlock = profile.FindUnlock(definition.Id);
                var isSealedUnlock = unlock != null && unlock.IsSealed;
                var entry = new AchievementEntry
                {
                    Definition = definition,
                    Id = definition.Id,
                    Title = definition.Title.Get(language),
                    Description = definition.Description.Get(language),
                    IconTexture = string.IsNullOrEmpty(definition.Icon.Texture) ? null : definition.Icon.Texture,
                    IconItem = string.IsNullOrEmpty(definition.Icon.Item) ? null : definition.Icon.Item,
                    IsHidden = definition.IsHidden,
                    IsUnavailable = definition.IsUnavailable,
                    IsUnlocked = unlock != null,
                    Tier = unlock?.Tier ?? AchievementTier.Beliar,
                    IsSealed = isSealedUnlock,
                    UnlockedDateUtc = unlock?.DateUtc
                };

                var progress = definition.ProgressCondition;
                if (progress != null)
                {
                    entry.ProgressTarget = progress.Count;
                    entry.ProgressCurrent = isSealedUnlock
                        ? progress.Count
                        : Math.Min(progress.Count, _conditionDomain.GetProgress(progress, reader, run));
                }

                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>
        /// Sealed unlocks (unsealed ones don't count for completion) out of all definitions.
        /// </summary>
        public void GetCompletion(List<AchievementDefinition> definitions, AchievementProfileData profile,
            out int unlocked, out int total)
        {
            unlocked = 0;
            total = definitions.Count;
            foreach (var definition in definitions)
            {
                var unlock = profile.FindUnlock(definition.Id);
                if (unlock != null && unlock.IsSealed)
                    unlocked++;
            }
        }
    }
}
