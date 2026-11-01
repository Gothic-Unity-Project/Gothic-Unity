using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Creator;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Extensions;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    public class NpcHelperService
    {
        /// <summary>
        /// Ranges are in meter.
        /// FIXME - We should use PERC_ASSESSTALK range to leverage the VR grab hover and remote grab distance!
        /// </summary>
        public readonly Dictionary<VmGothicEnums.PerceptionType, int> PerceptionRanges = new ();
        
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly VmCacheService _vmCacheService;

        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly MeshService _meshService;

        // Items closer to the hero than this (m) may be in his hands or holsters - NPCs don't see them.
        private const float _heroItemDistance = 1f;

        // Each world item's moving part (its rigidbody's transform - the loader stays where it was spawned while the
        // hero carries the item away), looked up once: Wld_DetectItem walks all world items on every call.
        private readonly Dictionary<VobContainer, Transform> _itemBodies = new();

        // The world item Wld_DetectItem found, per spawned NPC (not by the instance's index, which all wolves share):
        // the scripts only get its instance ('item') - shared by all items of that instance.
        private readonly Dictionary<NpcContainer, VobContainer> _detectedItems = new();

        private const float _fpLookupDistance = 7f; // meter
        private static readonly int _raycastLayersToUse = 1 << Constants.DefaultLayer;
        // A sight line looks through at most this many see-through polygons of one collider, a step past each.
        private const int _maxSeeThroughSteps = 8;
        private const float _seeThroughStep = 0.01f;

        // Sight lines (CanSeeNpc): their hits, per collider the materials drawn on its mesh (null: none to look
        // through) and per material whether sight passes it. Both caches hold one world's objects.
        private readonly RaycastHit[] _sightHits = new RaycastHit[16];
        private readonly Dictionary<Collider, Material[]> _sightMaterials = new();
        private readonly Dictionary<Material, bool> _seeThroughMaterials = new();

        public NpcHelperService()
        {
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(OnLoadingSceneLoaded);
        }

        private void OnLoadingSceneLoaded()
        {
            _sightMaterials.Clear();
            _seeThroughMaterials.Clear();
            _itemBodies.Clear();
        }

        public void Init()
        {
            // Perceptions
            var percInitSymbol = _gameStateService.GothicVm.GetSymbolByName("InitPerceptions");
            if (percInitSymbol == null)
            {
                Logger.LogError("InitPerceptions symbol not found.", LogCat.Npc);
            }
            else
            {
                _gameStateService.GothicVm.Call(percInitSymbol.Index);
            }
        }

        public void ExtPErcSetRange(int perceptionId, int rangeInCm)
        {
            PerceptionRanges[(VmGothicEnums.PerceptionType)perceptionId] = rangeInCm / 100;
        }

        public bool ExtIsMobAvailable(NpcInstance npcInstance, string vobName)
        {
            var npc = GetNpc(npcInstance);
            var container = _vobService.GetFreeInteractableWithin10M(npc.transform.position, vobName);
            if (container == null)
                return false;

            // Without a spot, AI_UseMob gave up at once and routine loops (e.g. ZS_Repair_Hut_Loop) asked again
            // every frame.
            return _vobService.IsMobUsableFrom(container, npc.transform.position);
        }

        public int ExtWldGetMobState(NpcInstance npcInstance, string scheme)
        {
            var npcGo = GetNpc(npcInstance);

            var prefabProps = npcInstance.GetUserData().PrefabProps;

            var mob = prefabProps.CurrentInteractable ??
                      _vobService.GetFreeInteractableWithin10M(npcGo.transform.position, scheme);

            if (mob?.Props is not InteractiveProperties)
                return -1;

            return Math.Max(0, mob.MobState);
        }

        public ItemInstance ExtNpcGetEquippedMeleeWeapon(NpcInstance npc)
        {
            var meleeWeapon = GetProperties(npc).EquippedItems
                .FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatNf);

            return meleeWeapon;
        }

        public bool ExtNpcHasEquippedMeleeWeapon(NpcInstance npc)
        {
            return ExtNpcGetEquippedMeleeWeapon(npc) != null;
        }

        public ItemInstance ExtNpcGetEquippedRangedWeapon(NpcInstance npc)
        {
            var rangedWeapon = GetProperties(npc).EquippedItems
                .FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatFf);

            return rangedWeapon;
        }

        public bool ExtNpcHasEquippedRangedWeapon(NpcInstance npc)
        {
            return ExtNpcGetEquippedRangedWeapon(npc) != null;
        }

        /// <summary>
        /// Like the engine (OpenGothic Inventory::hasRangedWeaponWithAmmo): a bow or crossbow, carried or equipped,
        /// and its munition in the inventory. Archers' scripts choose the ranged weapon by it (B_SelectWeapon).
        /// </summary>
        public bool ExtNpcHasRangedWeaponWithAmmo(NpcInstance npc)
        {
            return GetWeapons(npc, VmGothicEnums.ItemFlags.ItemKatFf).Any(i => HasMunition(npc, i));
        }

        /// <summary>
        /// AI_EquipBestMeleeWeapon / AI_EquipBestRangedWeapon: like the engine (OpenGothic Inventory::bestItem), the
        /// carried weapon of that kind with the most damage (then value) whose attribute conditions are met and whose
        /// munition is there, replacing the equipped one of that kind.
        /// </summary>
        public void ExtAiEquipBestWeapon(NpcInstance npc, VmGothicEnums.ItemFlags kind)
        {
            var container = GetContainer(npc);
            if (container?.Props == null)
                return;

            var best = GetWeapons(npc, kind)
                .Where(i => HasMunition(npc, i) && MeetsConditions(container, i))
                .OrderByDescending(i => i.DamageTotal)
                .ThenByDescending(i => i.Value)
                .FirstOrDefault();
            if (best == null || container.Props.EquippedItems.Any(i => i.Index == best.Index))
                return;

            var replaced = container.Props.EquippedItems.Where(i => i.MainFlag == (int)kind).ToList();
            container.Props.EquippedItems.RemoveAll(i => i.MainFlag == (int)kind);
            container.Props.EquippedItems.Add(best);
            ReplaceWeaponMesh(container, replaced, best);
        }

        /// <summary>
        /// The new weapon shows on the body instead of the old one (found on BoroBongo's branch: a newly chosen bow was
        /// invisible). An NPC out of sight gets its weapons from EquippedItems when it's built.
        /// </summary>
        private void ReplaceWeaponMesh(NpcContainer npc, List<ItemInstance> replaced, ItemInstance weapon)
        {
            if (npc.Go == null)
                return;

            foreach (var item in replaced)
            {
                var slot = npc.Go.FindChildRecursively(DrawWeapon.GetStowSlotName(GetWeaponState(item)));
                if (slot == null)
                    continue;

                for (var i = slot.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(slot.transform.GetChild(i).gameObject);
            }

            _meshService.CreateNpcWeapon(npc.Go, weapon, (VmGothicEnums.ItemFlags)weapon.MainFlag,
                (VmGothicEnums.ItemFlags)weapon.Flags);
        }

        private static VmGothicEnums.WeaponState GetWeaponState(ItemInstance weapon)
        {
            var flags = (VmGothicEnums.ItemFlags)weapon.Flags;
            if (weapon.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatFf)
                return flags.HasFlag(VmGothicEnums.ItemFlags.ItemCrossbow)
                    ? VmGothicEnums.WeaponState.CBow
                    : VmGothicEnums.WeaponState.Bow;

            return flags.HasFlag(VmGothicEnums.ItemFlags.Item2HdAxe) ||
                   flags.HasFlag(VmGothicEnums.ItemFlags.Item2HdSwd)
                ? VmGothicEnums.WeaponState.W2H
                : VmGothicEnums.WeaponState.W1H;
        }

        private IEnumerable<ItemInstance> GetWeapons(NpcInstance npc, VmGothicEnums.ItemFlags kind)
        {
            var props = GetProperties(npc);
            var carried = _npcInventoryService.GetAllInventoryItems(npc)
                .Select(i => _vmCacheService.TryGetItemData(i.Name))
                .Where(i => i != null);
            return carried.Concat(props?.EquippedItems ?? Enumerable.Empty<ItemInstance>())
                .Where(i => i.MainFlag == (int)kind);
        }

        /// <summary>
        /// A bow or crossbow needs its munition in the inventory - one without munition set has none, like the engine
        /// (OpenGothic hasAmmunition): a mod archer aimed forever without shooting. Melee weapons need nothing.
        /// </summary>
        private bool HasMunition(NpcInstance npc, ItemInstance weapon)
        {
            if (weapon.MainFlag != (int)VmGothicEnums.ItemFlags.ItemKatFf)
                return true;

            return weapon.Munition > 0 && _npcInventoryService.ExtNpcHasItems(npc, weapon.Munition) > 0;
        }

        private static bool MeetsConditions(NpcContainer npc, ItemInstance item)
        {
            foreach (var slot in new[] { ItemConditionSlot.Slot0, ItemConditionSlot.Slot1, ItemConditionSlot.Slot2 })
            {
                var value = item.GetCondValue(slot);
                if (value != 0 && npc.Vob.GetAttribute(item.GetCondAtr(slot)) < value)
                    return false;
            }

            return true;
        }

        public bool ExtIsNpcOnFp(NpcInstance npc, string vobNamePart)
        {
            var freePoint = GetProperties(npc).CurrentFreePoint;

            if (freePoint == null)
            {
                return false;
            }

            return freePoint.Name.ContainsIgnoreCase(vobNamePart);
        }

        /// <summary>
        /// Returns true and sets VM.other, if NPC was found.
        ///
        /// Hint:
        /// As WldDetectNpc and WldDetectNpc seem to be the same logic except one parameter, we implement both in this function.
        /// </summary>
        public bool ExtWldDetectNpcEx(NpcInstance npcInstance, int specificNpcIndex, int aiState, int guild,
            bool detectPlayer)
        {
            var npcPos = npcInstance.GetUserData().Go.transform.position;

            var sensesRangeMeters = npcInstance.SensesRange / 100f;
            var sensesRangeSqr = sensesRangeMeters * sensesRangeMeters;

            // NpcService.CacheHero() calls Vm.InitInstance(heroInstance) BEFORE assigning Vm.GlobalHero.
            // A (mod) script calling this during that window finds no hero yet.
            var globalHeroIndex = _gameStateService.GothicVm.GlobalHero?.Index;

            var foundNpc = _multiTypeCacheService.NpcCache
                .Where(i => i.Props != null) // ignore empty (safe check)
                .Where(i => i.Go != null) // ignore empty (safe check)
                .Where(i => i.Instance.Index != npcInstance.Index) // ignore self
                .Where(i => detectPlayer ||
                            globalHeroIndex == null ||
                            i.Instance.Index != globalHeroIndex) // if we don't detect player, then skip it
                .Where(i => specificNpcIndex < 0 ||
                            specificNpcIndex == i.Instance.Index) // Specific NPC is found right now?
                .Where(i => aiState < 0 || aiState == i.Vob.CurrentStateIndex)
                .Where(i => guild < 0 || i.Instance.Guild == guild) // check guild
                .Where(i => (i.Go.transform.position - npcPos).sqrMagnitude <= sensesRangeSqr) // detect only within senses range
                .OrderBy(i => (i.Go.transform.position - npcPos).sqrMagnitude) // get nearest
                .FirstOrDefault();

            // without this Dialog box stops and breaks the entire NPC logic
            if (foundNpc == null)
            {
                return false;
            }

            // We need to set it, as there are calls where we immediately need _other_. e.g.:
            // if (Wld_DetectNpc(self, ...) && (Npc_GetDistToNpc(self, other)<HAI_DIST_SMALLTALK)
            if (foundNpc.Instance != null)
            {
                _gameStateService.GothicVm.GlobalOther = foundNpc.Instance;
            }

            return foundNpc.Instance != null;
        }

        public int ExtNpcGetDistToWp(NpcInstance npc, string waypointName)
        {
            var npcGo = GetNpc(npc);
            var waypoint = _wayNetService.GetWayNetPoint(waypointName);

            if (!npcGo || waypoint == null)
                return int.MaxValue;

            // *100 as Gothic metrics are in cm, not m.
            return (int)(Vector3.Distance(npcGo.transform.position, waypoint.Position) * 100);
        }

        /// <summary>
        /// Wld_DetectItem, like the engine (OpenGothic GameScript::wld_detectitem): the nearest world item within the
        /// NPC's senses range whose main flag matches becomes 'item'.
        /// </summary>
        public bool ExtWldDetectItem(NpcInstance npc, int flags)
        {
            var npcContainer = GetContainer(npc);
            var npcGo = npcContainer?.Go;
            if (npcGo == null)
                return false;

            var npcPos = npcGo.transform.position;
            var range = npc.SensesRange / 100f;
            var heroGo = (_gameStateService.GothicVm.GlobalHero as NpcInstance)?.GetUserData()?.Go;
            VobContainer nearest = null;
            ItemInstance nearestItem = null;
            var nearestDistance = range * range;

            foreach (var container in _multiTypeCacheService.VobCache)
            {
                if (container.Go == null || container.Vob is not IItem vobItem ||
                    !TryGetItemBody(container, out var body))
                    continue;

                var position = body.position;
                var distance = (position - npcPos).sqrMagnitude;
                if (distance > nearestDistance)
                    continue;
                if (heroGo != null && IsNearHero(position, heroGo.transform.position))
                    continue;

                var item = _vmCacheService.TryGetItemData(GetItemInstanceName(vobItem));
                if (item == null || (item.MainFlag & flags) == 0)
                    continue;

                nearest = container;
                nearestItem = item;
                nearestDistance = distance;
            }

            if (nearest == null)
                return false;

            _gameStateService.GothicVm.GlobalItem = nearestItem;
            _detectedItems[npcContainer] = nearest;
            return true;
        }

        /// <summary>
        /// The world item behind the scripts' 'item' for this NPC: the one it detected last, if it is of that instance
        /// and still lies in the world.
        /// </summary>
        public VobContainer GetDetectedItem(NpcInstance npc, ItemInstance item)
        {
            var npcContainer = npc?.GetUserData();
            if (npcContainer == null || item == null || !_detectedItems.TryGetValue(npcContainer, out var detected) ||
                detected?.Go == null || detected.Vob is not IItem vobItem)
                return null;

            return _vmCacheService.TryGetItemData(GetItemInstanceName(vobItem))?.Index == item.Index ? detected : null;
        }

        /// <returns>cm, int.MaxValue without the item.</returns>
        public int ExtNpcGetDistToItem(NpcInstance npc, ItemInstance item)
        {
            var npcGo = GetContainer(npc)?.Go;
            var container = GetDetectedItem(npc, item);
            if (npcGo == null || container == null)
                return int.MaxValue;

            return (int)(Vector3.Distance(npcGo.transform.position, GetItemPosition(container)) * 100);
        }

        /// <returns>cm of height difference, int.MaxValue without the item.</returns>
        public int ExtNpcGetHeightToItem(NpcInstance npc, ItemInstance item)
        {
            var npcGo = GetContainer(npc)?.Go;
            var container = GetDetectedItem(npc, item);
            if (npcGo == null || container == null)
                return int.MaxValue;

            return (int)(Mathf.Abs(GetItemPosition(container).y - npcGo.transform.position.y) * 100);
        }

        /// <summary>
        /// Within the senses range and nothing solid between the NPC's eyes and the item.
        /// </summary>
        public bool ExtNpcCanSeeItem(NpcInstance npc, ItemInstance item)
        {
            var npcGo = GetContainer(npc)?.Go;
            var container = GetDetectedItem(npc, item);
            if (npcGo == null || container == null)
                return false;

            var eyes = npcGo.transform.position + Vector3.up * 1.6f;
            var target = GetItemPosition(container) + Vector3.up * 0.1f;
            var toTarget = target - eyes;
            if (toTarget.magnitude > npc.SensesRange / 100f)
                return false;

            return !Physics.Raycast(eyes, toTarget.normalized, toTarget.magnitude, 1 << Constants.DefaultLayer,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Wld_RemoveItem (G1: a monster ate the food it found).
        /// </summary>
        public void ExtWldRemoveItem(NpcInstance npc, ItemInstance item)
        {
            var container = GetDetectedItem(npc, item);
            if (container != null)
                _vobService.RemoveWorldItem(container);
        }

        /// <summary>
        /// The item's moving part (its rigidbody), not its loader that stays where it was spawned.
        /// </summary>
        public static Vector3 GetItemPosition(VobContainer container)
        {
            var body = container.Go.GetComponentInChildren<Rigidbody>();
            return body != null ? body.position : container.Go.transform.position;
        }

        public static Transform GetItemTransform(VobContainer container)
        {
            var body = container.Go.GetComponentInChildren<Rigidbody>();
            return body != null ? body.transform : container.Go.transform;
        }

        /// <summary>
        /// GetItemTransform, cached. False while the item's object is inactive or not loaded yet.
        /// </summary>
        private bool TryGetItemBody(VobContainer container, out Transform body)
        {
            body = null;
            if (!container.Go.activeInHierarchy)
                return false;
            if (_itemBodies.TryGetValue(container, out body) && body != null)
                return true;

            // Not looked up yet, or its rigidbody's object is gone (reloaded): the entry is dropped and looked up
            // again.
            _itemBodies.Remove(container);
            if (!_vobService.IsLoaded(container))
                return false;

            body = GetItemTransform(container);
            _itemBodies[container] = body;
            return true;
        }

        private static bool IsNearHero(Vector3 position, Vector3 heroPosition)
        {
            position.y = 0f;
            heroPosition.y = 0f;
            return (position - heroPosition).sqrMagnitude < _heroItemDistance * _heroItemDistance;
        }

        private static string GetItemInstanceName(IItem vobItem)
        {
            return !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
        }

        public int ExtNpcGetTalentSkill(NpcInstance npc, int skillId)
        {
            // Same storage as Npc_SetTalentSkill.
            // FIXME - skill levels should also drive the fight overlays (like OpenGothic does).
            return GetContainer(npc)?.Vob?.GetTalent(skillId)?.Skill ?? 0;
        }

        public int ExtNpcGetTalentValue(NpcInstance npc, int skillId)
        {
            return GetContainer(npc)?.Vob?.GetTalent(skillId)?.Value ?? 0;
        }

        /// <summary>
        /// Percentage shown in the status menu. G2 keeps the weapon talents' percentages in C_NPC.hitchance[]
        /// (B_AddFightSkill), G1 in the talent value (Npc_SetTalentValue).
        /// </summary>
        public int GetTalentPercent(NpcContainer npc, int talentIndex)
        {
            if (_contextGameVersionService.IsGothic2() && talentIndex <= (int)NpcTalent.Crossbow)
                return npc.Instance.GetHitChance((NpcTalent)talentIndex);

            return npc.Vob.GetTalent(talentIndex).Value;
        }

        public VmGothicEnums.Attitude GetPersonAttitude(NpcContainer self, NpcContainer other)
        {
            // Like the engine, an NPC has its own attitude to the player only: the temporary one (e.g. because the
            // hero stole something), else the permanent one of the scripts (Npc_SetAttitude). Unset: the guilds'.
            if (other.PrefabProps.IsHero())
            {
                if (self.Vob.AttitudeTemp != (int)VmGothicEnums.Attitude.Null)
                    return (VmGothicEnums.Attitude)self.Vob.AttitudeTemp;
                if (self.Vob.Attitude != (int)VmGothicEnums.Attitude.Null)
                    return (VmGothicEnums.Attitude)self.Vob.Attitude;
            }

            return GetGuildAttitude(self.Vob.Guild, other.Vob.Guild);
        }

        /// <summary>
        /// Npc_GetPermAttitude: like GetPersonAttitude(), but never the temporary attitude. ZS_Attack_End and the
        /// knockout rule ask it whether the fight was a brawl among friends or meant to kill.
        /// </summary>
        public VmGothicEnums.Attitude GetPermanentAttitude(NpcContainer self, NpcContainer other)
        {
            if (other.PrefabProps.IsHero() && self.Vob.Attitude != (int)VmGothicEnums.Attitude.Null)
                return (VmGothicEnums.Attitude)self.Vob.Attitude;

            // Like the engine (OpenGothic personAttitude), the attitude between the hero and an NPC is the NPC's, both
            // ways: whether the hero dies or is knocked out depends on the attacker's attitude to him.
            if (self.IsHero() && other.Vob.Attitude != (int)VmGothicEnums.Attitude.Null)
                return (VmGothicEnums.Attitude)other.Vob.Attitude;

            return GetGuildAttitude(self.Vob.Guild, other.Vob.Guild);
        }

        private VmGothicEnums.Attitude GetGuildAttitude(int selfGuild, int otherGuild)
        {
            return (VmGothicEnums.Attitude)_gameStateService.GuildAttitudes[selfGuild * _gameStateService.GuildCount + otherGuild];
        }

        [CanBeNull]
        private GameObject GetNpc([CanBeNull] NpcInstance npc)
        {
            // Scripts pass NPCs that aren't spawned (yet), e.g. Xardas' dialog asks for Lester's distance to a WP.
            return npc?.GetUserData()?.Go;
        }

        private NpcContainer GetContainer(NpcInstance npc)
        {
            return npc.GetUserData();
        }

        private NpcProperties GetProperties([CanBeNull] NpcInstance npc)
        {
            return npc?.GetUserData().Props;
        }

        /// <summary>
        /// Senses check based on C_NPC.senses: hearing and smelling only need the range check,
        /// seeing additionally needs a free line of sight (and FOV unless freeLOS is set).
        /// </summary>
        public bool CanSenseNpc(NpcInstance self, NpcInstance other, bool freeLOS, float maxRangeMeters = float.MaxValue)
        {
            var selfGo = self.GetUserData()?.Go;
            var otherGo = other.GetUserData()?.Go;

            // Not spawned yet (e.g. out of range): nothing to sense, like in CanSeeNpc.
            if (selfGo == null || otherGo == null)
            {
                return false;
            }

            var senseRangeMeters = Mathf.Min(self.SensesRange / 100f, maxRangeMeters); // daedalus values are in cm, we need them in m
            var range = Vector3.Distance(otherGo.transform.position, selfGo.transform.position);

            if (range > senseRangeMeters)
            {
                return false;
            }

            var senses = (VmGothicEnums.NpcSenses)self.Senses;

            // Defensive: an NPC without configured senses keeps the previous distance-only behavior.
            if (senses == 0)
            {
                return true;
            }

            if ((senses & (VmGothicEnums.NpcSenses.Hear | VmGothicEnums.NpcSenses.Smell)) != 0)
            {
                return true;
            }

            return CanSeeNpc(self, other, freeLOS);
        }

        /// <summary>
        /// freeLOS - Free Line Of Sight == ignoreFOV
        /// fov = 50 - OpenGothic assumes 100 fov for NPCs
        /// fov = 30 - We reuse this for Focus angle during AI_Attack()
        /// </summary>
        public bool CanSeeNpc(NpcInstance self, NpcInstance other, bool freeLOS, float fov = 50f)
        {
            var selfContainer = self.GetUserData();
            var otherContainer = other.GetUserData();

            if (selfContainer == null || otherContainer == null)
            {
                return false;
            }

            // Hint: For forward direction check, we can't use HeadMesh as e.g., Molerat's head is rotated to have red axis (right) as forward.
            //       OpenGothic is therefore using Body rotation, too.
            var selfRoot = selfContainer.Go.transform;
            var otherRoot = otherContainer.Go.transform;
            var selfHead = selfContainer.PrefabProps.Head ?? selfRoot;
            var otherHead = otherContainer.PrefabProps.Head ?? otherRoot;

            var selfGroundPosition = selfRoot.position;
            var otherGroundPosition = otherRoot.position;
            // Unity places positions of objects at the bottom. We need to lift them up towards the head
            // The VR hero's head is the camera: in room scale it isn't above the rig's origin.
            var selfRealHeadPosition = selfContainer.PrefabProps?.IsHero() == true
                ? selfHead.position
                : new Vector3(selfRoot.position.x, selfHead.position.y, selfRoot.position.z);
            var otherRealHeadPosition = otherContainer.PrefabProps?.IsHero() == true
                ? otherHead.position
                : new Vector3(otherRoot.position.x, otherHead.position.y, otherRoot.position.z);

            var distanceToNpc = Vector3.Distance(selfRealHeadPosition, otherRealHeadPosition);
            var inSightRange = distanceToNpc <= self.SensesRange / 100f; // SensesRange is in cm.

            var hasLineOfSightCollisions = inSightRange &&
                                           IsSightBlocked(selfRealHeadPosition, otherRealHeadPosition, selfRoot, otherRoot);

            // Calculate horizontal direction only (ignore Y axis for FOV check), basically a Gobbo is only using x+z for FOV and hero standing in front of it will work correctly.
            var directionToTarget = new Vector3(
                otherGroundPosition.x - selfGroundPosition.x,
                0f,
                otherGroundPosition.z - selfGroundPosition.z
            ).normalized;
            // UseMob aligns the root with the mob slot, but sit transitions turn the body around - look the way it does.
            // (BsSit is already set while walking to the mob, the state id only once seated.)
            var isSeated = selfContainer.Props.BodyState == VmGothicEnums.BodyState.BsSit && selfContainer.IsUsingMob();
            var selfForward = isSeated ? -selfRoot.forward : selfRoot.forward;
            var selfForwardHorizontal = new Vector3(selfForward.x, 0f, selfForward.z).normalized;
            var angleToTarget = Vector3.Angle(selfForwardHorizontal, directionToTarget);
            var inFov = angleToTarget <= fov;

            return inSightRange && !hasLineOfSightCollisions && (freeLOS || inFov);
        }

        /// <summary>
        /// Something solid is between the eyes and the target. Like the engine's sight rays (ZenGin
        /// zTRACERAY_POLY_IGNORE_TRANSP), alpha tested polygons - leaves, bushes, grates - let sight through. The two
        /// NPCs' own bodies don't count.
        /// </summary>
        private bool IsSightBlocked(Vector3 from, Vector3 to, Transform selfRoot, Transform otherRoot)
        {
            var direction = to - from;
            var distance = direction.magnitude;
            if (distance < 0.001f)
                return false;

            direction /= distance;
            var count = Physics.RaycastNonAlloc(from, direction, _sightHits, distance, _raycastLayersToUse,
                QueryTriggerInteraction.Ignore);

            // The hits come in no order: with a full buffer, a wall may be among the ones left out.
            if (count == _sightHits.Length)
                return true;

            for (var i = 0; i < count; i++)
            {
                var hit = _sightHits[i];
                var hitTransform = hit.collider.transform;
                if (hitTransform.IsChildOf(selfRoot) || hitTransform.IsChildOf(otherRoot))
                    continue;

                if (IsSolidAlong(hit, direction, distance))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The collider blocks the line: where the line meets it, or behind see-through polygons further in, it's solid.
        /// </summary>
        private bool IsSolidAlong(RaycastHit hit, Vector3 direction, float lineLength)
        {
            var collider = hit.collider;
            var travelled = hit.distance;
            for (var step = 0; step < _maxSeeThroughSteps; step++)
            {
                if (!IsSeeThrough(collider, hit.triangleIndex))
                    return true;

                travelled += _seeThroughStep;
                var remaining = lineLength - travelled;
                var ray = new Ray(hit.point + direction * _seeThroughStep, direction);
                if (remaining <= 0f || !collider.Raycast(ray, out hit, remaining))
                    return false;

                travelled += hit.distance;
            }

            return true;
        }

        /// <summary>
        /// The polygon hit belongs to an alpha tested material (render queue AlphaTest, Lit/AlphaToCoverage).
        /// </summary>
        private bool IsSeeThrough(Collider collider, int triangleIndex)
        {
            if (triangleIndex < 0 || collider is not MeshCollider { sharedMesh: { } mesh })
                return false;

            var materials = GetSightMaterials(collider, mesh);
            if (materials == null)
                return false;

            var index = triangleIndex * 3;
            for (var subMesh = 0; subMesh < mesh.subMeshCount && subMesh < materials.Length; subMesh++)
            {
                var descriptor = mesh.GetSubMesh(subMesh);
                if (index >= descriptor.indexStart && index < descriptor.indexStart + descriptor.indexCount)
                    return IsSeeThrough(materials[subMesh]);
            }

            return false;
        }

        /// <summary>
        /// The materials the collider's renderer draws on its mesh, null if it draws another one. Cached: looking them
        /// up allocates, and sight lines run for many NPCs.
        /// </summary>
        private Material[] GetSightMaterials(Collider collider, Mesh mesh)
        {
            if (_sightMaterials.TryGetValue(collider, out var materials))
                return materials;

            var meshFilter = collider.GetComponent<MeshFilter>();
            var meshRenderer = collider.GetComponent<MeshRenderer>();
            materials = meshFilter != null && meshRenderer != null && meshFilter.sharedMesh == mesh
                ? meshRenderer.sharedMaterials
                : null;
            _sightMaterials[collider] = materials;
            return materials;
        }

        private bool IsSeeThrough(Material material)
        {
            if (material == null)
                return false;

            if (!_seeThroughMaterials.TryGetValue(material, out var isSeeThrough))
            {
                var queue = material.renderQueue;
                isSeeThrough = queue >= (int)RenderQueue.AlphaTest && queue < (int)RenderQueue.Transparent;
                _seeThroughMaterials[material] = isSeeThrough;
            }

            return isSeeThrough;
        }

        /// <summary>
        /// Range set via Perc_SetRange() in meters. Unset perceptions are unlimited (senses range still applies).
        /// </summary>
        public float GetPerceptionRange(VmGothicEnums.PerceptionType type)
        {
            return PerceptionRanges.TryGetValue(type, out var range) ? range : float.MaxValue;
        }
    }
}
