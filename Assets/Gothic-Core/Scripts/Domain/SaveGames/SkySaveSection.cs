using Gothic.Core.Services.World;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// Today's rain window (Gothic stores it in the world's sky controller). Without it, the rain of a loaded game
    /// falls at Gothic's default time instead.
    /// </summary>
    public class SkySaveSection : ISaveStateSection
    {
        public string Key => "core.sky";
        public int Version => 1;

        [Inject] private readonly SkyService _skyService;


        public JToken Capture()
        {
            var (start, end) = _skyService.GetRainWindow();
            return new JObject { ["RainStart"] = start, ["RainEnd"] = end };
        }

        /// <summary>
        /// A new game starts with Gothic's default, a loaded one gets its own (Restore()).
        /// </summary>
        public void Reset()
        {
            _skyService.ResetRainWindow();
        }

        /// <summary>
        /// Before the world (and its sky) is created, which applies it.
        /// </summary>
        public void Restore(JToken data, int savedVersion)
        {
            if (data?["RainStart"] == null || data["RainEnd"] == null)
                return;

            _skyService.SetRainWindow(data.Value<float>("RainStart"), data.Value<float>("RainEnd"));
        }
    }
}
