#if GOTHIC_HVR_INSTALLED
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrHeroBlock: Gothic's parry in VR. Its animation holds the weapon with both hands (one hand
    /// at the grip, one at the blade) for one- and two-handed weapons alike - so: both hands on the same melee weapon and
    /// both triggers held. While it lasts the hero's props say so (IsBlocking, the weapon's material, the head's
    /// direction); FightService blocks the NPC hits from the front and ignores the hero's own hits.
    /// </summary>
    public class VRHeroBlock : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        private bool _isBlocking;

        // DeveloperConfig.EnableBlockWeaponContact: an NPC weapon within this of the held weapon touches it.
        private const float _contactDistance = 0.08f;
        private const float _contactRange = 4f;
        private const int _bladeSamples = 6;
        private static readonly string[] _weaponSlots = { "ZS_RIGHTHAND", "ZS_LEFTHAND" };
        private const string _fistBone = "BIP01 R HAND";

        private void Awake()
        {
            this.Inject();
            GlobalEventDispatcher.HeroBlockedHit.AddListener(OnHeroBlockedHit);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.HeroBlockedHit.RemoveListener(OnHeroBlockedHit);
        }

        private void Update()
        {
            var hero = (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
            if (hero?.Props == null)
                return;

            var material = -1;
            var isBlocking = _configService.Dev.EnableVrHeroBlock && TryGetBlockingWeapon(out material) &&
                             VrControls.GetButton(VrHandSide.Left, VrButton.Trigger).IsActive &&
                             VrControls.GetButton(VrHandSide.Right, VrButton.Trigger).IsActive;

            hero.Props.IsBlocking = isBlocking;
            hero.Props.BlockWeaponMaterial = material;
            var head = Camera.main;
            if (head != null)
                hero.Props.BlockForward = head.transform.forward;

            if (isBlocking && _configService.Dev.EnableBlockWeaponContact)
                ReportWeaponContacts(hero);

            if (isBlocking == _isBlocking)
                return;

            _isBlocking = isBlocking;
            Logger.Log($"[HeroBlock] {(isBlocking ? "blocks" : "stops blocking")}", LogCat.Fight);
            if (isBlocking)
            {
                _hapticsService.Vibrate(VrHandSide.Left, VrHapticPreset.Info);
                _hapticsService.Vibrate(VrHandSide.Right, VrHapticPreset.Info);
            }
        }

        /// <summary>
        /// DeveloperConfig.EnableBlockWeaponContact: which NPC weapons (the drawn one in the hand slot, sampled along its
        /// length) or fists touch the held weapon right now - FightService blocks only their hits.
        /// </summary>
        private void ReportWeaponContacts(NpcContainer hero)
        {
            var held = _vrPlayerService.GrabbedItemLeft;
            if (held == null || hero.Go == null)
                return;
            var colliders = held.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
                return;

            var heroPosition = hero.Go.transform.position;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc == hero || npc?.Go == null || !npc.Go.activeInHierarchy || NpcHelperService.IsDown(npc) ||
                    (npc.Go.transform.position - heroPosition).sqrMagnitude > _contactRange * _contactRange)
                    continue;

                var fightMode = (VmGothicEnums.WeaponState)npc.Vob.FightMode;
                if (fightMode is not (VmGothicEnums.WeaponState.Fist or VmGothicEnums.WeaponState.W1H
                    or VmGothicEnums.WeaponState.W2H))
                    continue;

                if (IsTouching(npc, colliders))
                    _fightService.ReportBlockContact(npc);
            }
        }

        private static bool IsTouching(NpcContainer npc, Collider[] colliders)
        {
            foreach (var slotName in _weaponSlots)
            {
                var slot = npc.Go.FindChildRecursively(slotName);
                if (slot == null)
                    continue;
                foreach (var filter in slot.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null)
                        continue;
                    var bounds = filter.sharedMesh.bounds;
                    var size = bounds.size;
                    var axis = size.x >= size.y && size.x >= size.z ? Vector3.right
                        : size.y >= size.z ? Vector3.up : Vector3.forward;
                    var half = Vector3.Scale(axis, bounds.extents);
                    var from = filter.transform.TransformPoint(bounds.center - half);
                    var to = filter.transform.TransformPoint(bounds.center + half);
                    for (var i = 0; i <= _bladeSamples; i++)
                    {
                        if (IsNear(Vector3.Lerp(from, to, (float)i / _bladeSamples), colliders))
                            return true;
                    }
                }
            }

            // Fists: the hand.
            var fist = npc.Go.FindChildRecursively(_fistBone);
            return (VmGothicEnums.WeaponState)npc.Vob.FightMode == VmGothicEnums.WeaponState.Fist && fist != null &&
                   IsNear(fist.transform.position, colliders);
        }

        private static bool IsNear(Vector3 point, Collider[] colliders)
        {
            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;
                if ((collider.ClosestPoint(point) - point).sqrMagnitude <= _contactDistance * _contactDistance)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Both hands hold the same melee weapon (ITEM_KAT_NF).
        /// </summary>
        private bool TryGetBlockingWeapon(out int material)
        {
            material = -1;
            var held = _vrPlayerService.GrabbedItemLeft;
            if (held == null || held != _vrPlayerService.GrabbedItemRight)
                return false;

            var instanceName = held.GetComponentInParent<VobLoader>()?.Container?.Vob?.Name;
            var item = instanceName != null ? _vmCacheService.TryGetItemData(instanceName) : null;
            if (item == null || (VmGothicEnums.ItemFlags)item.MainFlag != VmGothicEnums.ItemFlags.ItemKatNf)
                return false;

            material = item.Material;
            return true;
        }

        private void OnHeroBlockedHit(NpcContainer attacker)
        {
            _hapticsService.Vibrate(VrHandSide.Left, VrHapticPreset.Warning);
            _hapticsService.Vibrate(VrHandSide.Right, VrHapticPreset.Warning);
        }
    }
}
#endif
