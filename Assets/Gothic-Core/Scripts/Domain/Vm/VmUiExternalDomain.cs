using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.UI;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Models.Doc;
using Gothic.Core.Creator;
using MyBox;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Random = UnityEngine.Random;
using Time = UnityEngine.Time;

namespace Gothic.Core.Domain.Vm
{
    /// <summary>
    /// Daedalus externals the player sees as UI: Doc_*, Info_*, Log_*, Print*, videos and chapter intros.
    /// </summary>
    public class VmUiExternalDomain
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly StoryService _storyService;
        [Inject] private readonly DocService _docService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;

        private readonly VmExternalSupport _support;

        // (optional) Some messages from Daedalus are quite spammy. Ignore them.
        private static readonly string[] _spammyMessages = new[]
        {
            "-> bodystate",
            "Npc_GetBodyState(slf)"
        };

        // PrintScreen text -> Time.time of its last log line. Capped: a script can print ever-changing texts.
        private const float _printScreenLogInterval = 10f;

        private const int _maxPrintScreenLogTimes = 256;

        private readonly Dictionary<string, float> _printScreenLogTimes = new();

        private const string _printFont = "FONT_OLD_10_WHITE.TGA";

        public VmUiExternalDomain(VmExternalSupport support)
        {
            _support = support;
            this.Inject();
        }

        public void Register(VmExternalGuard vm)
        {
            vm.RegisterExternal<int>("Doc_Create", Doc_Create);
            vm.RegisterExternal<int>("Doc_CreateMap", Doc_CreateMap);
            vm.RegisterExternal<int, int>("Doc_SetPages", Doc_SetPages);
            vm.RegisterExternal<int, int, string, int>("Doc_SetPage", Doc_SetPage);
            vm.RegisterExternal<int, int, int, int, int, int, int>("Doc_SetMargins", Doc_SetMargins);
            vm.RegisterExternal<int, int, string>("Doc_SetFont", Doc_SetFont);
            vm.RegisterExternal<int, int, string>("Doc_PrintLine", Doc_PrintLine);
            vm.RegisterExternal<int, int, string>("Doc_PrintLines", Doc_PrintLines);
            vm.RegisterExternal<int>("Doc_Show", Doc_Show);
            // Map position externals: G1 has no Doc_SetLevelCoords (registering a missing symbol throws).
            if (vm.GetSymbolByName("Doc_SetLevel") != null)
                vm.RegisterExternal<int, string>("Doc_SetLevel", Doc_SetLevel);
            if (vm.GetSymbolByName("Doc_SetLevelCoords") != null)
                vm.RegisterExternal<int, int, int, int, int>("Doc_SetLevelCoords", Doc_SetLevelCoords);
            vm.RegisterExternal<int>("InfoManager_HasFinished", InfoManager_HasFinished);
            vm.RegisterExternal<int>("Info_ClearChoices", Info_ClearChoices);
            vm.RegisterExternal<int, string, int>("Info_AddChoice", Info_AddChoice);
            vm.RegisterExternal<string, string>("Log_AddEntry", Log_AddEntry);
            vm.RegisterExternal<string, int>("Log_CreateTopic", Log_CreateTopic);
            vm.RegisterExternal<string, int>("Log_SetTopicStatus", Log_SetTopicStatus);
            vm.RegisterExternal<string>("PrintDebug", PrintDebug);
            vm.RegisterExternal<int, string>("PrintDebugCh", PrintDebugCh);
            vm.RegisterExternal<string>("PrintDebugInst", PrintDebugInst);
            vm.RegisterExternal<int, string>("PrintDebugInstCh", PrintDebugInstCh);
            RegisterPrintScreen(vm, "PrintScreen");
            RegisterPrintScreen(vm, "AI_PrintScreen");
            vm.RegisterExternal<string>("Print", Print);
            RegisterPlayVideo(vm);
            vm.RegisterExternal<string, string, string, string, int>("IntroduceChapter", IntroduceChapter);
        }

