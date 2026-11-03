using System.Collections.Generic;
using System.Globalization;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.World;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;
using ZenKit.Vobs;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// Current states of mobs (VobContainer.MobState): wheels and levers, open doors and chests, ... Gothic's
    /// format has none (oCMobInter stores stateNum only, the number of its states). Stored per world, by type and
    /// position (mobs don't move). Mobs in state 0 aren't stored.
    /// Leaving a world keeps its states for a revisit in the same session.
    /// </summary>
    public class MobStatesSaveSection : ISaveStateSection
    {
        public string Key => "core.mobStates";
        public int Version => 1;

        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        // Gothic's world name (ToGothicWorldName()) -> mob key -> state.
        private readonly Dictionary<string, Dictionary<string, int>> _states = new();


        public MobStatesSaveSection()
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
                    states[state.Key] = state.Value;
                worlds[world.Key] = states;
            }

            return worlds;
        }

        /// <summary>
        /// A new game starts with all mobs in state 0, a loaded one brings its states (Restore()).
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

                var worldStates = new Dictionary<string, int>();
                foreach (var state in states.Properties())
                    worldStates[state.Name] = state.Value.Value<int>();
                _states[world.Name] = worldStates;
            }
        }

        private void StoreCurrentWorld()
        {
            if (string.IsNullOrEmpty(_saveGameService.CurrentWorldName))
                return;

            var states = new Dictionary<string, int>();
            foreach (var vobContainer in _multiTypeCacheService.VobCache)
            {
                if (vobContainer?.MobState > 0)
                    states[GetKey(vobContainer.Vob)] = vobContainer.MobState;
            }

            _states[_saveGameService.CurrentWorldName.ToGothicWorldName()] = states;
        }

        /// <summary>
        /// The world's mobs exist now. Their GameObjects (e.g. a door's hinge) are created later and read the state.
        /// </summary>
        private void RestoreCurrentWorld()
        {
            if (string.IsNullOrEmpty(_saveGameService.CurrentWorldName) ||
                !_states.TryGetValue(_saveGameService.CurrentWorldName.ToGothicWorldName(), out var states))
                return;

            foreach (var vobContainer in _multiTypeCacheService.VobCache)
            {
                if (vobContainer != null && states.TryGetValue(GetKey(vobContainer.Vob), out var state))
                    vobContainer.MobState = state;
            }
        }

        private static string GetKey(IVirtualObject mob)
        {
            var position = mob.Position;
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1:R}|{2:R}|{3:R}", mob.Type, position.X,
                position.Y, position.Z);
        }
    }
}
