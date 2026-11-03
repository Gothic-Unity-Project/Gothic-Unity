using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// Global game state stored inside SAVEDAT.SAV: Daedalus globals, game time and guild attitudes.
    /// Info states (told dialogs) and log topics are written into SaveGame.State directly at runtime
    /// (DialogService, StoryService).
    /// </summary>
    public class SaveGameDaedalusDomain
    {
        // SAVEDAT.SAV always stores a 42x42 guild table (G1 and G2).
        private const int _guildTableSize = 42;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly GameTimeService _gameTimeService;


        public void Flush(SaveGame save)
        {
            var vm = _gameStateService.GothicVm;
            if (vm == null)
            {
                Logger.LogWarning("[SaveGame] GothicVm not available. Daedalus state isn't saved.", LogCat.Loading);
                return;
            }

            var symbolStates = new List<SaveSymbolState>();
            for (var i = 0; i < vm.SymbolCount; i++)
            {
                var symbol = vm.GetSymbolByIndex(i);
                if (!IsSavedSymbol(symbol))
                    continue;

                var values = new List<int>(symbol.Size);
                for (var j = 0; j < symbol.Size; j++)
                    values.Add(symbol.GetInt((ushort)j));

                symbolStates.Add(new SaveSymbolState { Name = symbol.Name, Values = values });
            }
            save.State.SymbolStates = symbolStates;

            var day = _gameTimeService.GetElapsedDays();
            var time = _gameTimeService.GetCurrentTime();
            save.State.Day = day;
            save.State.Hour = time.Hours;
            save.State.Minute = time.Minutes;
            save.Metadata.TimeDay = day;
            save.Metadata.TimeHour = time.Hours;
            save.Metadata.TimeMinute = time.Minutes;

            FlushGuildAttitudes(save);

            Logger.Log($"[SaveGame] Daedalus state: {symbolStates.Count} symbols, {save.State.InfoStateCount} " +
                       $"infos, {save.State.LogTopicCount} log topics, day {day} {time.Hours}:{time.Minutes:D2}.",
                LogCat.Loading);
        }

        /// <summary>
        /// Needs to be called after the VM and guild data is (re)initialized and before NPCs are created.
        /// </summary>
        public void Restore(SaveGame save)
        {
            var vm = _gameStateService.GothicVm;
            if (vm == null)
            {
                Logger.LogError("[SaveGame] GothicVm not available. Daedalus state isn't restored.", LogCat.Loading);
                return;
            }

            var restored = 0;
            var skipped = 0;
            foreach (var state in save.State.SymbolStates)
            {
                var symbol = vm.GetSymbolByName(state.Name);

                // Mods or script updates can remove/rename symbols.
                if (!IsSavedSymbol(symbol))
                {
                    skipped++;
                    continue;
                }

                for (var j = 0; j < state.Values.Count && j < symbol.Size; j++)
                    symbol.SetInt(state.Values[j], (ushort)j);
                restored++;
            }

            _gameTimeService.SetDayAndTime(save.State.Day, save.State.Hour, save.State.Minute);
            RestoreGuildAttitudes(save);

            Logger.Log($"[SaveGame] Daedalus state restored: {restored} symbols ({skipped} skipped), " +
                       $"day {save.State.Day} {save.State.Hour}:{save.State.Minute:D2}.", LogCat.Loading);
        }

        /// <summary>
        /// Like original Gothic: global, non-const integer variables. Members (C_NPC.attribute), externals and
        /// function parameters/locals (FUNC.VAR) aren't game state.
        /// </summary>
        public static bool IsSavedSymbol(DaedalusSymbol symbol)
        {
            if (symbol == null || symbol.IsConst || symbol.IsMember || symbol.IsExternal || symbol.IsGenerated)
                return false;
            if (symbol.Type != DaedalusDataType.Int || symbol.Size == 0)
                return false;

            return !symbol.Name.Contains('.');
        }

        private void FlushGuildAttitudes(SaveGame save)
        {
            var attitudes = _gameStateService.GuildAttitudes;
            var guildCount = _gameStateService.GuildCount;
            if (attitudes == null || guildCount == 0)
                return;

            var table = new byte[_guildTableSize, _guildTableSize];
            var size = System.Math.Min(guildCount, _guildTableSize);
            for (var i = 0; i < size; i++)
            for (var j = 0; j < size; j++)
                table[i, j] = (byte)attitudes[i * guildCount + j];

            save.State.GuildAttitudes = table;
        }

        private void RestoreGuildAttitudes(SaveGame save)
        {
            var attitudes = _gameStateService.GuildAttitudes;
            var guildCount = _gameStateService.GuildCount;
            if (attitudes == null || guildCount == 0)
                return;

            var table = save.State.GuildAttitudes;
            var size = System.Math.Min(guildCount, table.GetLength(0));
            for (var i = 0; i < size; i++)
            for (var j = 0; j < size; j++)
                attitudes[i * guildCount + j] = table[i, j];
        }
    }
}
