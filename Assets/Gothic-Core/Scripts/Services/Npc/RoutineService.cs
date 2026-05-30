using System;
using System.Collections.Generic;
using GUZ.Core.Adapters.Npc;
using GUZ.Core.Models.Config;
using GUZ.Core.Models.Npc;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Npc;
using Reflex.Attributes;

namespace GUZ.Core.Manager
{
    /// <summary>
    /// Manages the Routines in a central spot. Routines Subscribe here. Calls the Routines when they are due.
    /// </summary>
    public class RoutineService
    {
        [Inject] private readonly ConfigService _configService;
        
        private Dictionary<int, List<RoutineHandler>> _npcStartTimeDict = new();


        public void Init()
        {
            //Init starting position
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(WorldLoadedEvent);
            GlobalEventDispatcher.GameTimeMinuteChangeCallback.AddListener(Invoke);
        }

        private void WorldLoadedEvent()
        {
            var time = new DateTime(1, 1, 1, _configService.Dev.StartTimeHour, _configService.Dev.StartTimeMinute, 0);

            Invoke(time);
        }

        public void Subscribe(RoutineHandler npcID, List<RoutineData> routines)
        {
            // We need to fill in routines backwards as e.g. Mud and Scorpio have duplicate routines. Last one needs to win.
            routines.Reverse();
            foreach (var routine in routines)
            {
                _npcStartTimeDict.TryAdd(routine.NormalizedStart, new List<RoutineHandler>());
                _npcStartTimeDict[routine.NormalizedStart].Add(npcID);
            }
        }

        public void Unsubscribe(RoutineHandler routineHandlerInstance, List<RoutineData> routines)
        {
            foreach (var routine in routines)
            {
                if (!_npcStartTimeDict.TryGetValue(routine.NormalizedStart, out var routinesForStartPoint))
                {
                    return;
                }

                routinesForStartPoint.Remove(routineHandlerInstance);

                // Remove element if empty
                if (_npcStartTimeDict[routine.NormalizedStart].Count == 0)
                {
                    _npcStartTimeDict.Remove(routine.NormalizedStart);
                }
            }
        }

        /// <summary>
        /// Calls the routineInstances that are due.
        /// Triggers Routine Change
        /// </summary>
        private void Invoke(DateTime now)
        {
            var normalizedNow = now.Hour % 24 * 60 + now.Minute;

            if (!_npcStartTimeDict.TryGetValue(normalizedNow, out var routineItems))
            {
                return;
            }

            foreach (var routineItem in routineItems)
            {
                routineItem.ChangeRoutine(now);
            }
        }
    }
}
