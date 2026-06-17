using System;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Npc;
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
        /// while we're not looking. Visible NPCs pick the new routine up with their next StartNextRoutine().
        /// For culled NPCs, the culling sphere follows the schedule to the new waypoint.
        /// FIXME - Original G1 interrupts the current ZS_* state of visible NPCs when their routine changes.
        /// </summary>
        private void OnGameTimeMinuteChanged(DateTime now)
        {
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                // e.g. Hero and monsters have no daily routines.
                if (npc.Props == null || npc.Props.Routines.Count == 0)
                    continue;

                if (!CalculateCurrentRoutine(npc.Instance))
                    continue;

                _npcMeshCullingService.NotifyNpcRoutineChanged(npc);
            }
        }

        public void ExtNpcExchangeRoutine(NpcInstance npcInstance, string routineName)
        {
            var formattedRoutineName = $"Rtn_{routineName}_{npcInstance.Id}";
            var newRoutine = _vm.GetSymbolByName(formattedRoutineName);

            if (newRoutine == null)
            {
                Logger.LogError($"Routine {formattedRoutineName} couldn't be found.", LogCat.Npc);
                return;
            }

            ExchangeRoutine(npcInstance, newRoutine.Index);
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

            npc.GetUserData().Props.Routines.Clear();

            // We always need to set "self" before executing any Daedalus function.
            _gameStateService.GothicVm.GlobalSelf = npc;
            _gameStateService.GothicVm.Call(routineIndex);

            npc.GetUserData().Vob.HasRoutine = npc.GetUserData().Props.Routines.NotNullOrEmpty();

            CalculateCurrentRoutine(npc);
        }

        /// <summary>
        /// Based on time of the day, we need to calculate routine.
        /// </summary>
        public bool CalculateCurrentRoutine(NpcInstance npc)
        {
            var npcProps = npc.GetUserData().Props;
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

            return changed;
        }
    }
}
