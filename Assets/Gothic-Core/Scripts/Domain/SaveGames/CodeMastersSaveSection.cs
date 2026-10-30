using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
using Gothic.Core.Services.World;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// Progress of the code masters (VobContainer.CodeMasterState): which of their switches or plates were pressed, like
    /// the engine keeps it (OpenGothic CodeMaster::save). Gothic's format has none. Without it a half solved puzzle -
    /// G1's Sleeper temple hearts and switches - started over after loading while the used switches couldn't fire again.
    /// Stored per world by name, code masters without presses aren't stored. Found on BoroBongo's branch.
    /// </summary>
    public class CodeMastersSaveSection : ISaveStateSection
    {
        public string Key => "core.codeMasters";
        public int Version => 1;

        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly GameStateService _gameStateService;

        // Gothic's world name (ToGothicWorldName()) -> code master name -> state.
        private readonly Dictionary<string, Dictionary<string, CodeMasterState>> _states = new();


        public CodeMastersSaveSection()
        {
            GlobalEventDispatcher.StoreWorldStateStart.AddListener(_ => StoreCurrentWorld());
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(RestoreCurrentWorld);
        }

        public JToken Capture()
        {
            StoreCurrentWorld();

            var worlds = new JObject();
            foreach (var world in _states)
            {
                if (world.Value.Count == 0)
                    continue;

                var states = new JObject();
                foreach (var state in world.Value)
                {
                    states[state.Key] = new JObject
                    {
                        ["pressed"] = new JArray(state.Value.PressedSlots.Select(isPressed => isPressed ? 1 : 0)),
                        ["count"] = state.Value.PressCount
                    };
                }
                worlds[world.Key] = states;
            }

            return worlds;
        }

        /// <summary>
        /// A new game starts with all code masters unsolved, a loaded one brings their progress (Restore()).
        /// </summary>
        public void Reset()
        {
            _states.Clear();
        }

        public void Restore(JToken data, int savedVersion)
        {
            if (data is not JObject worlds)
                return;

            foreach (var world in worlds.Properties())
            {
                if (world.Value is not JObject states)
                    continue;

                var worldStates = new Dictionary<string, CodeMasterState>();
                foreach (var state in states.Properties())
                {
                    if (state.Value is not JObject values || values["pressed"] is not JArray pressed)
                        continue;

                    worldStates[state.Name] = new CodeMasterState(pressed.Count)
                    {
                        PressedSlots = pressed.Select(isPressed => isPressed.Value<int>() != 0).ToArray(),
                        PressCount = values["count"]?.Value<int>() ?? 0
                    };
                }
                _states[world.Name] = worldStates;
            }
        }

        private void StoreCurrentWorld()
        {
            if (string.IsNullOrEmpty(_saveGameService.CurrentWorldName))
                return;

            var states = new Dictionary<string, CodeMasterState>();
            foreach (var codeMaster in _gameStateService.VobsCodeMaster)
            {
                if (codeMaster.Value?.CodeMasterState is { PressCount: > 0 } state)
                    states[codeMaster.Key] = state;
            }

            _states[_saveGameService.CurrentWorldName.ToGothicWorldName()] = states;
        }

        /// <summary>
        /// The world's code masters are registered now. Their handlers read the state when the next press comes.
        /// </summary>
        private void RestoreCurrentWorld()
        {
            if (string.IsNullOrEmpty(_saveGameService.CurrentWorldName) ||
                !_states.TryGetValue(_saveGameService.CurrentWorldName.ToGothicWorldName(), out var states))
                return;

            foreach (var state in states)
            {
                if (_gameStateService.VobsCodeMaster.TryGetValue(state.Key, out var codeMaster))
                    codeMaster.CodeMasterState = state.Value;
            }
        }
    }
}
