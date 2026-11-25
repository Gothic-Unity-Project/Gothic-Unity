using System;
using System.Linq;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.World;
using MyBox;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    public class NpcRoutineService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;


        private DaedalusVm _vm => _gameStateService.GothicVm;

        public void Init()
        {
            GlobalEventDispatcher.GameTimeMinuteChangeCallback.AddListener(OnGameTimeMinuteChanged);
        }

        /// <summary>
        /// Advance the time-based daily routine of all NPCs - including culled ones, so that the world progresses
        /// while we're not looking. CalculateCurrentRoutine() ends the routine state of visible NPCs and moves the
        /// culling sphere of culled ones to the new waypoint.
        /// </summary>
        private void OnGameTimeMinuteChanged(DateTime now)
        {
            RecalculateAllNpcRoutines();
        }

        /// <summary>
        /// Every NPC's routine for the time right now: at each game minute and right after a time jump
        /// (NpcService.ResetNpcsAfterTimeSkip).
        /// </summary>
        public void RecalculateAllNpcRoutines()
        {
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                // e.g. Hero and monsters have no daily routines.
                if (npc.Props == null || npc.Props.Routines.Count == 0)
                    continue;

                CalculateCurrentRoutine(npc.Instance);
            }
        }

        public void ExtNpcExchangeRoutine(NpcInstance npcInstance, string routineName)
        {
            if (npcInstance == null)
            {
                Logger.LogWarning($"Npc_ExchangeRoutine called with null npcInstance for routine '{routineName}'.",
                    LogCat.Npc);
                return;
            }

            var formattedRoutineName = $"Rtn_{routineName}_{npcInstance.Id}";
            var newRoutine = _vm.GetSymbolByName(formattedRoutineName);

            if (newRoutine == null)
            {
                Logger.LogError($"Routine {formattedRoutineName} couldn't be found.", LogCat.Npc);
                return;
            }

            var npcData = npcInstance.GetUserData();
            if (npcData != null)
                npcData.Vob.CurrentRoutine = formattedRoutineName;
            ExchangeRoutine(npcInstance, newRoutine.Index);
        }

        /// <summary>
        /// Npc_IsInRoutine(npc, Rtn_X): the NPC follows that daily routine right now (Npc_ExchangeRoutine changes it).
        /// The only call, G1 Damarok's healing offer, passes a Rtn_ function. OpenGothic compares the routine entry's
        /// state (ZS_*) instead, which is never true for it.
        /// </summary>
        public bool ExtNpcIsInRoutine(NpcInstance npc, int routineFunction)
        {
            var vob = npc?.GetUserData()?.Vob;
            if (vob == null)
            {
                Logger.LogWarning($"Npc_IsInRoutine: NPC {npc?.Id} has no Unity data (not spawned?) - answering " +
                                  "false.", LogCat.Npc);
                return false;
            }

            var routineName = _vm.GetSymbolByIndex(routineFunction)?.Name;
            return routineName != null && routineName.EqualsIgnoreCase(vob.CurrentRoutine);
        }

        public void ExchangeRoutine(NpcInstance npc, string routineName)
        {
            var routine = _gameStateService.GothicVm.GetSymbolByName(routineName);

            ExchangeRoutine(npc, routine == null ? 0: routine.Index);
        }

        public void ExchangeRoutine(NpcInstance npc, int routineIndex)
        {
            // Monsters
            // e.g. Monsters have no routine, and we just need to send StartAiState function.
            if (routineIndex == 0)
            {
                // FIXME - Call StartRoutine somehow again.
                // We always need to set "self" before executing any Daedalus function.
                // GameData.GothicVm.GlobalSelf = npcInstance;
                // go.GetComponent<AiHandler>().StartRoutine(npcInstance.StartAiState);
                return;
            }

            var npcContainer = npc.GetUserData();
            if (npcContainer == null)
            {
                Logger.LogWarning(
                    $"Npc_ExchangeRoutine: NPC {npc.Id} has no Unity data (not spawned?) - skipping routine exchange.",
                    LogCat.Npc);
                return;
            }

            npcContainer.Props.Routines.Clear();

            // The VOB keeps the active routine for save games (Npc_ExchangeRoutine changes it at runtime).
            npcContainer.Vob.CurrentRoutine = _gameStateService.GothicVm.GetSymbolByIndex(routineIndex)?.Name
                                              ?? string.Empty;

            // We always need to set "self" before executing any Daedalus function.
            _gameStateService.GothicVm.GlobalSelf = npc;
            _gameStateService.GothicVm.Call(routineIndex);

            npcContainer.Vob.HasRoutine = npcContainer.Props.Routines.NotNullOrEmpty();

            CalculateCurrentRoutine(npc);
        }

        /// <summary>
        /// Based on time of the day, we need to calculate routine.
        /// </summary>
        public bool CalculateCurrentRoutine(NpcInstance npc)
        {
            var npcProps = npc.GetUserData().Props;
            var previousRoutine = npcProps.RoutineCurrent;
            var currentTime = _gameTimeService.GetCurrentDateTime();
            var normalizedNow = currentTime.Hour % 24 * 60 + currentTime.Minute;
            RoutineData newRoutine = null;

            // There are routines where stop is lower than start. (e.g. now:8:00, routine:22:00-9:00), therefore the second check.
            foreach (var routine in npcProps.Routines)
            {
                if (routine.NormalizedStart <= normalizedNow && normalizedNow < routine.NormalizedEnd)
                {
                    newRoutine = routine;
                    break;
                }
                // Handling the case where the time range spans across midnight

                if (routine.NormalizedStart > routine.NormalizedEnd)
                {
                    if (routine.NormalizedStart <= normalizedNow || normalizedNow < routine.NormalizedEnd)
                    {
                        newRoutine = routine;
                        break;
                    }
                }
            }

            // e.g. Mud has a bug as there is no routine covering 8am. We therefore pick the last one as seen in original G1. (sit)
            if (newRoutine == null)
            {
                newRoutine = npcProps.Routines.Last();
            }

            var changed = npcProps.RoutineCurrent != newRoutine;

            if (changed)
            {
                var routineIndex = npcProps.Routines.IndexOf(newRoutine);
                var prevRoutineIndex = routineIndex == 0 ? npcProps.Routines.Count - 1 : routineIndex - 1;;
                npcProps.RoutinePrevious = npcProps.Routines[prevRoutineIndex];
            }
            npcProps.RoutineCurrent = newRoutine;

            if (changed)
            {
                _npcMeshCullingService.NotifyNpcRoutineChanged(npc.GetUserData());
                EndRoutineState(npc.GetUserData(), previousRoutine);
            }

            return changed;
        }

        /// <summary>
        /// Like the engine, a routine entry ends at its end time (or when Npc_ExchangeRoutine replaces it): the state
        /// the routine started ends with its _End - Harad leaves the anvil, a sleeper gets up - and the new entry's
        /// state follows. Before, a visible NPC only moved on when its state ended by itself, and mob states loop
        /// forever. States of perceptions or other AI_StartState calls (talk, fight, watching the hero) and a dialog
        /// aren't cut, the NPC takes the new entry when they end. Culled NPCs start it when they come back into view.
        /// </summary>
        private void EndRoutineState(NpcContainer npc, RoutineData previousRoutine)
        {
            var props = npc.Props;
            var vob = npc.Vob;
            var newRoutine = props.RoutineCurrent;

            // The same state at the same spot (Bilgot: TA_Sit_Campfire 08-23 and 23-08) goes on, no standing up.
            if (previousRoutine != null && newRoutine != null && previousRoutine.Action == newRoutine.Action &&
                string.Equals(previousRoutine.Waypoint, newRoutine.Waypoint, StringComparison.OrdinalIgnoreCase))
                return;

            if (!IsRoutineStateRunning(npc))
                return;

            switch (props.CurrentLoopState)
            {
                // The state runs: its _End is queued once the current actions are done (AiHandler LoopState.End), then
                // the new routine state starts (LoopState.AfterEnd -> StartNextRoutine).
                case LoopState.Loop:
                    props.CurrentLoopState = LoopState.End;
                    break;
                // Its start function hasn't run yet: start the new entry instead (LoopState.None -> StartNextRoutine).
                case LoopState.Start:
                    props.CurrentLoopState = LoopState.None;
                    break;
                // None, End, AfterEnd: the NPC takes the new entry with its next StartNextRoutine anyway.
                default:
                    return;
            }

            var newState = newRoutine == null ? "-" : _vm.GetSymbolByIndex(newRoutine.Action)?.Name;
            Logger.Log($"[Routine] {npc.Instance.GetName(NpcNameSlot.Slot0)}: routine changed - " +
                       $"{vob.CurrentStateName} ends, next {newState} at {newRoutine?.Waypoint}", LogCat.Ai);
        }

        /// <summary>
        /// The visible NPC is in a state of its routine (Gothic's isRtnState) that nothing else is about to replace.
        /// </summary>
        private bool IsRoutineStateRunning(NpcContainer npc)
        {
            var props = npc.Props;
            var vob = npc.Vob;
            if (props == null || vob == null || vob.Player)
                return false;

            if (props.IsDead || props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return false;

            if (!vob.CurrentStateValid || !vob.CurrentStateIsRoutine)
                return false;

            // A state change is on its way already (AI_StartState queued or about to start).
            if (vob.NextStateValid || props.HasQueued<StartState>())
                return false;

            var dialogs = _gameStateService.Dialogs;
            if (dialogs.IsInDialog && dialogs.CurrentDialogNpc == npc)
                return false;

            return _npcMeshCullingService.IsAwake(npc);
        }
    }
}
