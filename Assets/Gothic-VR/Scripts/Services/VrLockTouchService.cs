using System;
using System.Collections.Generic;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.VR.Adapters.Vob.LockPicking;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Finds the locked door or chest an empty hand touches, for all locks at once instead of one Update per lock.
    /// Locks register while their vob is active (inside the culling range). Nothing runs while none is registered,
    /// only two small overlap queries per frame while one is near the head.
    /// </summary>
    public class VrLockTouchService
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private const float _touchRadius = 0.1f;
        // A door or chest farther than this from the head can't be touched (its pivot may be at one edge).
        private const float _nearDistance = 4f;
        private const float _nearCheckInterval = 0.25f;

        private readonly Dictionary<Transform, VRContainerDoorPickingInteraction> _locks = new();
        private readonly Collider[] _hits = new Collider[16];
        private readonly VRContainerDoorPickingInteraction[] _touchedLocks = new VRContainerDoorPickingInteraction[2];
        private bool _isRunning;
        private bool _isLockNear;
        private float _nextNearCheckTime;

        /// <summary>
        /// lockableRoot: the root of the door/chest vob, every collider below it counts as touching the lock.
        /// </summary>
        public void Register(Transform lockableRoot, VRContainerDoorPickingInteraction lockTouch)
        {
            if (lockableRoot == null || lockTouch == null)
                return;

            _locks[lockableRoot] = lockTouch;
            if (!_isRunning)
                Run();
        }

        public void Unregister(Transform lockableRoot)
        {
            if (lockableRoot != null)
                _locks.Remove(lockableRoot);
        }

        private async void Run()
        {
            _isRunning = true;
            _nextNearCheckTime = 0f;
            try
            {
                while (_locks.Count > 0)
                {
                    Tick();
                    await Awaitable.NextFrameAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Play mode ended.
            }
            catch (Exception e)
            {
                Logger.LogError($"[VrLockTouchService] Lock touch check failed: {e}", LogCat.VR);
            }
            finally
            {
                _isRunning = false;
                _isLockNear = false;
                _touchedLocks[0] = _touchedLocks[1] = null;
            }
        }

        private void Tick()
        {
            var rig = _vrPlayerService.VRContextInteractionService?.Rig;
            if (rig == null || rig.Head == null)
                return;

            if (Time.time >= _nextNearCheckTime)
            {
                _nextNearCheckTime = Time.time + _nearCheckInterval;
                _isLockNear = IsAnyLockNear(rig.Head.position);
            }

            if (!_isLockNear)
            {
                _touchedLocks[0] = _touchedLocks[1] = null;
                return;
            }

            foreach (var hand in rig.HandGrabbers)
            {
                if (hand == null)
                    continue;

                var index = hand.HandSide == VrHandSide.Left ? 0 : 1;
                var wasTouched = _touchedLocks[index];
                var touched = hand.Held == null ? FindTouchedLock(rig, hand.HandSide) : null;
                _touchedLocks[index] = touched;

                // Only the first touch, not every frame the hand stays on it.
                if (touched != null && touched != wasTouched)
                    touched.OnEmptyHandTouched(hand.HandSide);
            }
        }

        private bool IsAnyLockNear(Vector3 headPosition)
        {
            const float sqrNearDistance = _nearDistance * _nearDistance;
            foreach (var lockableRoot in _locks.Keys)
            {
                if (lockableRoot != null && (lockableRoot.position - headPosition).sqrMagnitude <= sqrNearDistance)
                    return true;
            }
            return false;
        }

        private VRContainerDoorPickingInteraction FindTouchedLock(IVrRig rig, VrHandSide side)
        {
            var handVisual = rig.GetHandVisual(side);
            var handPosition = handVisual != null ? handVisual.position
                : _vrPlayerService.GetHandModelGo(side)?.transform.position;
            if (!handPosition.HasValue)
                return null;

            var count = Physics.OverlapSphereNonAlloc(handPosition.Value, _touchRadius, _hits,
                Constants.MobLockableMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                for (var parent = _hits[i].transform; parent != null; parent = parent.parent)
                {
                    if (_locks.TryGetValue(parent, out var lockTouch))
                        return lockTouch;
                }
            }
            return null;
        }
    }
}
