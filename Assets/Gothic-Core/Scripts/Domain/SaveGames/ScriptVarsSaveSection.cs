using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.World;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// Gothic's SAVEDAT.SAV stores int script variables only. Like OpenGothic, we also store float and string
    /// variables and variables pointing to an NPC (var C_NPC ...). Mods keep their state in them.
    /// NPC references are stored by the NPC's instance name. Monsters share an instance, then any of them is taken.
    /// </summary>
    public class ScriptVarsSaveSection : ISaveStateSection
    {
        public string Key => "core.scriptVars";
        public int Version => 1;

        // Set by the engine on every call, not game state.
        private static readonly HashSet<string> _engineInstances = new() { "SELF", "OTHER", "VICTIM", "ITEM", "HERO" };

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        // NPC references wait for the NPCs of the loaded world.
        private JObject _pendingNpcReferences;


        public ScriptVarsSaveSection()
        {
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(RestorePendingNpcReferences);
        }

        public JToken Capture()
        {
            var floats = new JObject();
            var strings = new JObject();
            var npcs = new JObject();

            var vm = _gameStateService.GothicVm;
            if (vm == null)
                return new JObject();

            for (var i = 0; i < vm.SymbolCount; i++)
            {
                var symbol = vm.GetSymbolByIndex(i);
                if (!IsSavedSymbol(symbol))
                    continue;

                switch (symbol.Type)
                {
                    case DaedalusDataType.Float:
                        floats[symbol.Name] = new JArray(Enumerable.Range(0, symbol.Size)
                            .Select(j => symbol.GetFloat((ushort)j)));
                        break;
                    case DaedalusDataType.String:
                        strings[symbol.Name] = new JArray(Enumerable.Range(0, symbol.Size)
                            .Select(j => symbol.GetString((ushort)j)));
                        break;
                    case DaedalusDataType.Instance:
                        if (symbol.GetInstance() is NpcInstance npc)
                            npcs[symbol.Name] = vm.GetSymbolByIndex((int)npc.Index)?.Name;
                        break;
                }
            }

            return new JObject { ["Floats"] = floats, ["Strings"] = strings, ["Npcs"] = npcs };
        }

        public void Reset()
        {
            _pendingNpcReferences = null;
        }

        /// <summary>
        /// Called after SAVEDAT.SAV is restored (SaveGameDaedalusDomain), before the world's INIT scripts run.
        /// </summary>
        public void Restore(JToken data, int savedVersion)
        {
            var vm = _gameStateService.GothicVm;
            if (vm == null || data is not JObject vars)
                return;

            foreach (var property in (vars["Floats"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var symbol = GetSavedSymbol(property.Name, DaedalusDataType.Float);
                if (symbol == null || property.Value is not JArray values)
                    continue;

                for (var j = 0; j < Math.Min(values.Count, symbol.Size); j++)
                    symbol.SetFloat(values[j].Value<float>(), (ushort)j);
            }

            foreach (var property in (vars["Strings"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var symbol = GetSavedSymbol(property.Name, DaedalusDataType.String);
                if (symbol == null || property.Value is not JArray values)
                    continue;

                for (var j = 0; j < Math.Min(values.Count, symbol.Size); j++)
                    symbol.SetString(values[j].Value<string>() ?? string.Empty, (ushort)j);
            }

            _pendingNpcReferences = vars["Npcs"] as JObject;
        }

        private void RestorePendingNpcReferences()
        {
            if (_pendingNpcReferences == null)
                return;

            var references = _pendingNpcReferences;
            _pendingNpcReferences = null;

            var vm = _gameStateService.GothicVm;
            foreach (var property in references.Properties())
            {
                var symbol = GetSavedSymbol(property.Name, DaedalusDataType.Instance);
                var npcSymbol = vm.GetSymbolByName(property.Value.Value<string>() ?? string.Empty);
                if (symbol == null || npcSymbol == null)
                    continue;

                var npc = _multiTypeCacheService.NpcCache
                    .FirstOrDefault(i => i.Instance != null && (int)i.Instance.Index == npcSymbol.Index);
                if (npc == null)
                {
                    Logger.LogWarning($"[SaveGame] {property.Name} points to {npcSymbol.Name}, which isn't inside " +
                                      "the loaded world. It stays empty.", LogCat.Loading);
                    continue;
                }

                symbol.SetInstance(npc.Instance);
            }
        }

        private DaedalusSymbol GetSavedSymbol(string name, DaedalusDataType type)
        {
            // Mods or script updates can remove/rename symbols.
            var symbol = _gameStateService.GothicVm.GetSymbolByName(name);
            return IsSavedSymbol(symbol) && symbol.Type == type ? symbol : null;
        }

        /// <summary>
        /// Global, non-const variables like SaveGameDaedalusDomain.IsSavedSymbol(), but floats, strings and instance
        /// variables. Instance definitions (e.g. PC_Hero) are const, instance variables have no size.
        /// Vanilla G1 has none of them.
        /// </summary>
        private static bool IsSavedSymbol(DaedalusSymbol symbol)
        {
            if (symbol == null || symbol.IsConst || symbol.IsMember || symbol.IsExternal || symbol.IsGenerated ||
                symbol.Name.Contains('.'))
                return false;

            return symbol.Type switch
            {
                DaedalusDataType.Float or DaedalusDataType.String => symbol.Size > 0,
                DaedalusDataType.Instance => !_engineInstances.Contains(symbol.Name),
                _ => false
            };
        }
    }
}