        public int InfoManager_HasFinished()
        {
            var ret = Convert.ToInt32(_dialogService.ExtInfoManagerHasFinished());
            return _support.LogInstantExternal(nameof(InfoManager_HasFinished), ret);
        }

        public void Info_ClearChoices(int info)
        {
            _dialogService.ExtInfoClearChoices(info);
        }

        public void Info_AddChoice(int info, string text, int function)
        {
            _dialogService.ExtInfoAddChoice(info, text, function);
        }

        public void Log_AddEntry(string topic, string entry)
        {
            _storyService.ExtLogAddEntry(topic, entry);
        }

        public void Log_CreateTopic(string name, int section)
        {
            _storyService.ExtLogCreateTopic(name, (SaveTopicSection)section);
        }

        public void Log_SetTopicStatus(string name, int status)
        {
            _storyService.ExtLogSetTopicStatus(name, (SaveTopicStatus)status);
        }

        /// <summary>
        /// G1/G2: int PlayVideo(string), G2: int PlayVideoEx(string, int screenBlend, int exitSession).
        /// VR plays it in a "cinema" (VRCinema) and pauses the world meanwhile, like the engine blocks the game.
        /// </summary>
        private void RegisterPlayVideo(VmExternalGuard vm)
        {
            try
            {
                var playVideo = vm.GetSymbolByName("PlayVideo");
                if (playVideo != null)
                {
                    // Some script versions declare a return value, some don't. It has to match the DAT symbol.
                    if (playVideo.HasReturn)
                        vm.RegisterExternal<int, string>("PlayVideo", fileName => PlayVideo(fileName));
                    else
                        vm.RegisterExternal<string>("PlayVideo", fileName => PlayVideo(fileName));
                }

                if (vm.GetSymbolByName("PlayVideoEx") != null)
                    vm.RegisterExternal<int, string, int, int>("PlayVideoEx",
                        (fileName, screenBlend, exitSession) => PlayVideo(fileName));
            }
            catch (Exception e)
            {
                Logger.LogWarning($"PlayVideo has an unexpected signature in this game/mod - not registered. " +
                                  $"{e.Message}", LogCat.ZenKit);
            }
        }

        private int PlayVideo(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return 0;

            if (_configService.Dev.SkipVideos)
            {
                Logger.Log($"[PlayVideo] {fileName} skipped (DeveloperConfig.SkipVideos).", LogCat.Misc);
                return 1;
            }

            Logger.Log($"[PlayVideo] {fileName}", LogCat.Misc);
            GlobalEventDispatcher.PlayVideo.Invoke(fileName);
            return 1;
        }

        /// <summary>
        /// Real signature in G1 + G2 is (msg, posX, posY, font, seconds) - the externals.d of the MDKs is outdated.
        /// Some script versions declare a return value, some don't. It has to match the DAT symbol, otherwise the
        /// VM stack breaks - so we register based on the symbol.
        /// AI_PrintScreen is shown immediately instead of being queued in the NPC's AI.
        /// </summary>
        private void RegisterPrintScreen(VmExternalGuard vm, string name)
        {
            var symbol = vm.GetSymbolByName(name);
            if (symbol == null)
                return;

            try
            {
                if (symbol.HasReturn)
                {
                    vm.RegisterExternal<int, string, int, int, string, int>(name, (msg, posX, posY, font, seconds) =>
                    {
                        PrintScreen(msg, posX, posY, font, seconds);
                        return 1;
                    });
                }
                else
                {
                    vm.RegisterExternal<string, int, int, string, int>(name,
                        (msg, posX, posY, font, seconds) => PrintScreen(msg, posX, posY, font, seconds));
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"{name} has an unexpected signature in this game/mod - not registered. {e.Message}",
                    LogCat.ZenKit);
            }
        }

        /// <summary>
        /// posX/posY are percent of the screen, -1 centers the text.
        /// </summary>
        private void PrintScreen(string message, int posX, int posY, string font, int seconds)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            // Scripts may print every AI tick - log a text once per interval.
            if (!_printScreenLogTimes.TryGetValue(message, out var lastLog) ||
                Time.time - lastLog > _printScreenLogInterval)
            {
                if (_printScreenLogTimes.Count >= _maxPrintScreenLogTimes)
                    _printScreenLogTimes.Clear();
                _printScreenLogTimes[message] = Time.time;
                Logger.Log($"[PrintScreen] {message}", LogCat.Dialog);
            }

