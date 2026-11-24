using Gothic.Core.Services.Achievements;
using Gothic.Core.Services.World;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// The run's achievement data (tier, counters). It only captures and restores, AchievementService owns the run
    /// state and decides the run once the world is loaded. A save without it (older saves) plays as Adanos. The save
    /// side (new game or not) is told from here, AchievementService doesn't know SaveGameService.
    /// Docs/achievements.md
    /// </summary>
    public class AchievementRunSaveSection : ISaveStateSection
    {
        public string Key => "core.achievements";
        public int Version => 1;

        [Inject] private readonly AchievementService _achievementService;
        [Inject] private readonly SaveGameService _saveGameService;


        /// <summary>
        /// Null while achievements are off (GameSettings.json) or outside a game.
        /// </summary>
        public JToken Capture()
        {
            return _achievementService.CaptureRun();
        }

        /// <summary>
        /// New game: SaveGameId is NewGame already. Loading: Restore() follows if the save has a section.
        /// </summary>
        public void Reset()
        {
            _achievementService.ResetRun(_saveGameService.IsNewGame);
        }

        public void Restore(JToken data, int savedVersion)
        {
            _achievementService.RestoreRun(data);
        }
    }
}
