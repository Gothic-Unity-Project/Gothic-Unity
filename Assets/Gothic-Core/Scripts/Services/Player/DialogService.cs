using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Domain.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using Gothic.Core.Models.Dialog;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
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
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly TradeService _tradeService;

        /// <summary>
        /// Set while a mob dialog (MOBSI, e.g. G2 shrine: [onStateFunc]_S1 -> AI_ProcessInfos(hero)) is open.
        /// The dialog box is shown at this anchor next to the mob instead of at an NPC.
        /// </summary>
        public GameObject MobDialogAnchor { get; private set; }

        private const float _heroQueueActionTimeout = 30f;
        private bool _isHeroQueueRunning;

        // An NPC farther away doesn't open its dialog. The engine allows 20 m (OpenGothic AI_ProcessInfos) - in VR that
        // was far too wide, Bloodwyn talked to the hero from across the yard.
        private const float _startMaxDistance = 8f;
        // Steps (stick or room-scale) around the spot where the hero stood when the dialog opened - free up to the soft
        // radius, slowed down to a stop at the radius.
        private const float _pinSoftRadius = 0.3f;
        private const float _pinRadius = 0.75f;
        // Held back a little inside the edge, so the hero isn't held right at the stop distance.
        private const float _holdEdgeMargin = 0.2f;
        // The hero can't be held inside (falling, climbing, teleported, blocked by a wall) - the dialog ends after
        // being outside this long, or at once when this far beyond the edge.
        private const float _holdGiveUpSeconds = 1f;
        private const float _holdGiveUpOvershoot = 2f;

        private Coroutine _heroHold;

        /// <summary>
        /// TextToSpeech toggle. So that the hero isn't repeating what we said already.
        /// </summary>
        public bool SkipNextOutput;
        
        public void Init()
        {
            GlobalEventDispatcher.UnloadGameStart.AddListener(StopDialogOnUnloadGame);
        }

        /// <summary>
        /// Loading a game (or starting a new one) while talking: the dialog partner is destroyed with the world.
        /// The dialog needs to end before, otherwise its coroutines access the destroyed NPC.
        /// </summary>
        private void StopDialogOnUnloadGame()
        {
            var dialogNpc = _gameStateService.Dialogs.CurrentDialogNpc;
            if (!_gameStateService.Dialogs.IsInDialog || dialogNpc == null)
                return;

            Logger.Log("[Dialog] A game is loaded - closing the active dialog.", LogCat.Dialog);
            StopDialog(dialogNpc);
        }

        /// <summary>
        /// Whether the NPC has an info of the asked kind (important TRUE: one it starts itself, FALSE: a normal one)
        /// for the hero. Like the engine (OpenGothic npc_checkinfo): not told yet or permanent, and its condition true.
        /// G2's undead dragon asks for a non-important one after his first talk (found on BoroBongo's branch).
        /// </summary>
        public bool ExtCheckInfo(NpcInstance npc, bool important)
        {
            _npcService.SetDialogs(npc.GetUserData());

            return TryGetInfo(npc.GetUserData(), important ? 1 : 0, out _);
        }

        /// <summary>
        /// initialDialogStarting - We only stop current AI routine if this is the first time the dialog box opens/NPC
        ///     talks important things. Otherwise, the ZS_*_End will get called every time we re-open a dialog in between.
        /// </summary>
        public void StartDialog(NpcContainer npcContainer, bool initialDialogStarting)
        {
            // A dialog closed in between (e.g. the NPC drew its weapon) isn't reopened by the NPC's queued
            // StartProcessInfos after its info function.
            if (!initialDialogStarting && !_gameStateService.Dialogs.IsInDialog)
                return;

            // ...and it doesn't start at all when the hero is too far away.
            if (initialDialogStarting && !IsHeroCloseEnoughToStart(npcContainer))
            {
                Logger.Log($"[Dialog] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)}: hero farther than " +
                           $"{_startMaxDistance} m - dialog not started", LogCat.Dialog);
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

            // Mob dialogs have the hero as dialog partner - no head to follow.
            var isMobDialog = IsMobDialog(npcContainer);

            // The NPC follows the hero with its head (hero's Head is the camera). Daedalus' own AI_LookAtNpc wins.
            if (initialDialogStarting && !isMobDialog && npcContainer.PrefabProps?.AnimationHeadHandler != null)
                npcContainer.PrefabProps.AnimationHeadHandler.StartDialogLookAt(
                    _npcService.GetHeroContainer().PrefabProps.Head);

            // On every (re)open, as something else may have unlocked the player in between. Only teleport is off: the
            // hero walks (stick or room-scale) inside the pin radius, HoldHero slows him down at its edge.
            _contextInteractionService.LockPlayer(PlayerLocks.Dialog, PlayerControls.Teleport);

            if (initialDialogStarting)
            {
                StopHeroHold();
                _heroHold = _unityMonoService.StartCoroutine(HoldHero(npcContainer));
            }

            // We are already inside a sub-dialog
            if (_gameStateService.Dialogs.CurrentOptions.Any())
            {
                _contextDialogService.FillDialog(npcContainer.Instance, _gameStateService.Dialogs.CurrentOptions);
                _contextDialogService.ShowDialog(GetDialogGo(npcContainer));
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

                    var conditionResult = CallDialogCondition(npcContainer, dialog.Condition);

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
            }
        }

        /// <summary>
        /// A dialog's condition function with self = the NPC, other = the hero.
        /// </summary>
        private int CallDialogCondition(NpcContainer npcContainer, int condition)
        {
            var vm = _gameStateService.GothicVm;
            using (new VmGlobalsScope(vm, npcContainer.Instance, vm.GlobalHero, null))
                return vm.Call<int>(condition);
        }

        /// <summary>
        /// If something is important, then call it automatically.
        /// </summary>
        private bool TryGetImportant(NpcContainer npcContainer, out InfoInstance item)
        {
            return TryGetInfo(npcContainer, 1, out item);
        }

        /// <summary>
        /// The first info (lowest nr) with this "important" value that is available: not told yet (or permanent), its
        /// condition true.
        /// </summary>
        private bool TryGetInfo(NpcContainer npcContainer, int important, out InfoInstance item)
        {
            // Like the engine: lowest nr first.
            foreach (var dialog in npcContainer.Props.Dialogs.OrderBy(d => d.Nr))
            {
                // Not the kind asked for (important or not).
                if (dialog.Important != important)
                {
                    continue;
                }

                // Dialog has already been told.
                if (dialog.Permanent != 1 && GetInfoState(dialog.Index).Told)
                {
                    continue;
                }

                // No dialog condition exists or dialog condition() is false.
                if (dialog.Condition == 0)
                {
                    continue;
                }

                var conditionResult = CallDialogCondition(npcContainer, dialog.Condition);

                if (conditionResult == 0)
                {
                    continue;
                }

                // Dialog is usable.
                item = dialog;
                return true;
            }

            item = null;
            return false;
        }

        public void ExtAiOutput(NpcInstance self, NpcInstance target, string outputName)
        {
            var isHero = IsHero(self);
            // Always the NPC we're talking to!
            var speakerId = GetSpeakerId(self, isHero);

            var npcTalkingTo = isHero ? target : self;

            npcTalkingTo.GetUserData().Props.AnimationQueue.Enqueue(new Output(
                new AnimationAction(int0: speakerId, string0: outputName),
                npcTalkingTo.GetUserData()));
        }

        /// <summary>
        /// The hero comments on something outside a dialog (e.g. $KEYMISSING at a locked door): voice and subtitle,
        /// fire-and-forget. Takes the first SVM the game has a line for (G1 lacks some of G2's).
        /// </summary>
        public void HeroSaySvm(params string[] svmNames)
        {
            if (_gameStateService.GothicVm.GlobalHero is not NpcInstance hero)
                return;

            var blocks = _gameStateService.Dialogs.CutsceneLibrary?.Blocks;
            foreach (var svmName in svmNames)
            {
                var lineName = $"SVM_{hero.Voice}_{svmName.TrimStart('$')}";
                if (blocks != null &&
                    !blocks.Exists(block => block.Name.Equals(lineName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                Logger.Log($"[Dialog] Hero says {svmName} ({lineName}).", LogCat.Dialog);
                new OutputSvm(new AnimationAction(int0: 0, string0: svmName), GetNpcContainer(hero)).Start();
                return;
            }

            Logger.LogWarning($"[Dialog] Hero has no line for {string.Join(", ", svmNames)}.", LogCat.Dialog);
        }

        /// <summary>
        /// The hero by instance, not by C_NPC.id == 0 - monsters don't set an id either (0). Speaker id 0 marks the hero
        /// in Output/OutputSvm, others get a non-zero one.
        /// </summary>
        public void ExtAiOutputSvmOverlay(NpcInstance npc, NpcInstance target, string svmName)
        {
            var npcContainer = GetNpcContainer(npc);
            var queue = npcContainer.Props.AnimationQueue;
            var svmAction = new OutputSvm(
                new AnimationAction(int0: GetSpeakerId(npcContainer.Instance, IsHero(npc)), string0: svmName,
                    bool0: true),
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
            var isHero = IsHero(npc);

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
                new AnimationAction(int0: GetSpeakerId(npcContainer.Instance, false), string0: svmName),
                npcContainer));
        }

        /// <summary>
        /// The hero by his instance, not by C_NPC.id 0 - monsters have no id either: G2's undead dragon "was the hero",
        /// his lines went to the hero's queue, which never runs (found on BoroBongo's branch).
        /// </summary>
        private bool IsHero(NpcInstance npc)
        {
            return _gameStateService.GothicVm.GlobalHero is NpcInstance hero && hero.Index == npc.Index;
        }

        /// <summary>
        /// Output and OutputSvm take speaker id 0 for the hero - anybody else without an id gets -1.
        /// </summary>
        private static int GetSpeakerId(NpcInstance npc, bool isHero)
        {
            if (isHero)
                return 0;

            return npc.Id != 0 ? npc.Id : -1;
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
        /// Called right before the mob's [onStateFunc]_S1, which opens the dialog via AI_ProcessInfos(hero).
        /// Choices are C_INFO instances with npc = PC_Hero.
        /// The hero's leftover AI queue is cleared - in VR nothing processes it, it's only run during mob dialogs.
        /// </summary>
        public void PrepareMobDialog(GameObject anchor)
        {
            if (MobDialogAnchor != null)
                Object.Destroy(MobDialogAnchor);

            MobDialogAnchor = anchor;
            _isHeroQueueRunning = false;
            _npcService.GetHeroContainer()?.Props.AnimationQueue.Clear();
        }

        /// <summary>
        /// Called after the mob's [onStateFunc]_S1: if it didn't open a dialog, the anchor goes again.
        /// </summary>
        public void ReleaseUnusedMobDialogAnchor()
        {
            var heroQueue = _npcService.GetHeroContainer()?.Props.AnimationQueue;
            var isDialogPending = _gameStateService.Dialogs.IsInDialog || _isHeroQueueRunning ||
                                  (heroQueue != null && heroQueue.Count > 0);
            if (MobDialogAnchor == null || isDialogPending)
                return;

            Object.Destroy(MobDialogAnchor);
            MobDialogAnchor = null;
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

        private GameObject GetDialogGo(NpcContainer npcContainer)
        {
            return IsMobDialog(npcContainer) ? MobDialogAnchor : npcContainer.Go;
        }

        private bool IsMobDialog(NpcContainer npcContainer)
        {
            return MobDialogAnchor != null && IsHero(npcContainer);
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
            if (!IsMobDialog(npcContainer))
                return;

            RunHeroQueue();
        }

        private IEnumerator RunHeroQueue(NpcContainer hero)
        {
            _isHeroQueueRunning = true;

            // StartCoroutine runs synchronously until the first yield. Wait a frame, so AI_ProcessInfos(hero) called
            // from inside "[onStateFunc]_S1" (GlobalSelf still swapped to the hero) doesn't re-enter here.
            yield return null;

            try
            {
                var queue = hero.Props.AnimationQueue;
                while (queue.Count > 0)
                {
                    var action = queue.Dequeue();
                    var startTime = Time.time;
                    var isRunning = TryRunHeroAction(action, isStart: true);

                    while (isRunning && !action.IsFinished() && Time.time - startTime < _heroQueueActionTimeout)
                    {
                        yield return null;
                        isRunning = TryRunHeroAction(action, isStart: false);
                    }
                }
            }
            finally
            {
                // Unity does not dispose stopped coroutines (scene change) - PrepareMobDialog resets the flag again.
                _isHeroQueueRunning = false;
            }
        }

        private bool TryRunHeroAction(AbstractAnimationAction action, bool isStart)
        {
            try
            {
                if (isStart)
                    action.Start();
                else
                    action.Tick();
                return true;
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[MobDialog] Hero action {action.GetType().Name} failed: {e.Message}",
                    LogCat.Dialog);
                return false;
            }
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
            _tradeService.End();
            _gameStateService.Dialogs.CurrentInstance = null;
            _gameStateService.Dialogs.CurrentOptions.Clear();
            _gameStateService.Dialogs.IsInDialog = false;
            _gameStateService.Dialogs.WasPlayerInitiated = false;
            _gameStateService.Dialogs.CurrentDialogNpc = null;
            StopHeroHold();

            // Only ends the dialog's look-at. One started by Daedalus (e.g. sitting NPCs) stays.
            if (npc.PrefabProps?.AnimationHeadHandler != null)
                npc.PrefabProps.AnimationHeadHandler.StopDialogLookAt();

            _contextInteractionService.UnlockPlayer(PlayerLocks.Dialog);

            _contextDialogService.HideDialog();
            _contextDialogService.EndDialog();

            // EndDialog moved the dialog box away from the anchor - it can go now.
            if (IsMobDialog(npc))
            {
                Object.Destroy(MobDialogAnchor);
                MobDialogAnchor = null;
            }

            // Hide subtitles from both dialog partners. A destroyed NPC took its subtitles with it.
            _npcService.GetHeroContainer().PrefabProps.NpcSubtitles.HideSubtitles();
            if (npc.Go != null)
                npc.PrefabProps.NpcSubtitles.HideSubtitles();
        }

        /// <summary>
        /// Within 8 m (the engine: 20 m, OpenGothic AI_ProcessInfos).
        /// </summary>
        private bool IsHeroCloseEnoughToStart(NpcContainer npcContainer)
        {
            if (npcContainer.Go == null || !TryGetHeroPosition(out var heroPosition))
                return true;

            return Vector3.Distance(heroPosition, npcContainer.Go.transform.position) <= _startMaxDistance;
        }

        /// <summary>
        /// The ground point under the hero's head (the camera): a physical step moves it, turning doesn't. The same
        /// point KeepPlayerWithin holds.
        /// </summary>
        private bool TryGetHeroPosition(out Vector3 position)
        {
            position = Vector3.zero;
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
                return false;

            position = hero.Go.transform.position;
            var head = hero.PrefabProps?.Head;
            if (head != null)
            {
                position.x = head.position.x;
                position.z = head.position.z;
            }
            return true;
        }

        /// <summary>
        /// The hold is a cylinder: horizontal distance like KeepPlayerWithin (a ramp mustn't count), plus a height cap
        /// (upper floor, cliff, ladder).
        /// </summary>
        private bool IsHeroWithin(Vector3 center, float radius, out float horizontalDistance)
        {
            horizontalDistance = 0f;
            if (!TryGetHeroPosition(out var heroPosition))
                return true;

            var offset = heroPosition - center;
            var heightDifference = Mathf.Abs(offset.y);
            offset.y = 0f;
            horizontalDistance = offset.magnitude;

            return horizontalDistance <= radius && heightDifference <= Constants.DialogMaxHeightDifference;
        }

        /// <summary>
        /// Holds the hero while the dialog is open, at the spot where he stood when it opened - like the engine, the
        /// player can't walk away. If the hero can't be held (falling, climbing, teleported, blocked), the dialog ends.
        /// </summary>
        private IEnumerator HoldHero(NpcContainer npcContainer)
        {
            if (!TryGetHeroPosition(out var pinPoint))
            {
                Logger.LogWarning($"[Dialog] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)}: no hero position " +
                                  "- the hero isn't held during the dialog", LogCat.Dialog);
                _heroHold = null;
                yield break;
            }

            var dialogs = _gameStateService.Dialogs;
            var outsideSeconds = 0f;
            while (dialogs.IsInDialog && dialogs.CurrentDialogNpc == npcContainer)
            {
                if (IsHeroWithin(pinPoint, _pinRadius, out var horizontalDistance))
                {
                    outsideSeconds = 0f;

                    // Slows the hero down before the edge, so the hold doesn't kick in as a hard stop.
                    KeepHeroWithin(pinPoint);
                }
                else
                {
                    outsideSeconds += Time.deltaTime;
                    var overshoot = horizontalDistance - _pinRadius;
                    if (outsideSeconds > _holdGiveUpSeconds || overshoot > _holdGiveUpOvershoot)
                    {
                        Logger.Log($"[Dialog] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)}: hero can't be " +
                                   "held - closing the dialog", LogCat.Dialog);
                        StopDialog(npcContainer);
                        yield break;
                    }

                    KeepHeroWithin(pinPoint);
                }
                yield return null;
            }
            _heroHold = null;
        }

        private void KeepHeroWithin(Vector3 center)
        {
            _contextInteractionService.KeepPlayerWithin(center, _pinSoftRadius, _pinRadius - _holdEdgeMargin);
        }

        private void StopHeroHold()
        {
            if (_heroHold == null)
                return;

            _unityMonoService.StopCoroutine(_heroHold);
            _heroHold = null;
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
            CallInformation(npcContainer, infoInstance.Information, infoInstance.Trade != 0);
        }

        /// <param name="isTrade">The entry is a trade (C_Info.trade): trading starts after its lines instead of the
        /// choices, they come back once trading ends.</param>
        private void CallInformation(NpcContainer npcContainer, int information, bool isTrade = false)
        {
            _contextDialogService.HideDialog();

            // Like the engine (OpenGothic GameScript::exec), the chosen choice leaves the list before its function
            // runs: a teacher's "+5"/"+1" function clears the choices and adds both again - removing the chosen one
            // afterwards took the new "+5" away (found on BoroBongo's branch).
            var chosen = _gameStateService.Dialogs.CurrentOptions
                .FirstOrDefault(option => option.Function == information);
            if (chosen != null)
                _gameStateService.Dialogs.CurrentOptions.Remove(chosen);

            // We always need to set "self" before executing any Daedalus function.
            _gameStateService.GothicVm.GlobalSelf = npcContainer.Instance;
            _gameStateService.GothicVm.GlobalOther = _gameStateService.GothicVm.GlobalHero;

            // A failing dialog function (e.g. a mod's external called with an uninitialized NPC) must not skip the
            // queue handling below. Otherwise the choice box never comes back and the dialog soft-locks.
            try
            {
                _gameStateService.GothicVm.Call(information);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Dialog function threw an exception - dialog may be incomplete: {ex}",
                    LogCat.Dialog);
            }

            var animationQueue = npcContainer.Props.AnimationQueue;

            // If Daedalus tells us, that the dialog is stopped after this chat (AI_StopProcessInfos), then we're done.
            if (animationQueue.Any(i => i.GetType() == typeof(StopProcessInfos)))
            {
                return;
            }
            // Else we want to have the dialog menu back once all dialog lines are talked.
            else if (isTrade)
            {
                animationQueue.Enqueue(new StartTrade(new AnimationAction(int0: information), npcContainer));
            }
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
