using Gothic.Core.Services.Achievements;
using Gothic.Core.Services.World;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// The run's achievement data (run id, save number, tier, counters), sealed. It only captures and restores,
    /// AchievementService owns the run state and decides the run once the world is loaded. A save without it (older
    /// saves) plays as Adanos with a new run id, a broken seal as Beliar. Docs/achievements.md
    /// </summary>
    public class AchievementRunSaveSection : ISaveStateSection
    {
        public string Key => "core.achievements";
        public int Version => 1;

        [Inject] private readonly AchievementService _achievementService;


        /// <summary>
        /// Null while achievements are off (GameSettings.json) or outside a game.
        /// </summary>
        public JToken Capture()
        {
            return _achievementService.CaptureRun();
        }

        public void Reset()
        {
            _achievementService.ResetRun();
        }

        public void Restore(JToken data, int savedVersion)
        {
            _achievementService.RestoreRun(data);
        }
    }
}
