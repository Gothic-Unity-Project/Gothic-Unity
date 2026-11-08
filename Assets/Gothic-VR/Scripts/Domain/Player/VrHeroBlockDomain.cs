using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Melee = Gothic.VR.VRConstants.Melee;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// Gothic's parry in VR (option heroBlock), owned by VRHeroBlockService. Its animation holds the weapon with
    /// both hands (one hand at the grip, one at the blade) for one- and two-handed weapons alike - so: both hands on
    /// the same melee weapon and both triggers held. While it lasts the hero's props say so (IsBlocking, the weapon's
    /// material, the head's direction) and the NPC weapons touching the held one are reported to FightService.
    /// Per frame without allocations: the held weapon's colliders and each NPC's slots and weapon meshes are cached.
    /// </summary>
    public class VrHeroBlockDomain
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        // An NPC weapon within this of the held weapon touches it.
        private const int _bladeSamples = 6;

        private bool _isBlocking;
        private Transform _head;

        // The held weapon, resolved once per grab.
        private GameObject _cachedHeld;
        private bool _isCachedHeldMeleeWeapon;
        private int _cachedHeldMaterial = -1;
        private readonly List<Collider> _heldColliders = new();

        private readonly Dictionary<NpcContainer, NpcContactParts> _npcParts = new();
        // Reused for each slot's mesh lookup.
        private readonly List<MeshFilter> _filterBuffer = new();

        public VrHeroBlockDomain()
        {
            GlobalEventDispatcher.HeroBlockedHit.AddListener(OnHeroBlockedHit);
            GlobalEventDispatcher.NpcMeshCullingChanged.AddListener(OnNpcMeshCullingChanged);
            GlobalEventDispatcher.NpcVisualBodyChanged.AddListener(OnNpcVisualBodyChanged);
            GlobalEventDispatcher.LoadGameStart.AddListener(OnWorldChanging);
            GlobalEventDispatcher.UnloadGameStart.AddListener(OnWorldChanging);
        }

        public void Tick()
        {
            // No hero yet (menus, loading a world) - nothing to block, every frame until then.
            var hero = (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
            if (hero?.Props == null)
                return;

            var isBlocking = TryGetBlockingWeapon(out var material) &&
                             _vrInput.GetButton(VrHandSide.Left, VrButton.Trigger).IsActive &&
                             _vrInput.GetButton(VrHandSide.Right, VrButton.Trigger).IsActive;

            hero.Props.IsBlocking = isBlocking;
            hero.Props.BlockWeaponMaterial = material;
            var head = GetHead();
            if (head != null)
                hero.Props.BlockForward = head.forward;

            if (isBlocking)
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

        private Transform GetHead()
        {
            // A new player rig (scene reload) destroyed the old head: looked up again.
            if (_head != null)
                return _head;
            // IVrRig is a MonoBehaviour: Unity's null check, not ?.
            var rig = _vrPlayerService.Rig;
            if (rig as Object != null)
                _head = rig.Head;
            return _head;
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

            if (held != _cachedHeld)
                CacheHeld(held);

            if (!_isCachedHeldMeleeWeapon)
                return false;
            material = _cachedHeldMaterial;
            return true;
        }

        private void CacheHeld(GameObject held)
        {
            _cachedHeld = held;
            held.GetComponentsInChildren(_heldColliders);

            // By its symbol (a world item's vob name isn't always it).
            var container = held.GetComponentInParent<VobLoader>()?.Container;
            _isCachedHeldMeleeWeapon = container != null && container.IsMeleeWeapon();
            _cachedHeldMaterial = _isCachedHeldMeleeWeapon ? container.GetItemInstance()!.Material : -1;
        }

        /// <summary>
        /// Which NPC weapons (the drawn one in the hand slot, sampled along its
        /// length) or fists touch the held weapon right now - FightService blocks only their hits.
        /// </summary>
        private void ReportWeaponContacts(NpcContainer hero)
        {
            if (_heldColliders.Count == 0 || hero.Go == null)
                return;

            var heroPosition = hero.Go.transform.position;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc == hero || npc?.Go == null || !npc.Go.activeInHierarchy || npc.IsDown() ||
                    (npc.Go.transform.position - heroPosition).sqrMagnitude >
                    Melee.BlockContactRange * Melee.BlockContactRange)
                    continue;

                var fightMode = (VmGothicEnums.WeaponState)npc.Vob.FightMode;
                if (fightMode is not (VmGothicEnums.WeaponState.Fist or VmGothicEnums.WeaponState.W1H
                    or VmGothicEnums.WeaponState.W2H))
                    continue;

                if (IsTouching(GetParts(npc), fightMode))
                    _fightService.ReportBlockContact(npc);
            }
        }

        private bool IsTouching(NpcContactParts parts, VmGothicEnums.WeaponState fightMode)
        {
            foreach (var blade in parts.Blades)
            {
                if (blade.Transform == null)
                    continue;
                var from = blade.Transform.TransformPoint(blade.LocalFrom);
                var to = blade.Transform.TransformPoint(blade.LocalTo);
                for (var i = 0; i <= _bladeSamples; i++)
                {
                    if (IsNear(Vector3.Lerp(from, to, (float)i / _bladeSamples)))
                        return true;
                }
            }

            // Fists: the hand.
            return fightMode == VmGothicEnums.WeaponState.Fist && parts.Fist != null && IsNear(parts.Fist.position);
        }

        private bool IsNear(Vector3 point)
        {
            foreach (var collider in _heldColliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;
                if ((collider.ClosestPoint(point) - point).sqrMagnitude <=
                    Melee.BlockContactDistance * Melee.BlockContactDistance)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The NPC's hand slots and fist bone, found once per body. A drawn or stowed weapon changes a slot's child
        /// count: its blades are measured again then.
        /// </summary>
        private NpcContactParts GetParts(NpcContainer npc)
        {
            if (!_npcParts.TryGetValue(npc, out var parts) || parts.Go != npc.Go)
            {
                parts = new NpcContactParts
                {
                    Go = npc.Go,
                    RightSlot = npc.Go.FindChildRecursively(Constants.SlotRightHand)?.transform,
                    LeftSlot = npc.Go.FindChildRecursively(Constants.SlotLeftHand)?.transform,
                    Fist = npc.Go.FindChildRecursively(Constants.Animations.RightHandBoneName)?.transform
                };
                _npcParts[npc] = parts;
            }

            var rightCount = parts.RightSlot == null ? 0 : parts.RightSlot.childCount;
            var leftCount = parts.LeftSlot == null ? 0 : parts.LeftSlot.childCount;
            if (rightCount != parts.RightChildCount || leftCount != parts.LeftChildCount)
            {
                parts.RightChildCount = rightCount;
                parts.LeftChildCount = leftCount;
                parts.Blades.Clear();
                AddBlades(parts.RightSlot, parts.Blades);
                AddBlades(parts.LeftSlot, parts.Blades);
            }
            return parts;
        }

        /// <summary>
        /// Each mesh in the slot as a line along its longest axis (in the mesh's space).
        /// </summary>
        private void AddBlades(Transform slot, List<Blade> blades)
        {
            if (slot == null || slot.childCount == 0)
                return;

            slot.GetComponentsInChildren(_filterBuffer);
            foreach (var filter in _filterBuffer)
            {
                if (filter.sharedMesh == null)
                    continue;
                var bounds = filter.sharedMesh.bounds;
                var size = bounds.size;
                var axis = size.x >= size.y && size.x >= size.z ? Vector3.right
                    : size.y >= size.z ? Vector3.up : Vector3.forward;
                var half = Vector3.Scale(axis, bounds.extents);
                blades.Add(new Blade
                {
                    Transform = filter.transform,
                    LocalFrom = bounds.center - half,
                    LocalTo = bounds.center + half
                });
            }
            _filterBuffer.Clear();
        }

        private void OnNpcMeshCullingChanged(NpcContainer npc, NpcLoader loader, bool isInVisibleRange,
            bool wasOutOfDistance)
        {
            if (!isInVisibleRange && npc != null)
                _npcParts.Remove(npc);
        }

        private void OnNpcVisualBodyChanged(NpcContainer npc)
        {
            if (npc != null)
                _npcParts.Remove(npc);
        }

        private void OnWorldChanging()
        {
            _npcParts.Clear();
            _cachedHeld = null;
            _isCachedHeldMeleeWeapon = false;
            _heldColliders.Clear();
        }

        private void OnHeroBlockedHit(NpcContainer attacker)
        {
            _hapticsService.Vibrate(VrHandSide.Left, VrHapticPreset.Warning);
            _hapticsService.Vibrate(VrHandSide.Right, VrHapticPreset.Warning);
        }

        /// <summary>
        /// One NPC's parts that can touch the held weapon. Go tells a rebuilt body apart.
        /// </summary>
        private class NpcContactParts
        {
            public GameObject Go;
            public Transform RightSlot;
            public Transform LeftSlot;
            public Transform Fist;
            // -1: the blades aren't measured yet.
            public int RightChildCount = -1;
            public int LeftChildCount = -1;
            public readonly List<Blade> Blades = new();
        }

        /// <summary>
        /// A weapon mesh as a line along its longest axis, in the mesh's space.
        /// </summary>
        private struct Blade
        {
            public Transform Transform;
            public Vector3 LocalFrom;
            public Vector3 LocalTo;
        }
    }
}
