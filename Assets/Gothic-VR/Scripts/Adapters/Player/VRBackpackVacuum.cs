#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.VR.Adapters.HVROverrides;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Sockets;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// V2 (DeveloperConfig.EnableBackpackVacuum): while the backpack is held in a hand, its bottom opening
    /// (BackpackPutIntoSlot) works like a weak magnet on nearby world items:
    /// - Items are moved towards the opening without building up momentum.
    /// - The pull target ("gravity eye") sits outside the opening, away from the backpack body: items approach from
    ///   there and never through the backpack. Collisions stay on; an item that would bump into the backpack waits
    ///   instead of pushing it.
    /// - An item inside the opening shows the socket's regular hover feedback (like holding an item there by hand)
    ///   and gets stored after BackpackVacuumStoreSeconds - via the regular socket, same logic as storing by hand.
    /// Added at runtime by VRBackpack.
    /// </summary>
    public class VRBackpackVacuum : MonoBehaviour
    {
        private const string _putIntoSlotName = "BackpackPutIntoSlot";
        private const float _scanInterval = 0.1f;
        // Very light pull (m/s). Velocity is set, not added, so items never gain momentum.
        private const float _pullSpeed = 0.35f;
        // Distance to the opening's center at which an item counts as "inside".
        private const float _captureRadius = 0.3f;
        // How far outside of the opening the "gravity eye" sits, so items approach from below and not through the backpack.
        private const float _approachDistance = 0.25f;

        [Inject] private readonly ConfigService _configService;

        private HVRGrabbable _backpackGrabbable;
        private VRSocket _putIntoSocket;
        private HVRSocketHoverAction[] _hoverActions;
        private readonly Collider[] _overlapResults = new Collider[32];
        private readonly Dictionary<HVRGrabbable, float> _dwellTimes = new();
        private readonly HashSet<HVRGrabbable> _seenThisScan = new();
        private HVRGrabbable _hoverShownFor;
        private float _scanTimer;
        private int _itemLayerMask;


        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            _backpackGrabbable = GetComponent<HVRGrabbable>();
            _putIntoSocket = GetComponentsInChildren<VRSocket>(true).FirstOrDefault(i => i.name == _putIntoSlotName);
            _itemLayerMask = 1 << Constants.VobItemLayer;

            if (_backpackGrabbable == null || _putIntoSocket == null)
            {
                Logger.LogWarning($"[VRBackpackVacuum] Backpack grabbable or '{_putIntoSlotName}' socket not found. Vacuum disabled.", LogCat.VR);
                enabled = false;
                return;
            }

            _hoverActions = _putIntoSocket.GetComponents<HVRSocketHoverAction>();
        }

        private void OnDisable()
        {
            UntrackAll();
        }

        private void FixedUpdate()
        {
            if (!_configService.Dev.EnableBackpackVacuum || !_backpackGrabbable.IsHandGrabbed)
            {
                UntrackAll();
                return;
            }

            var opening = _putIntoSocket.transform.position;
            PullItems(opening);

            _scanTimer += Time.fixedDeltaTime;
            if (_scanTimer < _scanInterval)
                return;

            UpdateDwellTimes(opening, _scanTimer);
            _scanTimer = 0f;
        }

        private void PullItems(Vector3 opening)
        {
            // "Outside" of the opening = away from the backpack's body. Items first go to a point there and only then
            // into the opening, so their path never crosses the backpack.
            var outward = (opening - _backpackGrabbable.transform.position).normalized;
            var approachPoint = opening + outward * _approachDistance;

            foreach (var grabbable in _dwellTimes.Keys)
            {
                if (grabbable == null || !IsPullable(grabbable))
                    continue;

                var position = grabbable.transform.position;
                var isOnOpeningSide = Vector3.Dot(position - opening, outward) > 0f;
                var target = isOnOpeningSide ? opening : approachPoint;

                var toTarget = target - position;
                // Slow down close to the target so it settles instead of oscillating around it.
                var speed = Mathf.Min(_pullSpeed, toTarget.magnitude / Time.fixedDeltaTime * 0.1f);
                var step = toTarget.normalized * speed;

                var rb = grabbable.Rigidbody;

                // Magnet, not a bulldozer: if the next step would hit the backpack, the item waits instead of pushing it.
                if (rb != null && speed > 0f &&
                    rb.SweepTest(toTarget.normalized, out var hit, speed * Time.fixedDeltaTime + 0.01f, QueryTriggerInteraction.Ignore) &&
                    hit.collider.transform.IsChildOf(transform))
                {
                    step = Vector3.zero;
                }

                if (rb != null && !rb.isKinematic)
                {
                    rb.linearVelocity = step;
                    rb.angularVelocity = Vector3.MoveTowards(rb.angularVelocity, Vector3.zero, 5f * Time.fixedDeltaTime);
                    // Cancel gravity for this tick - the velocity above would otherwise be pulled down again.
                    rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
                }
                else if (rb != null)
                {
                    rb.MovePosition(position + step * Time.fixedDeltaTime);
                }
                else
                {
                    grabbable.transform.position = position + step * Time.fixedDeltaTime;
                }
            }
        }

        private void UpdateDwellTimes(Vector3 opening, float elapsed)
        {
            var radius = _configService.Dev.BackpackVacuumRadius;
            var count = Physics.OverlapSphereNonAlloc(opening, radius, _overlapResults, _itemLayerMask, QueryTriggerInteraction.Ignore);

            _seenThisScan.Clear();
            HVRGrabbable inside = null;
            for (var i = 0; i < count; i++)
            {
                var grabbable = _overlapResults[i].GetComponentInParent<HVRGrabbable>();
                if (grabbable == null || !_seenThisScan.Add(grabbable) || !IsPullable(grabbable))
                    continue;

                if (!_dwellTimes.ContainsKey(grabbable))
                    Track(grabbable);

                // Only time spent inside the opening counts towards storing. Items further out are just pulled.
                var isInside = Vector3.Distance(grabbable.transform.position, opening) <= _captureRadius;
                var dwell = isInside ? _dwellTimes[grabbable] + elapsed : 0f;
                _dwellTimes[grabbable] = dwell;

                if (isInside && inside == null)
                    inside = grabbable;

                if (dwell >= _configService.Dev.BackpackVacuumStoreSeconds)
                {
                    TryStore(grabbable);
                    if (inside == grabbable)
                        inside = null;
                }
            }

            // Forget items that left the pull radius (or got grabbed/stored meanwhile).
            foreach (var gone in _dwellTimes.Keys.Where(i => !_seenThisScan.Contains(i)).ToList())
                Untrack(gone);

            UpdateHoverFeedback(inside);
        }

        private bool IsPullable(HVRGrabbable grabbable)
        {
            // Held, socketed (holster, backpack slot, loot panel, chest) or part of the backpack itself.
            if (grabbable.IsBeingHeld || grabbable.IsSocketed || grabbable.transform.IsChildOf(transform))
                return false;

            var container = grabbable.GetComponentInParent<VobLoader>()?.Container;
            return container != null && container.Vob.Type == VirtualObjectType.oCItem && !container.IsHeldByPlayer;
        }

        private void Track(HVRGrabbable grabbable)
        {
            _dwellTimes[grabbable] = 0f;
        }

        private void Untrack(HVRGrabbable grabbable)
        {
            _dwellTimes.Remove(grabbable);
        }

        private void UntrackAll()
        {
            foreach (var grabbable in _dwellTimes.Keys.ToList())
                Untrack(grabbable);

            UpdateHoverFeedback(null);
        }

        /// <summary>
        /// Same visual feedback as hovering an item into the socket by hand (scale/material hover actions).
        /// </summary>
        private void UpdateHoverFeedback(HVRGrabbable inside)
        {
            if (inside == _hoverShownFor)
                return;

            if (_hoverShownFor != null)
                foreach (var action in _hoverActions)
                    action.OnHoverExit(_putIntoSocket, _hoverShownFor, true);

            if (inside != null)
                foreach (var action in _hoverActions)
                    action.OnHoverEnter(_putIntoSocket, inside, true);

            _hoverShownFor = inside;
        }

        private void TryStore(HVRGrabbable grabbable)
        {
            Untrack(grabbable);

            if (_putIntoSocket.IsGrabbing || !_putIntoSocket.TryGrab(grabbable))
            {
                Logger.Log($"[VRBackpackVacuum] Couldn't store '{grabbable.name}' (socket busy or item filtered).", LogCat.VR);
                return;
            }

            Logger.Log($"[VRBackpackVacuum] Stored '{grabbable.name}'.", LogCat.VR);
        }
    }
}
#endif