            // An NPC's item function (on_equip/on_unequip) talks to the player only if it's the hero's. Mods check and
            // print about `hero` in their item functions (Mroczne Tajemnice: "too little strength", "no helmet with
            // this armor") - when NPCs put on their equipment (loading a save, new game) the player got all of them.
            var itemFunctionOwner = _npcInventoryService.ItemFunctionOwner;
            if (itemFunctionOwner != null && !_support.IsHero(itemFunctionOwner))
            {
                Logger.Log($"[PrintScreen] Not shown - from {itemFunctionOwner.GetName(NpcNameSlot.Slot0)}'s item " +
                           $"function: {message}", LogCat.Dialog);
                return;
            }

            GlobalEventDispatcher.PrintScreen.Invoke(new PrintScreenMessage(message, posX, posY, font, seconds));
        }

        /// <summary>
        /// Engine messages like PLAYER_MOB_MISSING_ITEM's "missing item" text. Centered, 3 s.
        /// </summary>
        public void Print(string message)
        {
            PrintScreen(message, PrintScreenMessage.Centered, PrintScreenMessage.Centered, _printFont, 3);
        }

        public void PrintDebug(string message)
        {
            if (!_support.EnableZSpyLogs)
                return;

            Logger.Log($"[zspy]: {message}", LogCat.ZSpy);
        }

        public void PrintDebugCh(int channel, string message)
        {
            if (!_support.EnableZSpyLogs || (_support.ZSpyChannel & (1 << (channel - 1))) == 0)
                return;

            Logger.Log($"[zspy,{channel}]: {message}", LogCat.ZSpy);
        }

        public void PrintDebugInst(string message)
        {
            if (!_support.EnableZSpyLogs)
                return;

            Logger.Log($"[zspy]: {message}", LogCat.ZSpy);
        }

        public void PrintDebugInstCh(int channel, string message)
        {
            if (!_support.EnableZSpyLogs || (_support.ZSpyChannel & (1 << (channel - 1))) == 0)
                return;
            
            if (_support.IgnoreSpammyMessages && _spammyMessages.Any(spammy => message.Contains(spammy)))
                return;

            Logger.Log($"[zspy,{channel}]: {message}", LogCat.ZSpy);
        }

        public int Doc_Create() => _docService.CreateDoc();

        public int Doc_CreateMap() => _docService.CreateMap();

        public void Doc_SetPages(int id, int count) => _docService.SetPages(id, count);

        public void Doc_SetPage(int id, int page, string texture, int flags) =>
            _docService.SetPage(id, page, texture, flags);

        public void Doc_SetMargins(int id, int page, int left, int top, int right, int bottom, int type) =>
            _docService.SetMargins(id, page, left, top, right, bottom, type);

        public void Doc_SetFont(int id, int page, string font) => _docService.SetFont(id, page, font);

        public void Doc_PrintLine(int id, int page, string text) => _docService.PrintLine(id, page, text);

        public void Doc_PrintLines(int id, int page, string text) => _docService.PrintLines(id, page, text);

        public void Doc_Show(int id)
        {
            // ShowDoc raises the platform listeners (VR viewer) - their exceptions must not escape into the VM.
            try
            {
                _docService.ShowDoc(id);
            }
            catch (Exception e)
            {
                Logger.LogError($"Doc_Show({id}) failed: {e}", LogCat.Dialog);
            }
        }

        public void Doc_SetLevel(int id, string level) => _docService.SetLevel(id, level);

        public void Doc_SetLevelCoords(int id, int left, int top, int right, int bottom) =>
            _docService.SetLevelCoords(id, left, top, right, bottom);

        public void IntroduceChapter(string chapter, string text, string texture, string wav, int time)
        {
            _storyService.ExtIntroduceChapter(chapter, text, texture, wav, time);
        }
    }
}
