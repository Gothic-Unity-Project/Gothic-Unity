using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// Fireplaces and torches (oCMobFire) burn like in the engine: their fire tree follows the scripts' mob routine of
    /// their scheme (G1 STARTUP: Wld_SetMobRoutine(22,00,"FIREPLACE",1) / (05,00,"FIREPLACE",0) - lit at night, out
    /// by day). Fires indoors (light mapped: houses, mines) always burn. Touching one toggles it until the routine
    /// switches again. Further than _cullDistance their effects are off.
    /// One check for all fires per _checkSeconds - no per fire Update. Not saved: after a load the routine decides.
    /// </summary>
    public class FireService
    {
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly MobRoutineService _mobRoutineService;

        private const float _checkSeconds = 1f;
        private const float _cullDistance = 60f;

        private readonly List<FireContainer> _fires = new();
        private Coroutine _checkRoutine;

        public FireService()
        {
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(OnLoadingSceneLoaded);
        }

        /// <summary>
        /// A fire built by VobInitializerDomain.CreateFire, with the objects of its fire tree.
        /// </summary>
        public void Register(GameObject go, string scheme, bool isAlwaysLit, IEnumerable<GameObject> fireObjects)
        {
            var fire = new FireContainer { Go = go, Scheme = scheme, IsAlwaysLit = isAlwaysLit };
            fire.FireObjects.AddRange(fireObjects);
            _fires.Add(fire);
            Refresh(fire, GetCameraPosition());

            _checkRoutine ??= _unityMonoService.StartCoroutine(CheckFires());
        }

        /// <summary>
        /// The hero lights or puts out the fire the object belongs to - kept until its routine switches the next time.
        /// False if it isn't a registered fire.
        /// </summary>
        public bool TryToggle(GameObject go)
        {
            var fire = Find(go);
            if (fire == null)
                return false;

            var routineState = _mobRoutineService.GetState(fire.Scheme, _gameTimeService.GetCurrentDateTime());
            fire.ManualLit = !(fire.ManualLit ?? IsRoutineLit(fire, routineState));
            fire.ManualRoutineState = routineState;
            Logger.Log($"[Fire] '{fire.Go.name}' {(fire.ManualLit.Value ? "lit" : "put out")} by the hero", LogCat.Vob);
            Refresh(fire, GetCameraPosition());
            return true;
        }

        /// <summary>
        /// The fire of a grabbed object: a root fire's loader is the mesh's parent, a child fire's loader its mesh
        /// itself. The closest in the hierarchy wins - a fireplace can hold another fireplace below it.
        /// </summary>
        private FireContainer Find(GameObject go)
        {
            if (go == null)
                return null;

            FireContainer best = null;
            var bestDistance = int.MaxValue;
            foreach (var fire in _fires)
            {
                if (fire.Go == null)
                    continue;
                var distance = GetHierarchyDistance(fire.Go.transform, go.transform);
                if (distance < 0)
                    distance = GetHierarchyDistance(go.transform, fire.Go.transform);
                if (distance >= 0 && distance < bestDistance)
                {
                    best = fire;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// Levels from the transform up to the ancestor (0 = the same), -1 if it isn't one.
        /// </summary>
        private static int GetHierarchyDistance(Transform transform, Transform ancestor)
        {
            var distance = 0;
            for (var t = transform; t != null; t = t.parent, distance++)
            {
                if (t == ancestor)
                    return distance;
            }
            return -1;
        }

        private IEnumerator CheckFires()
        {
            var wait = new WaitForSeconds(_checkSeconds);
            while (true)
            {
                yield return wait;

                var cameraPosition = GetCameraPosition();
                for (var i = _fires.Count - 1; i >= 0; i--)
                {
                    if (_fires[i].Go == null)
                        _fires.RemoveAt(i);
                    else
                        Refresh(_fires[i], cameraPosition);
                }
            }
        }

        private void Refresh(FireContainer fire, Vector3? cameraPosition)
        {
            var routineState = _mobRoutineService.GetState(fire.Scheme, _gameTimeService.GetCurrentDateTime());
            if (fire.ManualLit.HasValue && routineState != fire.ManualRoutineState)
                fire.ManualLit = null;

            var isNear = !cameraPosition.HasValue ||
                         (cameraPosition.Value - fire.Go.transform.position).sqrMagnitude <=
                         _cullDistance * _cullDistance;
            var isActive = (fire.ManualLit ?? IsRoutineLit(fire, routineState)) && isNear;
            if (fire.IsActive == isActive)
                return;

            fire.IsActive = isActive;
            foreach (var fireObject in fire.FireObjects)
            {
                if (fireObject != null && fireObject.activeSelf != isActive)
                    fireObject.SetActive(isActive);
            }
        }

        /// <summary>
        /// A scheme without a routine (-1) burns.
        /// </summary>
        private static bool IsRoutineLit(FireContainer fire, int routineState) =>
            fire.IsAlwaysLit || routineState != 0;

        private static Vector3? GetCameraPosition() =>
            Camera.main != null ? Camera.main.transform.position : null;

        private void OnLoadingSceneLoaded()
        {
            _fires.Clear();
        }
    }
}
