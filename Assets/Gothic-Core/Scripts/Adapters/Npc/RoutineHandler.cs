using System;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Models.Npc;
using GUZ.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.Npc
{
    public class RoutineHandler : MonoBehaviour
    {
        [Inject] private readonly RoutineService _routineService;
        [Inject] private readonly GameTimeService _gameTimeService;
        
        public readonly List<RoutineData> Routines = new();

        public RoutineData CurrentRoutine;

        private void Start()
        {
            _routineService.Subscribe(this, Routines);
        }

        private void OnDisable()
        {
            _routineService.Unsubscribe(this, Routines);
        }

        public void ChangeRoutine(DateTime now)
        {
            if (!CalculateCurrentRoutine())
            {
                Logger.LogWarning("ChangeRoutine got called but the resulting routine was the same: " +
                                 $"NPC: >{gameObject.name}< WP: >{CurrentRoutine.Waypoint}<", LogCat.Ai);
                return;
            }

            // FIXME - We need to set! routine, not Start() it immediately. Please check with G1 if we should
            //         changeRoutine and execute ZS_*_END normally or change immediately.
            // GetComponent<AiHandler>().StartRoutine(CurrentRoutine.Action, CurrentRoutine.Waypoint);
        }

        /// <summary>
        /// Calculate new routine based on given timestamp.
        /// Hints about normalization:
        ///   1. Daedalus handles routines with a 00:00 as midnight (24:00)
        ///   -> For the midnight topic, we normalize via %24
        ///   2. Routines can span multiple days (e.g. 22:00 - 09:00)
        ///   -> For the overnight topic, we leverage the second if when start > end
        /// </summary>
        /// <returns>Whether the routine changed or not.</returns>
        public bool CalculateCurrentRoutine()
        {
            var currentTime = _gameTimeService.GetCurrentDateTime();

            var normalizedNow = currentTime.Hour % 24 * 60 + currentTime.Minute;

            RoutineData newRoutine = null;

            // There are routines where stop is lower than start. (e.g. now:8:00, routine:22:00-9:00), therefore the second check.
            foreach (var routine in Routines)
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
                newRoutine = Routines.Last();
            }

            var changed = CurrentRoutine != newRoutine;
            CurrentRoutine = newRoutine;

            return changed;
        }

        public RoutineData GetPreviousRoutine()
        {
            var currentRoutineIndex = Routines.IndexOf(CurrentRoutine);
            return currentRoutineIndex == 0 ? Routines.Last() : Routines[currentRoutineIndex - 1];
        }
    }
}
