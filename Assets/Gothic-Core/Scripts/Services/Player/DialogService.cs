using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Dialog;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Manager
{
    public class DialogService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextDialogService _contextDialogService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _configService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly Gothic.Core.Services.Trade.TradeService _tradeService;

        /// <summary>
        /// Set while a mob dialog (MOBSI, e.g. G2 shrine: [onStateFunc]_S1 -> AI_ProcessInfos(hero)) is open.
        /// The dialog box is shown at this anchor next to the mob instead of at an NPC.
        /// </summary>
        public GameObject MobDialogAnchor { get; private set; }

        private const float _heroQueueActionTimeout = 30f;

        /// <summary>
        /// The dialog box shows the choices (nobody talks) - only then the hero can leave the dialog bubble.
        /// </summary>
        private bool _isChoosing;
        private bool _isHeroQueueRunning;
        
        
        /// <summary>
        /// TextToSpeech toggle. So that the hero isn't repeating what we said already.
        /// </summary>
        public bool SkipNextOutput;
        
        /// <summary>
        /// Check if NPC has at least one dialog which isn't told and Hero should know about.
        /// important
        ///     - TRUE - check if there's one important dialog untold.
        ///     - FALSE - check if there's one unimportant dialog untold. (unused in G1)
        /// </summary>
        public bool ExtCheckInfo(NpcInstance npc, bool important)
        {
            if (!important)
            {
                // Don't worry. I assume this even makes no sense at all, as also the "END" dialog would always trigger a return true.
                Logger.LogError("Npc_CheckInfo isn't implemented for important=0.", LogCat.Dialog);
            }

            _npcService.SetDialogs(npc.GetUserData());
            
            return TryGetImportant(npc.GetUserData(), out _);
        }

        /// <summary>
        /// initialDialogStarting - We only stop current AI routine if this is the first time the dialog box opens/NPC
        ///     talks important things. Otherwise, the ZS_*_End will get called every time we re-open a dialog in between.
        /// </summary>
        public void StartDialog(NpcContainer npcContainer, bool initialDialogStarting)
        {
            var isFreeMovement = _configService.Dev.EnableDialogFreeMovement;

            // DeveloperConfig.EnableDialogFreeMovement: a dialog closed in between (the hero walked away) isn't
            // reopened by the NPC's queued StartProcessInfos after its info function.
            if (isFreeMovement && !initialDialogStarting && !_gameStateService.Dialogs.IsInDialog)
                return;

            // ...and it doesn't start at all when the hero is too far away to read the dialog box - come closer.
            if (isFreeMovement && initialDialogStarting && !IsHeroInDialogRange(npcContainer))
            {
                Logger.Log($"[Dialog] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)}: hero farther than " +
                           $"{_configService.Dev.DialogMaxDistance} m - dialog not started", LogCat.Dialog);
                // Not the cleanup of a dialog with someone else that is going on.
                if (!_gameStateService.Dialogs.IsInDialog || _gameStateService.Dialogs.CurrentDialogNpc == npcContainer)
                    StopDialog(npcContainer);
                return;
            }

            if (initialDialogStarting)
            {
                // Optimization: We expect, that AmbientInfos are assigned before Ai_ProcessInfos() is called.
                _npcService.SetDialogs(npcContainer);

                _contextDialogService.StartDialogInitially();
            }

            _gameStateService.Dialogs.IsInDialog = true;
            _gameStateService.Dialogs.CurrentDialogNpc = npcContainer;

            if (isFreeMovement)
            {
                if (initialDialogStarting)
                    _unityMonoService.StartCoroutine(StopDialogWhenHeroLeaves(npcContainer));
            }
            else
            {
                // WIP: locking movement
                _contextInteractionService.LockPlayerInPlace();
            }

            // We are already inside a sub-dialog
            if (_gameStateService.Dialogs.CurrentOptions.Any())
            {
                _contextDialogService.FillDialog(npcContainer.Instance, _gameStateService.Dialogs.CurrentOptions);
                _contextDialogService.ShowDialog(GetDialogGo(npcContainer));
                _isChoosing = true;
            }
            // There is at least one important entry, the NPC wants to talk to the hero about.
            else if (initialDialogStarting && TryGetImportant(npcContainer, out var infoInstance))
            {
                _gameStateService.Dialogs.CurrentInstance = infoInstance;
                CallMainInformation(npcContainer, infoInstance);
            }
            else
            {
                var selectableDialogs = new List<InfoInstance>();

                foreach (var dialog in npcContainer.Props.Dialogs)
                {
                    // Dialog is non-permanent and already been told
                    if (dialog.Permanent == 0 && GetInfoState(dialog.Index).Told)
                    {
                        continue;
                    }

                    // TODO - Should be outsourced to some VmManager.Call<int> function which sets and resets values.
                    var oldSelf = _gameStateService.GothicVm.GlobalSelf;
                    var oldOther = _gameStateService.GothicVm.GlobalOther;
                    _gameStateService.GothicVm.GlobalSelf = npcContainer.Instance;
                    _gameStateService.GothicVm.GlobalOther = _gameStateService.GothicVm.GlobalHero;
                    var conditionResult = _gameStateService.GothicVm.Call<int>(dialog.Condition);
                    _gameStateService.GothicVm.GlobalSelf = oldSelf;
                    _gameStateService.GothicVm.GlobalOther = oldOther;

                    // Dialog condition is false
                    if (conditionResult == 0)
                    {
                        continue;
                    }

                    // We can now add the dialog
                    selectableDialogs.Add(dialog);
                }

                selectableDialogs = selectableDialogs.OrderBy(d => d.Nr).ToList();

                if (!selectableDialogs.Any())
                {
                    StopDialog(npcContainer);
                    return;
                }

                _contextDialogService.FillDialog(npcContainer.Instance, selectableDialogs);
                _contextDialogService.ShowDialog(GetDialogGo(npcContainer));
                _isChoosing = true;
            }
        }

        /// <summary>
        /// If something is important, then call it automatically.
        /// </summary>
        private bool TryGetImportant(NpcContainer npcContainer, out InfoInstance item)
        {
            // DeveloperConfig.EnableImportantInfoOrder: the engine checks them by nr (lowest first).
            var dialogs = _configService.Dev.EnableImportantInfoOrder
                ? npcContainer.Props.Dialogs.OrderBy(d => d.Nr)
                : (IEnumerable<InfoInstance>)npcContainer.Props.Dialogs;
            foreach (var dialog in dialogs)
            {
                // Dialog is not important.
                if (dialog.Important != 1)
                    continue;

                // Important dialog has already been told.
                if (dialog.Permanent != 1 && GetInfoState(dialog.Index).Told)
                    continue;

                // No dialog condition exists or dialog condition() is false.
                if (dialog.Condition == 0)
                    continue;

                var oldSelf = _gameStateService.GothicVm.GlobalSelf;
                var oldOther = _gameStateService.GothicVm.GlobalOther;
                _gameStateService.GothicVm.GlobalSelf = npcContainer.Instance;
                _gameStateService.GothicVm.GlobalOther = _gameStateService.GothicVm.GlobalHero;
                var conditionResult = _gameStateService.GothicVm.Call<int>(dialog.Condition);
                _gameStateService.GothicVm.GlobalSelf = oldSelf;
                _gameStateService.GothicVm.GlobalOther = oldOther;

                if (conditionResult == 0)
                    continue;

                // Dialog is usable.
                item = dialog;
                return true;
            }

            item = null;
            return false;
        }

        public void ExtAiOutput(NpcInstance self, NpcInstance target, string outputName)
        {
            var isHero = self.Id == 0;
            // Always the NPC we're talking to!
            var speakerId = self.Id;

            var npcTalkingTo = isHero ? target : self;

            npcTalkingTo.GetUserData().Props.AnimationQueue.Enqueue(new Output(
                new AnimationAction(int0: speakerId, string0: outputName),
                npcTalkingTo.GetUserData()));
        }

        public void ExtAiOutputSvmOverlay(NpcInstance npc, NpcInstance target, string svmName)
        {
            var npcContainer = GetNpcContainer(npc);
            var queue = npcContainer.Props.AnimationQueue;
            var svmAction = new OutputSvm(
                new AnimationAction(int0: npcContainer.Instance.Id, string0: svmName, bool0: true),
                npcContainer);

            // Daedalus queues GoToNpc before AI_OutputSVM_Overlay in the same script execution.
            // Reorder so the SVM fires first (fire-and-forget), then the NPC runs.
            if (queue.Count > 0 && queue.Last() is GoToNpc)
            {
                var items = queue.ToArray();
                queue.Clear();
                for (var i = 0; i < items.Length - 1; i++)
                    queue.Enqueue(items[i]);
                queue.Enqueue(svmAction);
                queue.Enqueue(items[items.Length - 1]);
            }
            else
            {
                queue.Enqueue(svmAction);
            }
        }

        public void ExtAiOutputSvm(NpcInstance npc, NpcInstance target, string svmName)
        {
            var isHero = npc.Id == 0;

            // Hero SVM: enqueue on target's queue (hero's queue is never processed); hero container provides voice ID.
            if (isHero && target != null)
            {
                // NPC walked to player (e.g. important dialog) — hero's "hey" reactive lines make no sense.
                if (!_gameStateService.Dialogs.WasPlayerInitiated)
                    return;

                var heroContainer = GetNpcContainer(npc);
                target.GetUserData().Props.AnimationQueue.Enqueue(new OutputSvm(
                    new AnimationAction(int0: 0, string0: svmName),
                    heroContainer));
                return;
            }

            var npcContainer = GetNpcContainer(npc);
            npcContainer.Props.AnimationQueue.Enqueue(new OutputSvm(
                new AnimationAction(int0: npcContainer.Instance.Id, string0: svmName),
                npcContainer));
        }

        public bool ExtInfoManagerHasFinished()
        {
            return !_gameStateService.Dialogs.IsInDialog;
        }

        /// <summary>
        /// We update the Unity cached/created elements only.
        /// </summary>
        public void ExtInfoClearChoices(int info)
        {
            _gameStateService.Dialogs.CurrentOptions.Clear();
        }

        public void ExtInfoAddChoice(int info, string text, int function)
        {
            // Check if we need to change current instance as it wasn't cleared before.
            var oldInstance = _gameStateService.Dialogs.CurrentInstance;

            // First entry of current dialog to add
            if (oldInstance == null)
            {
                _gameStateService.Dialogs.CurrentInstance = _gameStateService.Dialogs.Instances.First(i => i.Index == info);
                _gameStateService.Dialogs.CurrentOptions.Clear();
            }
            else if (oldInstance.Index != info)
            {
                throw new Exception("Previous Dialog wasn't cleared. Gothic bug? " +
                                    $"Desc={oldInstance.Description}, Npc={oldInstance.Npc}, Info= {oldInstance.Information}");
            }

            // Add new entry
            _gameStateService.Dialogs.CurrentOptions.Add(new DialogOption
            {
                Text = text,
                Function = function
            });
        }

        public void ExtAiProcessInfos(NpcInstance npc)
        {
            var props = GetProperties(npc);

            props.AnimationQueue.Enqueue(new StartProcessInfos(new AnimationAction(bool0: true), npc.GetUserData()));
            RunHeroQueueIfMobDialog(npc.GetUserData());
        }

        public void ExtAiStopProcessInfos(NpcInstance npc)
        {
            var props = GetProperties(npc);

            props.AnimationQueue.Enqueue(new StopProcessInfos(new AnimationAction(), npc.GetUserData()));
            RunHeroQueueIfMobDialog(npc.GetUserData());
        }

        /// <summary>
        /// DeveloperConfig.EnableMobsiDialogs: called right before the mob's [onStateFunc]_S1, which opens the dialog
        /// via AI_ProcessInfos(hero). Choices are C_INFO instances with npc = PC_Hero.
        /// The hero's leftover AI queue is cleared - in VR nothing processes it, it's only run during mob dialogs.
        /// </summary>
        public void PrepareMobDialog(GameObject anchor)
        {
            if (MobDialogAnchor != null)
                Object.Destroy(MobDialogAnchor);

            MobDialogAnchor = anchor;
            _npcService.GetHeroContainer()?.Props.AnimationQueue.Clear();
        }

        private bool IsHeroInDialogRange(NpcContainer npcContainer)
        {
            var hero = _npcService.GetHeroContainer();
            var dialogGo = GetDialogGo(npcContainer);
            if (hero?.Go == null || dialogGo == null)
                return true;

            var distance = Vector3.Distance(hero.Go.transform.position, dialogGo.transform.position);
            return distance <= _configService.Dev.DialogMaxDistance;
        }

        /// <summary>
        /// DeveloperConfig.EnableDialogFreeMovement: the hero walks freely inside the dialog bubble. While somebody
        /// talks, the bubble's edge holds the hero back (skip the line first) - the NPC finished its whole answer
        /// after the hero had run away. With the choices shown, leaving the bubble ends the dialog.
        /// </summary>
        private IEnumerator StopDialogWhenHeroLeaves(NpcContainer npcContainer)
        {
            while (_gameStateService.Dialogs.IsInDialog && _gameStateService.Dialogs.CurrentDialogNpc == npcContainer)
            {
                if (!IsHeroInDialogRange(npcContainer))
                {
                    if (_isChoosing)
                    {
                        Logger.Log($"[Dialog] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)}: hero walked away - " +
                                   "dialog ended", LogCat.Dialog);
                        StopDialog(npcContainer);
                        yield break;
                    }

                    // A little inside the edge, so the hero isn't held right at the stop distance.
                    _contextInteractionService.KeepPlayerWithin(GetDialogGo(npcContainer).transform.position,
                        _configService.Dev.DialogMaxDistance - 0.2f);
                }
                yield return null;
            }
        }

        private GameObject GetDialogGo(NpcContainer npcContainer)
        {
            return MobDialogAnchor != null && IsHero(npcContainer) ? MobDialogAnchor : npcContainer.Go;
        }

        private bool IsHero(NpcContainer npcContainer)
        {
            return npcContainer != null && npcContainer == _npcService.GetHeroContainer();
        }

        /// <summary>
        /// The VR hero has no AiHandler. During a mob dialog, its AI queue (StartProcessInfos after a choice,
        /// AI_Output lines, AI_StopProcessInfos) is executed here like AiHandler does for NPCs.
        /// </summary>
        private void RunHeroQueueIfMobDialog(NpcContainer npcContainer)
        {
            if (MobDialogAnchor == null || !IsHero(npcContainer))
                return;

            RunHeroQueue();
        }

        /// <summary>
        /// Executes what scripts queued on the hero (e.g. PLAYER_MOB_MISSING_ITEM: B_Say_Overlay "$MISSINGITEM").
        /// </summary>
        public void RunHeroQueue()
        {
            var hero = _npcService.GetHeroContainer();
            if (hero == null || _isHeroQueueRunning)
                return;

            _unityMonoService.StartCoroutine(RunHeroQueue(hero));
        }

        private IEnumerator RunHeroQueue(NpcContainer hero)
        {
            _isHeroQueueRunning = true;
            var queue = hero.Props.AnimationQueue;

            while (queue.Count > 0)
            {
                var action = queue.Dequeue();
                var startTime = Time.time;
                var isRunning = true;
                try
                {
                    action.Start();
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"[MobDialog] Hero action {action.GetType().Name} failed to start: {e.Message}", LogCat.Dialog);
                    isRunning = false;
                }

                while (isRunning && !action.IsFinished() && Time.time - startTime < _heroQueueActionTimeout)
                {
                    yield return null;
                    try
                    {
                        action.Tick();
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning($"[MobDialog] Hero action {action.GetType().Name} failed: {e.Message}", LogCat.Dialog);
                        isRunning = false;
                    }
                }
            }

            _isHeroQueueRunning = false;
        }

        public void MainSelectionClicked(NpcContainer npcContainer, InfoInstance infoInstance)
        {
            CallMainInformation(npcContainer, infoInstance);
        }

        public void SubSelectionClicked(NpcContainer npcContainer, int dialogId)
        {
            CallInformation(npcContainer, dialogId);
        }

        /// <summary>
        /// Skip/Stop current Dialog's .wav entry now.
        /// </summary>
        public void SkipCurrentDialogLine(NpcProperties props)
        {

            if (props.CurrentAction.GetType() == typeof(Output))
            {
                props.CurrentAction.StopImmediately();
            }
        }

        public void StopDialog(NpcContainer npc)
        {
            _gameStateService.Dialogs.CurrentInstance = null;
            _gameStateService.Dialogs.CurrentOptions.Clear();
            _gameStateService.Dialogs.IsInDialog = false;
            _gameStateService.Dialogs.WasPlayerInitiated = false;
            _gameStateService.Dialogs.CurrentDialogNpc = null;

            // The trade ends with the dialog - everything laid on the counter goes back.
            _tradeService.Cancel();

            // WIP: unlocking movement
            _contextInteractionService.UnlockPlayer();

            _contextDialogService.HideDialog();
            _isChoosing = false;
            _contextDialogService.EndDialog();

            // EndDialog moved the dialog box away from the anchor - it can go now.
            if (MobDialogAnchor != null && IsHero(npc))
            {
                Object.Destroy(MobDialogAnchor);
                MobDialogAnchor = null;
            }

            // Hide subtitles from both dialog partners.
            _npcService.GetHeroContainer().PrefabProps.NpcSubtitles.HideSubtitles();
            npc.PrefabProps.NpcSubtitles.HideSubtitles();
        }

        /// <summary>
        /// A C_Info is clicked (main dialog entry)
        /// </summary>
        private void CallMainInformation(NpcContainer npcContainer, InfoInstance infoInstance)
        {
            // Set a new CurrentInstance for potential sub-dialog choices to fetch later.
            _gameStateService.Dialogs.CurrentInstance = npcContainer.Props.Dialogs
                .First(d => d.Information == infoInstance.Information);

            // Add entry to list of "told" information if it is main element only. (Sub-dialogs will never be reached again as main one (entry point) is already told)
            AddNpcInfoTold(infoInstance.Index);


            // Delegate remaining tasks to general implementation of CallInformation
            CallInformation(npcContainer, infoInstance.Information);

            // DeveloperConfig.EnableVrTrade: a choice with trade != 0 opens the trade after its info function (G2 fills
            // the trader's goods there, e.g. B_GiveTradeInv).
            if (infoInstance.Trade != 0)
                _tradeService.TryStart(npcContainer);
        }

        private void CallInformation(NpcContainer npcContainer, int information)
        {
            _contextDialogService.HideDialog();
            _isChoosing = false;

            // We always need to set "self" before executing any Daedalus function.
            _gameStateService.GothicVm.GlobalSelf = npcContainer.Instance;
            _gameStateService.GothicVm.GlobalOther = _gameStateService.GothicVm.GlobalHero;

            try
            {
                _gameStateService.GothicVm.Call(information);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Dialog function threw an exception — dialog may be incomplete: {ex.Message}", LogCat.Dialog);
            }

            var animationQueue = npcContainer.Props.AnimationQueue;

            // If Daedalus tells us, that the dialog is stopped after this chat (AI_StopProcessInfos), then we're done.
            if (animationQueue.Any(i => i.GetType() == typeof(StopProcessInfos)))
            {
                return;
            }
            // Else we want to have the dialog menu back once all dialog lines are talked.
            else
            {
                animationQueue.Enqueue(new StartProcessInfos(
                    new AnimationAction(int0: information), npcContainer));
            }

            RunHeroQueueIfMobDialog(npcContainer);
        }

        public bool ExtNpcKnowsInfo(NpcInstance npc, int informationIndex)
        {
            var infoInstanceName = _gameStateService.GothicVm.GetSymbolByIndex(informationIndex)!.Name;
            var state = _saveGameService.Save.State;
            SaveInfoState foundInfo = default;
            
            for (var i = 0; i < state.InfoStateCount; i++)
            {
                if (state.GetInfoState(i).Name == infoInstanceName)
                {
                    foundInfo = state.GetInfoState(i);
                    break;
                }
            }
            
            // New entry - Add it now
            if (foundInfo.Name == null)
            {
                foundInfo = new()
                {
                    Name = infoInstanceName,
                    Told = false
                };
                _saveGameService.Save.State.AddInfoState(foundInfo);
            }

            return foundInfo.Told;
        }

        private SaveInfoState GetInfoState(int informationIndex)
        {
            var infoInstanceName = _gameStateService.GothicVm.GetSymbolByIndex(informationIndex)!.Name;
            var state = _saveGameService.Save.State;

            for (var i = 0; i < state.InfoStateCount; i++)
            {
                if (state.GetInfoState(i).Name == infoInstanceName)
                {
                    return state.GetInfoState(i);
                }
            }

            // We safely assume, that if an entry doesn't exist in list, it's simply not yet told.
            return default;
        }

        private void AddNpcInfoTold(int informationIndex)
        {
            var infoInstanceName = _gameStateService.GothicVm.GetSymbolByIndex(informationIndex)!.Name;
            var allStates = _saveGameService.Save.State;

            var infoState = new SaveInfoState
            {
                Name = infoInstanceName,
                Told = true
            };
            
            for (var i = 0; i < allStates.InfoStateCount; i++)
            {
                if (allStates.GetInfoState(i).Name == infoInstanceName)
                {
                    allStates.SetInfoState(i, infoState);
                    return;
                }
            }
            
            // If not yet existing, then create it.
            allStates.AddInfoState(infoState);
        }

        private GameObject GetGo(NpcInstance npc)
        {
            return npc.GetUserData().Go;
        }

        private NpcContainer GetNpcContainer(NpcInstance npc)
        {
            return npc.GetUserData();
        }

        private NpcProperties GetProperties(NpcInstance npc)
        {
            return npc.GetUserData().Props;
        }
    }
}
