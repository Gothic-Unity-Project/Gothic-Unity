using Gothic.Core.Services.Achievements;
using Gothic.Core.Services.World;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// The run's achievement data (run id, save number, tier, counters), sealed. It only captures and restores,
    /// AchievementService owns the run state and decides the run once the world is loaded. A save without it (older
    /// saves) plays as Adanos with a new run id, a broken seal as Beliar. The save side (which save, new game or not)
    /// is told from here, AchievementService doesn't know SaveGameService. Docs/achievements.md
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
            return _achievementService.CaptureRun(GetSaveIdentity());
        }

        /// <summary>
        /// New game: SaveGameId is NewGame already. Loading: the save's id, Restore() follows if it has a section.
        /// </summary>
        public void Reset()
        {
            _achievementService.ResetRun(_saveGameService.IsNewGame);
        }

        public void Restore(JToken data, int savedVersion)
        {
            _achievementService.RestoreRun(data, GetSaveIdentity());
        }

        public void OnCommitted()
        {
            _achievementService.OnSaveCommitted();
        }

        /// <summary>
        /// Gothic's SAVEINFO of the save being written or loaded (date + play time in whole seconds, as ZenKit stores
        /// it): binds a section to its save.
        /// </summary>
        private string GetSaveIdentity()
        {
            var metadata = _saveGameService.Save?.Metadata;
            if (metadata == null)
                return string.Empty;

            return $"{metadata.SaveDate.Trim()}|{(int)metadata.PlayTime.TotalSeconds}";
        }
    }
}
