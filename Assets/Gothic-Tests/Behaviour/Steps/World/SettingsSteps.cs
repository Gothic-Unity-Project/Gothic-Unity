using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Steps.World
{
    /// <summary>
    /// The player's options (Gothic.ini), put back when the scenario ends; the Gothic.ini file itself is restored by the
    /// session too.
    /// </summary>
    public class SettingsSteps
    {
        // The section the game's own options are written to (the torch light option, ...).
        private const string _gothicIniSection = "GAME";

        [Inject] private readonly ConfigService _configService;

        private readonly ScenarioScope _scope;


        public SettingsSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        /// <summary>
        /// Writes the option like the menu does (GothicIniConfig.SetInt: the file, then PlayerPrefUpdated for the
        /// services that follow it). on = 1, off = 0.
        /// </summary>
        [Step("the Gothic.ini option \"(.*)\" is (on|off|\\d+)")]
        public void TheGothicIniOptionIs(string key, string value)
        {
            var gothic = _configService.Gothic;
            var previous = gothic.GetString(key, null);
            gothic.SetInt(_gothicIniSection, key, value switch { "on" => 1, "off" => 0, _ => int.Parse(value) });

            _scope.Undo($"Gothic.ini {key} back to {previous ?? "not set"}", () =>
            {
                // An option the file didn't have can't be removed again: 0 is "off", the default of the game's
                // switches. The file itself is restored when the run ends.
                if (previous == null)
                    Logger.Log($"[BehaviourTests] Gothic.ini had no {key} - set to 0 until the run ends.",
                        LogCat.Debug);
                gothic.SetInt(_gothicIniSection, key, int.TryParse(previous, out var number) ? number : 0);
            });
        }
    }
}
