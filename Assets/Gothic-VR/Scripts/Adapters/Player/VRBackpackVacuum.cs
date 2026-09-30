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
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableBackpackVacuum): while the backpack is held in a hand, its opening
    /// (BackpackPutIntoSlot) gently pulls nearby world items. An item staying inside the opening for
    /// BackpackVacuumStoreSeconds is put into the backpack via the regular socket -> same logic as storing by hand.
    /// Added at runtime by VRBackpack.
    /// </summary>
    public class VRBackpackVacuum : MonoBehaviour
    {
        private const string _putIntoSlotName = "BackpackPutIntoSlot";
        private const float _scanInterval = 0.1f;
        // Very light pull: world items are kinematic, so we move them ourselves at this speed (m/s).
        private const float _pullSpeed = 0.35f;
        // Distance to the opening's center at which an item counts as "inside".
        private const float _captureRadius = 0.3f;

        [Inject] private readonly ConfigService _configService;

        private HVRGrabbable _backpackGrabbable;
        private VRSocket _putIntoSocket;
        private readonly Collider[] _overlapResults = new Collider[32];
        private readonly Dictionary<HVRGrabbable, float> _dwellTimes = new();
        private readonly HashSet<HVRGrabbable> _seenThisScan = new();
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
            }
        }

        private void FixedUpdate()
        {
            if (!_configService.Dev.EnableBackpackVacuum || !_backpackGrabbable.IsHandGrabbed)
            {
                _dwellTimes.Clear();
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

        /// <summary>
        /// Items are only pulled, never thrown: we move them a tiny bit towards the opening every physics tick.
        /// </summary>
        private void PullItems(Vector3 opening)
        {
            foreach (var grabbable in _dwellTimes.Keys)
            {
                if (grabbable == null || !IsPullable(grabbable))
                    continue;

                var rb = grabbable.Rigidbody;
                var next = Vector3.MoveTowards(grabbable.transform.position, opening, _pullSpeed * Time.fixedDeltaTime);
                if (rb != null && !rb.isKinematic)
                {
                    // Cancel gravity a bit and nudge it - items with physics would otherwise just fall down again.
                    rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, (opening - rb.position).normalized * _pullSpeed, 2f * Time.fixedDeltaTime);
                    rb.AddForce(-Physics.gravity * 0.9f, ForceMode.Acceleration);
                }
                else if (rb != null)
                {
                    rb.MovePosition(next);
                }
                else
                {
                    grabbable.transform.position = next;
                }
            }
        }

        private void UpdateDwellTimes(Vector3 opening, float elapsed)
        {
            var radius = _configService.Dev.BackpackVacuumRadius;
            var count = Physics.OverlapSphereNonAlloc(opening, radius, _overlapResults, _itemLayerMask, QueryTriggerInteraction.Ignore);

            _seenThisScan.Clear();
            for (var i = 0; i < count; i++)
            {
                var grabbable = _overlapResults[i].GetComponentInParent<HVRGrabbable>();
                if (grabbable == null || !_seenThisScan.Add(grabbable) || !IsPullable(grabbable))
                    continue;

                // Only time spent inside the opening counts towards storing. Items further out are just pulled.
                var isInside = Vector3.Distance(grabbable.transform.position, opening) <= _captureRadius;
                _dwellTimes.TryGetValue(grabbable, out var dwell);
                dwell = isInside ? dwell + elapsed : 0f;
                _dwellTimes[grabbable] = dwell;

                if (dwell >= _configService.Dev.BackpackVacuumStoreSeconds)
                    TryStore(grabbable);
            }

            // Forget items that left the pull radius (or got grabbed/stored meanwhile).
            foreach (var gone in _dwellTimes.Keys.Where(i => !_seenThisScan.Contains(i)).ToList())
                _dwellTimes.Remove(gone);
        }

        private bool IsPullable(HVRGrabbable grabbable)
        {
            // Held, socketed (holster, backpack slot, loot panel, chest) or part of the backpack itself.
            if (grabbable.IsBeingHeld || grabbable.IsSocketed || grabbable.transform.IsChildOf(transform))
                return false;

            var container = grabbable.GetComponentInParent<VobLoader>()?.Container;
            return container != null && container.Vob.Type == VirtualObjectType.oCItem && !container.IsHeldByPlayer;
        }

        private void TryStore(HVRGrabbable grabbable)
        {
            _dwellTimes.Remove(grabbable);

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
