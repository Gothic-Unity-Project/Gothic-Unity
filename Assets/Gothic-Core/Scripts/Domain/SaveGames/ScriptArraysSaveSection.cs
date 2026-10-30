using System;
using System.Linq;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.World;
using Newtonsoft.Json.Linq;
using Reflex.Attributes;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// G1 save games store one value per script variable (SAVEDAT.SAV): only the first element of an int array
    /// survives. Mods (e.g. Ikarus/LeGo based ones) keep their data in such arrays. We store the full arrays.
    /// G2 save games store arrays completely, nothing to do then.
    /// </summary>
    public class ScriptArraysSaveSection : ISaveStateSection
    {
        public string Key => "core.scriptArrays";
        public int Version => 1;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;


        public JToken Capture()
        {
            var arrays = new JObject();
            var vm = _gameStateService.GothicVm;
            if (vm == null || !_contextGameVersionService.IsGothic1())
                return arrays;

            for (var i = 0; i < vm.SymbolCount; i++)
            {
                var symbol = vm.GetSymbolByIndex(i);
                if (!SaveGameDaedalusDomain.IsSavedSymbol(symbol) || symbol.Size <= 1)
                    continue;

                var values = Enumerable.Range(0, symbol.Size).Select(j => symbol.GetInt((ushort)j));
                arrays[symbol.Name] = new JArray(values);
            }

            return arrays;
        }

        public void Reset()
        {
            // Nothing to forget: the arrays live in the VM, which is new for every game.
        }

        /// <summary>
        /// Called after SAVEDAT.SAV is restored (SaveGameDaedalusDomain), i.e. our values win.
        /// </summary>
        public void Restore(JToken data, int savedVersion)
        {
            var vm = _gameStateService.GothicVm;
            if (vm == null || data is not JObject arrays)
                return;

            foreach (var property in arrays.Properties())
            {
                // Mods or script updates can remove/rename symbols.
                var symbol = vm.GetSymbolByName(property.Name);
                if (!SaveGameDaedalusDomain.IsSavedSymbol(symbol) || property.Value is not JArray values)
                    continue;

                for (var j = 0; j < Math.Min(values.Count, symbol.Size); j++)
                    symbol.SetInt(values[j].Value<int>(), (ushort)j);
            }
        }
    }
}
