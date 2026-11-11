using System.Collections.Generic;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// The scripts' mob routines (Wld_SetMobRoutine): per scheme (e.g. FIREPLACE) the times a state switches.
    /// Cleared when a world is left - the world's STARTUP sets them again. Not saved.
    /// </summary>
    public class MobRoutineService
    {
        private readonly Dictionary<string, List<(int hour, int minute, int status)>> _routines = new();

        public MobRoutineService()
        {
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(() => _routines.Clear());
        }

        public void Add(string scheme, int hour, int minute, int status)
        {
            if (!_routines.TryGetValue(scheme, out var entries))
            {
                entries = new();
                _routines[scheme] = entries;
            }
            entries.Add((hour, minute, status));
        }

        /// <summary>
        /// The state the scripts' mob routine of a scheme gives at the time: the last switch at or before it,
        /// wrapping over midnight. -1 without a routine.
        /// </summary>
        public int GetState(string scheme, System.DateTime time)
        {
            if (string.IsNullOrEmpty(scheme) || !_routines.TryGetValue(scheme, out var entries) || entries.Count == 0)
                return -1;

            var nowMinutes = time.Hour * 60 + time.Minute;
            int? bestBefore = null, bestBeforeState = null, latest = null, latestState = null;
            foreach (var (hour, minute, status) in entries)
            {
                var minutes = hour * 60 + minute;
                if (minutes <= nowMinutes && (bestBefore == null || minutes >= bestBefore))
                {
                    bestBefore = minutes;
                    bestBeforeState = status;
                }
                if (latest == null || minutes >= latest)
                {
                    latest = minutes;
                    latestState = status;
                }
            }
            return bestBeforeState ?? latestState ?? -1;
        }
    }
}
