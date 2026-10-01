#if GOTHIC_HVR_INSTALLED
using System;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableDigSpots): mobs you "use with" a melee tool and which have an onStateFunc - e.g. G2's
    /// treasure X marks (oCMobInter, TREASURE_ADDON_01.ASC, useWithItem ItMw_2H_Axe_L_01 = pickaxe, onStateFunc
    /// B_SCGetTreasure). In Gothic the hero plays the dig animation and the engine calls "[onStateFunc]_S1" when state 1
    /// is reached. In VR: hit the mob DigHitsNeeded times with a swung melee weapon, then we call the same function
    /// (self = hero). The script does the rest (Wld_InsertItem, quest variables, XP, SVM).
    ///
    /// Hit detection only compares bounds + weapon speed - no colliders, layers or combat code involved.
    /// Added at runtime by VRFocus.
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

        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;

        private IInteractiveObject _mob;
        private Renderer[] _renderers;
        private HVRHandGrabber[] _hands;
        private int _hits;
        private float _lastHitTime = -999f;


        /// <summary>
        /// Generic for G2 and mods: any interactive mob with an onStateFunc which is used with a melee weapon/tool.
        /// </summary>
        public static bool IsDigSpot(VobContainer container, VmCacheService vmCacheService)
        {
            if (container?.Vob is not IInteractiveObject mob || container.Vob is IDoor || container.Vob is IContainer)
                return false;
            if (string.IsNullOrEmpty(mob.OnStateChangeFunction) || string.IsNullOrEmpty(mob.Item))
                return false;

            var tool = vmCacheService.TryGetItemData(mob.Item);
            return tool != null && ((VmGothicEnums.ItemFlags)tool.MainFlag & VmGothicEnums.ItemFlags.ItemKatNf) != 0;
        }

        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            var loader = GetComponentInParent<VobLoader>();
            _mob = loader?.Container?.Vob as IInteractiveObject;
            _renderers = loader != null ? loader.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            _hands = FindObjectsByType<HVRHandGrabber>(FindObjectsSortMode.None);

            // Also tells whether the mob is visible at all (G2 X marks were invisible in an older playtest).
            Logger.Log($"[VRDigSpot] '{loader?.name}' ready: func={_mob?.OnStateChangeFunction}{_stateOneSuffix}, " +
                       $"tool={_mob?.Item}, renderers={_renderers.Length}", LogCat.VR);
        }

        private void FixedUpdate()
        {
            if (_mob == null || !_configService.Dev.EnableDigSpots)
                return;
            if (Time.time - _lastHitTime < _hitCooldown)
                return;
            if (!TryGetBounds(out var spotBounds))
                return;

            foreach (var hand in _hands)
            {
                var weapon = hand != null ? hand.GrabbedTarget : null;
                if (weapon == null || !IsAllowedTool(weapon))
                    continue;

                var rb = weapon.Rigidbody;
                if (rb == null || rb.linearVelocity.magnitude < _minSwingSpeed)
                    continue;

                if (!IntersectsWeapon(weapon, spotBounds))
                    continue;

                RegisterHit(weapon);
                return;
            }
        }

        private bool IsAllowedTool(HVRGrabbable weapon)
        {
            var container = weapon.GetComponentInParent<VobLoader>()?.Container;
            var item = container?.GetItemInstance();
            if (item == null || ((VmGothicEnums.ItemFlags)item.MainFlag & VmGothicEnums.ItemFlags.ItemKatNf) == 0)
                return false;

            // Default: any melee weapon (easier to test). Vanilla requires the mob's tool (e.g. the pickaxe).
            if (!_configService.Dev.DigSpotsRequireTool)
                return true;

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
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (hasBounds)
                bounds.Expand(_boundsPadding * 2f);
            return hasBounds;
        }

        private static bool IntersectsWeapon(HVRGrabbable weapon, Bounds spotBounds)
        {
            foreach (var weaponCollider in weapon.GetComponentsInChildren<Collider>())
            {
                if (weaponCollider.enabled && spotBounds.Intersects(weaponCollider.bounds))
                    return true;
            }

            return false;
        }

        private void RegisterHit(HVRGrabbable weapon)
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
            var vm = _gameStateService.GothicVm;
            var functionName = _mob.OnStateChangeFunction + _stateOneSuffix;
            var symbol = vm.GetSymbolByName(functionName);
            if (symbol == null)
            {
                Logger.LogWarning($"[VRDigSpot] '{functionName}' not found in the scripts.", LogCat.VR);
                return;
            }

            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            try
            {
                vm.Call(symbol.Index);
                Logger.Log($"[VRDigSpot] Called '{functionName}'", LogCat.VR);
            }
            catch (Exception e)
            {
                Logger.LogError($"[VRDigSpot] '{functionName}' failed: {e.Message}", LogCat.VR);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
        }
    }
}
#endif
