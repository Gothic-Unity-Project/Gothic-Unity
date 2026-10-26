using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
#if GOTHIC_HVR_INSTALLED
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Sockets;
#endif

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// V2 (DeveloperConfig.EnableBackpackVacuum): while the backpack is held in a hand, its bottom opening
    /// (BackpackPutIntoSlot on HVR, PutIntoSlot on XRI) works like a weak magnet on nearby world items:
    /// - Items are moved towards the opening without building up momentum.
    /// - The pull target ("gravity eye") sits outside the opening, away from the backpack body: items approach from
    ///   there and never through the backpack. Collisions stay on; an item that would bump into the backpack waits
    ///   instead of pushing it.
    /// - An item inside the opening gets stored after BackpackVacuumStoreSeconds - via the regular socket, same logic
    ///   as storing by hand (HVR also shows the socket's hover feedback meanwhile; XRI's slot shows its own).
    /// Added at runtime by VRBackpack. Any VR backend (IVrSocket, VrInteractable).
    /// </summary>
    public class VRBackpackVacuum : MonoBehaviour
    {
        private static readonly string[] _putIntoSlotNames = { "BackpackPutIntoSlot", "PutIntoSlot" };
        private const float _scanInterval = 0.1f;
        // Very light pull (m/s). Velocity is set, not added, so items never gain momentum.
        private const float _pullSpeed = 0.35f;
        // Distance to the opening's center at which an item counts as "inside".
        private const float _captureRadius = 0.3f;
        // How far outside of the opening the "gravity eye" sits, so items approach from below and not through the backpack.
        private const float _approachDistance = 0.25f;

        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private VrInteractable _backpack;
        private IVrSocket _putIntoSocket;
        private readonly Collider[] _overlapResults = new Collider[32];
        private readonly Dictionary<VrInteractable, float> _dwellTimes = new();
        private readonly HashSet<VrInteractable> _seenThisScan = new();
        private float _scanTimer;
        private int _itemLayerMask;
#if GOTHIC_HVR_INSTALLED
        private HVRSocketHoverAction[] _hoverActions;
        private VrInteractable _hoverShownFor;
#endif


        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            _backpack = GetComponent<VrInteractable>();
            _putIntoSocket = GetComponentsInChildren<MonoBehaviour>(true)
                .OfType<IVrSocket>()
                .FirstOrDefault(i => _putIntoSlotNames.Contains(((MonoBehaviour)i).name) && IsActiveSocket(i));
            _itemLayerMask = 1 << Constants.VobItemLayer;

            if (_backpack == null || _putIntoSocket == null)
            {
                Logger.LogWarning("[VRBackpackVacuum] Backpack interactable or its put-into slot " +
                                  $"({string.Join("/", _putIntoSlotNames)}) not found. Vacuum disabled.", LogCat.VR);
                enabled = false;
                return;
            }

#if GOTHIC_HVR_INSTALLED
            _hoverActions = _putIntoSocket.Transform.GetComponents<HVRSocketHoverAction>();
#endif
        }

        /// <summary>
        /// Prefabs carry the sockets of all backends - only the active backend's bridge holds anything.
        /// </summary>
        private static bool IsActiveSocket(IVrSocket socket)
        {
            return socket is not IVrSocketBackend backend || backend.IsActive;
        }

        private void OnDisable()
        {
            UntrackAll();
        }

        private void FixedUpdate()
        {
            if (!_configService.Dev.EnableBackpackVacuum || !IsBackpackInHand())
            {
                UntrackAll();
                return;
            }

            var opening = _putIntoSocket.Transform.position;
            PullItems(opening);

            _scanTimer += Time.fixedDeltaTime;
            if (_scanTimer < _scanInterval)
                return;

            UpdateDwellTimes(opening, _scanTimer);
            _scanTimer = 0f;
        }

        private bool IsBackpackInHand()
        {
            var rig = _vrPlayerService.VRContextInteractionService?.Rig;
            if (rig == null)
                return false;

            foreach (var hand in rig.HandGrabbers)
            {
                if (hand.Held == _backpack)
                    return true;
            }
            return false;
        }

        private void PullItems(Vector3 opening)
        {
            // "Outside" of the opening = away from the backpack's body. Items first go to a point there and only then
            // into the opening, so their path never crosses the backpack.
            var outward = (opening - _backpack.transform.position).normalized;
            var approachPoint = opening + outward * _approachDistance;

            foreach (var item in _dwellTimes.Keys)
            {
                if (item == null || !IsPullable(item))
                    continue;

                var position = item.transform.position;
                var isOnOpeningSide = Vector3.Dot(position - opening, outward) > 0f;
                var target = isOnOpeningSide ? opening : approachPoint;

                var toTarget = target - position;
                // Slow down close to the target so it settles instead of oscillating around it.
                var speed = Mathf.Min(_pullSpeed, toTarget.magnitude / Time.fixedDeltaTime * 0.1f);
                var step = toTarget.normalized * speed;

                var rb = item.GetComponent<Rigidbody>();

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
                    item.transform.position = position + step * Time.fixedDeltaTime;
                }
            }
        }

        private void UpdateDwellTimes(Vector3 opening, float elapsed)
        {
            var radius = _configService.Dev.BackpackVacuumRadius;
            var count = Physics.OverlapSphereNonAlloc(opening, radius, _overlapResults, _itemLayerMask, QueryTriggerInteraction.Ignore);

            _seenThisScan.Clear();
            VrInteractable inside = null;
            for (var i = 0; i < count; i++)
            {
                var item = _overlapResults[i].GetComponentInParent<VrInteractable>();
                if (item == null || !_seenThisScan.Add(item) || !IsPullable(item))
                    continue;

                if (!_dwellTimes.ContainsKey(item))
                    _dwellTimes[item] = 0f;

                // Only time spent inside the opening counts towards storing. Items further out are just pulled.
                var isInside = Vector3.Distance(item.transform.position, opening) <= _captureRadius;
                var dwell = isInside ? _dwellTimes[item] + elapsed : 0f;
                _dwellTimes[item] = dwell;

                if (isInside && inside == null)
                    inside = item;

                if (dwell >= _configService.Dev.BackpackVacuumStoreSeconds)
                {
                    TryStore(item);
                    if (inside == item)
                        inside = null;
                }
            }

            // Forget items that left the pull radius (or got grabbed/stored meanwhile).
            foreach (var gone in _dwellTimes.Keys.Where(i => !_seenThisScan.Contains(i)).ToList())
                _dwellTimes.Remove(gone);

            UpdateHoverFeedback(inside);
        }

        private bool IsPullable(VrInteractable item)
        {
            // Held, socketed (holster, backpack slot, loot panel, chest), part of the backpack itself or shown in the
            // favorites ring (still in the inventory).
            if (item.IsHeld || item.transform.IsChildOf(transform) ||
                item.GetComponentInParent<VRFavoritesRing>() != null)
                return false;

            var container = item.GetComponentInParent<VobLoader>()?.Container;
            if (container == null || container.Vob.Type != VirtualObjectType.oCItem)
                return false;

            // DeveloperConfig.EnableVacuumIgnoresStaleHeld: neither held nor socketed, so not with the player any more -
            // a rune taken out of a holster and dropped stayed "held by the player" and the vacuum never took it.
            if (container.IsHeldByPlayer && _configService.Dev.EnableVacuumIgnoresStaleHeld)
            {
                Logger.Log($"[VRBackpackVacuum] {item.name} was still marked as held by the player - cleared",
                    LogCat.VR);
                container.IsHeldByPlayer = false;
            }
            return !container.IsHeldByPlayer;
        }

        private void UntrackAll()
        {
            _dwellTimes.Clear();
            UpdateHoverFeedback(null);
        }

        /// <summary>
        /// HVR: same visual feedback as hovering an item into the socket by hand (scale/material hover actions).
        /// </summary>
        private void UpdateHoverFeedback(VrInteractable inside)
        {
#if GOTHIC_HVR_INSTALLED
            if (inside == _hoverShownFor || _hoverActions == null || _putIntoSocket?.Transform == null)
                return;

            var socket = _putIntoSocket.Transform.GetComponent<HVRSocket>();
            if (socket == null)
                return;

            if (_hoverShownFor != null)
            {
                var previous = _hoverShownFor.GetComponent<HurricaneVR.Framework.Core.HVRGrabbable>();
                foreach (var action in _hoverActions)
                    action.OnHoverExit(socket, previous, true);
            }

            if (inside != null)
            {
                var current = inside.GetComponent<HurricaneVR.Framework.Core.HVRGrabbable>();
                foreach (var action in _hoverActions)
                    action.OnHoverEnter(socket, current, true);
            }

            _hoverShownFor = inside;
#endif
        }

        private void TryStore(VrInteractable item)
        {
            _dwellTimes.Remove(item);

            if (_putIntoSocket.IsOccupied || !_putIntoSocket.TryPlace(item, false))
            {
                Logger.Log($"[VRBackpackVacuum] Couldn't store '{item.name}' (socket busy or item filtered).", LogCat.VR);
                return;
            }

            Logger.Log($"[VRBackpackVacuum] Stored '{item.name}'.", LogCat.VR);
        }
    }
}
