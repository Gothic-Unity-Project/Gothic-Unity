using System;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Vm;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// Mobs you "use with" a melee tool and which have an onStateFunc - e.g. G2's
    /// treasure X marks (oCMobInter, TREASURE_ADDON_01.ASC, useWithItem ItMw_2H_Axe_L_01 = pickaxe, onStateFunc
    /// B_SCGetTreasure), see VmCacheService.IsDigSpot. In Gothic the hero plays the dig animation and the engine calls
    /// "[onStateFunc]_S1" when state 1 is reached. In VR: hit the mob _digHitsNeeded times with a swung melee weapon,
    /// then we call the same function (self = hero). The script does the rest (Wld_InsertItem, quest variables, XP).
    ///
    /// Hit detection only compares bounds + weapon speed - no colliders, layers or combat code involved.
    /// Part of the oCMobInter/DigSpot prefab variant (PrefabType.VobInteractableDigSpot).
    /// </summary>
    public class VRDigSpot : MonoBehaviour
    {
        private const int _digHitsNeeded = 3;

        // Minimum weapon speed (m/s) for a touch to count as a hit.
        private const float _minSwingSpeed = 1.2f;

        // A single swing can overlap for several physics ticks - count it once.
        private const float _hitCooldown = 0.4f;
        private const float _boundsPadding = 0.1f;
        private const string _stateOneSuffix = "_S1";

        [Inject] private readonly VmService _vmService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private IInteractiveObject _mob;
        private Renderer[] _renderers;
        private int _hits;
        private float _lastHitTime = float.NegativeInfinity;
        private GameObject _lastWeapon;
        private bool? _isLastWeaponAllowed;
        private Collider[] _lastWeaponColliders;
        private Rigidbody _lastWeaponRigidbody;

        private void Start()
        {
            var loader = GetComponentInParent<VobLoader>();
            _mob = loader?.Container?.Vob as IInteractiveObject;
            _renderers = loader != null ? loader.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();

            // Also tells whether the mob is visible at all (G2 X marks were invisible in an older playtest).
            Logger.Log($"[VRDigSpot] '{loader?.name}' ready: func={_mob?.OnStateChangeFunction}{_stateOneSuffix}, " +
                       $"tool={_mob?.Item}, renderers={_renderers.Length}", LogCat.VR);
        }

        private void FixedUpdate()
        {
            if (_mob == null)
                return;
            if (Time.time - _lastHitTime < _hitCooldown)
                return;
            if (!TryGetBounds(out var spotBounds))
                return;

            if (TryRegisterHit(_vrPlayerService.GrabbedItemLeft, spotBounds))
                return;

            TryRegisterHit(_vrPlayerService.GrabbedItemRight, spotBounds);
        }

        /// <summary>
        /// Checks the item in one hand (null if the hand is empty).
        /// </summary>
        private bool TryRegisterHit(GameObject weapon, Bounds spotBounds)
        {
            if (weapon == null)
                return false;

            CacheWeapon(weapon);

            var rb = _lastWeaponRigidbody;
            if (rb == null || rb.linearVelocity.magnitude < _minSwingSpeed)
                return false;

            if (!IntersectsWeapon(spotBounds))
                return false;

            // Cheapest checks first: the tool lookup only runs for a fast weapon touching the spot.
            if (!IsAllowedTool(weapon))
                return false;

            RegisterHit(weapon);
            return true;
        }

        private void CacheWeapon(GameObject weapon)
        {
            if (weapon == _lastWeapon)
                return;

            _lastWeapon = weapon;
            _isLastWeaponAllowed = null;
            _lastWeaponColliders = weapon.GetComponentsInChildren<Collider>();
            _lastWeaponRigidbody = weapon.GetComponent<Rigidbody>();
        }

        private bool IsAllowedTool(GameObject weapon)
        {
            _isLastWeaponAllowed ??= CheckAllowedTool(weapon);
            return _isLastWeaponAllowed.Value;
        }

        private bool CheckAllowedTool(GameObject weapon)
        {
            var container = weapon.GetComponentInParent<VobLoader>()?.Container;
            var item = container?.GetItemInstance();
            if (item == null || ((VmGothicEnums.ItemFlags)item.MainFlag & VmGothicEnums.ItemFlags.ItemKatNf) == 0)
                return false;

            // Like vanilla, only the mob's own tool (e.g. the pickaxe) digs.
            var vobItem = container.VobAs<IItem>();
            var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
            return instanceName.EqualsIgnoreCase(_mob.Item);
        }

        private bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            var hasBounds = false;
            foreach (var r in _renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (hasBounds)
                bounds.Expand(_boundsPadding * 2f);
            return hasBounds;
        }

        private bool IntersectsWeapon(Bounds spotBounds)
        {
            foreach (var weaponCollider in _lastWeaponColliders)
            {
                if (weaponCollider != null && weaponCollider.enabled && spotBounds.Intersects(weaponCollider.bounds))
                    return true;
            }

            return false;
        }

        private void RegisterHit(GameObject weapon)
        {
            _lastHitTime = Time.time;
            _hits++;
            Logger.Log($"[VRDigSpot] Hit {_hits}/{_digHitsNeeded} with '{weapon.name}'", LogCat.VR);

            if (_hits < _digHitsNeeded)
                return;

            _hits = 0;
            Dig();
        }

        /// <summary>
        /// Same as the engine reaching mob state 1: call "[onStateFunc]_S1" with self = hero.
        /// Calling it again is safe - vanilla scripts track what was already dug up (e.g. G2 RAKEPLACE[]).
        /// </summary>
        private void Dig()
        {
            var functionName = _mob.OnStateChangeFunction + _stateOneSuffix;
            if (_vmService.CallAsHero(functionName, LogCat.VR))
                Logger.Log($"[VRDigSpot] Called '{functionName}'", LogCat.VR);
            else
                Logger.LogWarning($"[VRDigSpot] '{functionName}' not found in the scripts or failed.", LogCat.VR);
        }
    }
}
