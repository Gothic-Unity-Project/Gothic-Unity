using System;
using System.Collections.Generic;
using Gothic.Core.Domain.Marvin;
using Gothic.Core.Logging;
using Gothic.Core.Models.Marvin;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Marvin
{
    /// <summary>
    /// Marvin console: the whole command line is one string. The service tells the UI which completions and which
    /// next characters are possible, and runs the line. Commands: see BuildCommands().
    /// </summary>
    public class MarvinCommandService
    {
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;

        private const int _maxRecentCommands = 5;
        private const int _maxAmount = 999;

        private readonly MarvinCheatDomain _cheatDomain = new MarvinCheatDomain().Inject();
        private readonly List<string> _recentCommands = new();

        private MarvinCommandParserDomain _parser;
        private MarvinClosedSetValue _waypointValue;
        private MarvinClosedSetValue _guildValue;
        private MarvinClosedSetValue _instanceValue;
        private HashSet<string> _itemNames = new(StringComparer.Ordinal);

        /// <summary>Normalized, most recent first. Session only.</summary>
        public IReadOnlyList<string> RecentCommands => _recentCommands;

        public MarvinCommandService()
        {
            // New world: other waypoints. New VM (load game): other symbols.
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(() => _waypointValue?.Invalidate());
            GlobalEventDispatcher.LoadGameStart.AddListener(InvalidateVmCandidates);
        }

        /// <summary>
        /// Completions of the partial (last) token of the line, paged.
        /// </summary>
        public List<string> GetOptions(string line, int offset, int count, out int total)
        {
            return GetParser().GetOptions(line, offset, count, out total);
        }

        /// <summary>
        /// Placeholder of the value expected next, e.g. "&lt;HOUR&gt;". Null if none.
        /// </summary>
        public string GetPlaceholder(string line)
        {
            return GetParser().GetPlaceholder(line);
        }

        public bool IsAllowedNextChar(string line, char c)
        {
            return GetParser().IsAllowedNextChar(line, c);
        }

        public string ApplyOption(string line, string option)
        {
            return GetParser().ApplyOption(line, option);
        }

        /// <summary>
        /// After the user typed: extends the line as far as there is nothing to choose, e.g. "C" -> "CHEAT ".
        /// </summary>
        public string AutoComplete(string line)
        {
            return GetParser().AutoComplete(line);
        }

        /// <summary>
        /// After the user deleted: goes back to the last point with a choice, e.g. "CHEAT" -> "".
        /// </summary>
        public string RevertToLastBranch(string line)
        {
            return GetParser().RevertToLastBranch(line);
        }

        public string RemoveLastToken(string line)
        {
            return GetParser().RemoveLastToken(line);
        }

        public bool CanExecute(string line)
        {
            return GetParser().CanExecute(line);
        }

        public bool Execute(string line)
        {
            if (!GetParser().TryResolve(line, out var result))
            {
                Logger.LogWarning($"[MarvinCommandService] '{line}' isn't a runnable command.", LogCat.Ui);
                return false;
            }

            var normalized = string.Join(" ", result.Tokens);
            bool success;
            try
            {
                success = result.Current.Execute(result.Values);
            }
            catch (Exception e)
            {
                Logger.LogError($"[MarvinCommandService] '{normalized}' failed: {e}", LogCat.Ui);
                return false;
            }

            if (!success)
                return false;

            AddRecentCommand(normalized);
            return true;
        }

        private void AddRecentCommand(string command)
        {
            _recentCommands.Remove(command);
            _recentCommands.Insert(0, command);
            if (_recentCommands.Count > _maxRecentCommands)
                _recentCommands.RemoveAt(_recentCommands.Count - 1);

            GlobalEventDispatcher.MarvinRecentCommandsChanged.Invoke();
        }

        /// <summary>
        /// Lazy: the Daedalus VM isn't loaded when the menu starts.
        /// </summary>
        private MarvinCommandParserDomain GetParser()
        {
            return _parser ??= new MarvinCommandParserDomain(BuildCommands(), _contextGameVersionService.Version);
        }

        private MarvinCommandNode BuildCommands()
        {
            _waypointValue = new MarvinClosedSetValue("<WAYPOINT>",
                () => new MarvinPrefixIndex(_gameStateService.WayPoints.Keys));
            _guildValue = new MarvinClosedSetValue("<GUILD>", CreateGuildIndex);
            _instanceValue = new MarvinClosedSetValue("<INSTANCE>", CreateInstanceIndex);

            var root = new MarvinCommandNode();

            var cheat = root.Add("cheat");
            cheat.Add("full").Describe("Heal hit points and mana").Runs(_ => _cheatDomain.Full());
            cheat.Add("level").Describe("Hero gains 5 levels").Runs(_ => _cheatDomain.AddLevels());
            cheat.Add("guild").Describe("Change the hero's guild")
                .Add(_guildValue).Runs(values => _cheatDomain.SetGuild(values[0]));
            if (_configService.Dev.EnableMarvinGodMode)
                cheat.Add("god").Describe("Hits and falls don't hurt the hero (toggle)")
                    .Runs(_ => _cheatDomain.ToggleGodMode());
            if (_configService.Dev.EnableMarvinBlink)
                root.Add("goto").Add("forward").Describe("4 m forward where you look, through walls")
                    .Runs(_ => _cheatDomain.BlinkForward());

            root.Add("goto").Add("waypoint").Describe("Teleport to a waypoint")
                .Add(_waypointValue).Runs(values => _cheatDomain.TeleportToWaypoint(values[0]));

            var insert = root.Add("insert").Describe("Item into the inventory or NPC in front of the hero")
                .Add(_instanceValue).Runs(values => Insert(values[0], 1));
            insert.Add(new MarvinNumberValue("<AMOUNT>", 1, _maxAmount))
                .Runs(values => Insert(values[0], int.Parse(values[1])));

            if (!string.IsNullOrEmpty(_configService.Dev.MarvinFriendsNpcSymbols))
                root.Add("team").Add("irdorath").Describe("The Irdorath crew in front of the hero")
                    .Runs(_ => _cheatDomain.SpawnGroup("Irdorath crew", _configService.Dev.MarvinFriendsNpcSymbols));
            if (!string.IsNullOrEmpty(_configService.Dev.MarvinCheatFriendsNpcSymbols))
                root.Add("team").Add("helpers").Describe("The scripts' test helpers (SH/CH) in front of the hero")
                    .Runs(_ => _cheatDomain.SpawnGroup("Test helpers",
                        _configService.Dev.MarvinCheatFriendsNpcSymbols));

            root.Add("set").Add("time").Describe("Set the game time")
                .Add(new MarvinNumberValue("<HOUR>", 0, 23))
                .Add(new MarvinNumberValue("<MINUTE>", 0, 59))
                .Runs(values => _cheatDomain.SetTime(int.Parse(values[0]), int.Parse(values[1])));

            return root;
        }

        private bool Insert(string instanceName, int amount)
        {
            // Resolve the candidates first, as _itemNames is filled while creating them.
            _instanceValue.GetCandidates();
            return _cheatDomain.Insert(instanceName, amount, _itemNames.Contains(instanceName));
        }

        private MarvinPrefixIndex CreateInstanceIndex()
        {
            var vm = _gameStateService.GothicVm;
            var names = new List<string>();
            _itemNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var symbol in vm.GetInstanceSymbols("C_Item"))
            {
                _itemNames.Add(symbol.Name.ToUpperInvariant());
                names.Add(symbol.Name);
            }

            foreach (var symbol in vm.GetInstanceSymbols("C_NPC"))
                names.Add(symbol.Name);

            return new MarvinPrefixIndex(names);
        }

        /// <summary>
        /// Guild ids are int constants of the scripts starting with GIL_. Arrays (e.g. the attitude table) and
        /// markers aren't guilds.
        /// </summary>
        private MarvinPrefixIndex CreateGuildIndex()
        {
            var vm = _gameStateService.GothicVm;
            var names = new List<string>();

            for (var i = 0; i < vm.SymbolCount; i++)
            {
                var symbol = vm.GetSymbolByIndex(i);
                if (symbol == null || !symbol.Name.StartsWith("GIL_", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (symbol.Type != DaedalusDataType.Int || !symbol.IsConst || symbol.Size > 1)
                    continue;
                if (symbol.Name.Equals("GIL_MAX", StringComparison.OrdinalIgnoreCase)
                    || symbol.Name.Contains("SEPERATOR", StringComparison.OrdinalIgnoreCase)
                    || symbol.Name.Contains("SEPARATOR", StringComparison.OrdinalIgnoreCase))
                    continue;

                names.Add(symbol.Name);
            }

            return new MarvinPrefixIndex(names);
        }

        private void InvalidateVmCandidates()
        {
            _guildValue?.Invalidate();
            _instanceValue?.Invalidate();
        }
    }
}
