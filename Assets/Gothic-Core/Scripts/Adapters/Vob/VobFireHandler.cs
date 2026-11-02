using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// DeveloperConfig.EnableFireStates: a fireplace/torch (oCMobFire) burns like in the engine - its fire tree (flame,
    /// smoke, light, sound) follows the scripts' mob routine of its scheme (G1 STARTUP: Wld_SetMobRoutine(22,00,
    /// "FIREPLACE",1) / (05,00,"FIREPLACE",0) - lit at night, out by day). Fires indoors (light mapped: houses, mines)
    /// always burn. Touching one toggles it until the routine changes again. Far away its effects are off.
    /// </summary>
    public class VobFireHandler : MonoBehaviour
    {
        private const float _checkInterval = 1f;
        private const float _cullDistance = 60f;

        private readonly List<GameObject> _fireObjects = new();
        private string _scheme;
        private bool _isAlwaysLit;
        private bool? _manualLit;
        private int _manualRoutineState;
        private float _nextCheckTime;
        private bool? _isActive;

        public bool IsLit => _manualLit ?? IsRoutineLit(GetRoutineState());

        public void Init(string scheme, bool isAlwaysLit, IEnumerable<GameObject> fireObjects)
        {
            _scheme = scheme;
            _isAlwaysLit = isAlwaysLit;
            _fireObjects.AddRange(fireObjects);
            // Spread the checks of hundreds of fires over the frames.
            _nextCheckTime = Time.time + Random.value * _checkInterval;
            Refresh();
        }

        /// <summary>
        /// The hero lights or puts out the fire - kept until the routine switches the next time.
        /// </summary>
        public void Toggle()
        {
            var routineState = GetRoutineState();
            _manualLit = !IsLit;
            _manualRoutineState = routineState;
            Logger.Log($"[Fire] '{name}' {(_manualLit.Value ? "lit" : "put out")} by the hero", LogCat.Vob);
            Refresh();
        }

        private void Update()
        {
            if (Time.time < _nextCheckTime)
                return;
            _nextCheckTime = Time.time + _checkInterval;
            Refresh();
        }

        private void Refresh()
        {
            var routineState = GetRoutineState();
            if (_manualLit.HasValue && routineState != _manualRoutineState)
                _manualLit = null;

            var head = Camera.main != null ? Camera.main.transform : null;
            var isNear = head == null || (head.position - transform.position).sqrMagnitude <= _cullDistance * _cullDistance;
            var isActive = (_manualLit ?? IsRoutineLit(routineState)) && isNear;
            if (_isActive == isActive)
                return;

            _isActive = isActive;
            foreach (var fireObject in _fireObjects)
            {
                if (fireObject != null && fireObject.activeSelf != isActive)
                    fireObject.SetActive(isActive);
            }
        }

        private bool IsRoutineLit(int routineState) => _isAlwaysLit || routineState != 0;

        /// <summary>
        /// The scheme's mob routine state at the current game time: the last switch at or before now (wrapping over
        /// midnight). -1 without a routine.
        /// </summary>
        private int GetRoutineState()
        {
            var routines = ReflexProjectInstaller.DIContainer.Resolve<NpcService>().MobRoutines;
            if (string.IsNullOrEmpty(_scheme) || !routines.TryGetValue(_scheme, out var entries) || entries.Count == 0)
                return -1;

            var now = ReflexProjectInstaller.DIContainer.Resolve<GameTimeService>().GetCurrentDateTime();
            var nowMinutes = now.Hour * 60 + now.Minute;
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
