using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// Wld_SetMobRoutine: per mob scheme (FIREPLACE, ...) the state its mobs switch to at a time of day. Like the engine
    /// (OpenGothic WorldObjects::setMobRoutine and tick) every mob of the scheme takes the state of the current time:
    /// the last switch at or before now, else the day's last one. Fires burn while their state is above 0 (OpenGothic
    /// FirePlace::onStateChanged) - G1's fireplaces are lit from 22:00 to 05:00. Fires without a routine keep burning.
    /// </summary>
    public class MobRoutineService
    {
        [Inject] private readonly GameTimeService _gameTimeService;

        private readonly Dictionary<string, List<(int Minutes, int State)>> _routines =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Scheme, GameObject FireTree)> _fires = new();

        public MobRoutineService()
        {
            GlobalEventDispatcher.GameTimeMinuteChangeCallback.AddListener(_ => ApplyToFires());
        }

        public void AddRoutine(int hour, int minute, string scheme, int state)
        {
            if (!_routines.TryGetValue(scheme, out var routine))
                _routines[scheme] = routine = new();

            routine.Add((hour * 60 + minute, state));
            routine.Sort((a, b) => a.Minutes.CompareTo(b.Minutes));
            ApplyToFires();
        }

        /// <summary>
        /// A new world's scripts set their routines again.
        /// </summary>
        public void ClearRoutines()
        {
            _routines.Clear();
        }

        /// <summary>
        /// The fire tree (flame, smoke, light, sound) of a fireplace or torch of the scheme, shown while it burns.
        /// </summary>
        public void RegisterFire(string scheme, GameObject fireTree)
        {
            _fires.Add((scheme, fireTree));
            ApplyToFire(scheme, fireTree);
        }

        private void ApplyToFires()
        {
            _fires.RemoveAll(fire => fire.FireTree == null);
            foreach (var (scheme, fireTree) in _fires)
                ApplyToFire(scheme, fireTree);
        }

        private void ApplyToFire(string scheme, GameObject fireTree)
        {
            var state = GetState(scheme);
            var isBurning = state == null || state > 0;
            if (fireTree.activeSelf == isBurning)
                return;

            fireTree.SetActive(isBurning);
            Logger.LogEditor($"[MobRoutine] {fireTree.transform.parent?.name} {(isBurning ? "lit" : "out")}",
                LogCat.Vob);
        }

        private int? GetState(string scheme)
        {
            if (string.IsNullOrEmpty(scheme) || !_routines.TryGetValue(scheme, out var routine) || routine.Count == 0)
                return null;

            var now = _gameTimeService.GetCurrentDateTime();
            var minutes = now.Hour * 60 + now.Minute;
            for (var i = routine.Count - 1; i >= 0; i--)
            {
                if (routine[i].Minutes <= minutes)
                    return routine[i].State;
            }

            return routine[^1].State;
        }
    }
}
