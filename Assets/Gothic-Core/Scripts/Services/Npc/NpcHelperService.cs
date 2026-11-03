using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Creator;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
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
                // Engine (OpenGothic wld_detectnpcex): never the dead - a berzerk guard picked a corpse every frame.
                .Where(i => i.Props.BodyState != VmGothicEnums.BodyState.BsDead)
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

            // Diagnostics (berzerk picks a victim without the player): rare call, logged.
            if (!detectPlayer)
                Logger.Log($"[Wld_DetectNpcEx] {npcInstance.GetName(NpcNameSlot.Slot0)} (no player, state {aiState}, " +
                           $"guild {guild}, range {sensesRangeMeters:F0} m): " +
                           $"{foundNpc?.Instance.GetName(NpcNameSlot.Slot0) ?? "nobody"}", LogCat.Ai);

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
        /// The world item behind 'item' (e.g. for AI_GotoItem/Npc_GetDistToItem after Wld_DetectItem): its own container,
        /// or the one this NPC detected last if it is that item.
        /// </summary>
        public VobContainer GetWorldItemContainer(NpcInstance npc, ItemInstance item)
        {
            if (item == null)
                return null;
            if (item.UserData is VobContainer own && own.Go != null)
                return own;
            var detected = npc?.GetUserData()?.Props?.DetectedItem;
            if (detected?.Go == null)
                return null;

            return detected.GetItemInstance()?.Index == item.Index ? detected : null;
        }

        public bool ExtWldDetectItem(NpcInstance npc, int flags)
        {
            var npcGo = npc.GetUserData()?.Go;
            if (npcGo == null) return false;

            var npcPos = npcGo.transform.position;
            const float searchRadius = 20f;

            ItemInstance nearestData = null;
            VobContainer nearestContainer = null;
            var nearestDist = float.MaxValue;

            // Items in the hero's hands are left out with the VR inventory PR (held items are marked there). Not by a
            // kinematic rigidbody: in VR every item lying still is kinematic.
            foreach (var container in _multiTypeCacheService.VobCache)
            {
                if (container.Vob is not IItem vobItem) continue;
                // Skip items whose GO was destroyed (player already picked them up via loot menu) or that are hidden
                // (inside a closed VR chest or backpack).
                if (container.Go == null || !container.Go.activeInHierarchy) continue;

                // Use actual GO position (physics may have moved it)
                var dist = Vector3.Distance(container.Go.transform.position, npcPos);
                if (dist > searchRadius || dist >= nearestDist) continue;

                if (string.IsNullOrEmpty(vobItem.Instance) && string.IsNullOrEmpty(vobItem.Name)) continue;
                var itemData = container.GetItemInstance();
                if (itemData == null) continue;
                if (flags != 0 && (itemData.MainFlag & flags) == 0) continue;

                nearestDist = dist;
                nearestData = itemData;
                nearestContainer = container;
            }

            if (nearestData == null)
            {
                Logger.Log($"[Wld_DetectItem] {npc.GetName(NpcNameSlot.Slot0)}: no item with flags={flags} nearby", LogCat.Npc);
                return false;
            }

            _gameStateService.GothicVm.GlobalItem = nearestData;
            npc.GetUserData().Props.DetectedItem = nearestContainer;
            Logger.Log($"[Wld_DetectItem] {npc.GetName(NpcNameSlot.Slot0)} found '{nearestData.Name}' flags={flags} dist={nearestDist:F1}m", LogCat.Npc);
            return true;
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

        /// <summary>
        /// The temporary attitude (Npc_SetTempAttitude) when set, else the permanent one (GetPermanentAttitude).
        /// </summary>
        public VmGothicEnums.Attitude GetPersonAttitude(NpcContainer self, NpcContainer other)
        {
            if (IsHero(self) || IsHero(other))
            {
                var npc = IsHero(self) ? other : self;
                if (npc.Vob.AttitudeTemp != (int)VmGothicEnums.Attitude.Null)
                    return (VmGothicEnums.Attitude)npc.Vob.AttitudeTemp;
            }

            return GetPermanentAttitude(self, other);
        }

        /// <summary>
        /// Like the engine (OpenGothic personAttitude), an NPC's own attitude (Npc_SetAttitude/Npc_SetTempAttitude) is
        /// its attitude towards the hero - between two NPCs only the guild table counts. Baal Parvez (friendly to the
        /// hero as a guide) was friendly to every lurker and scavenger too and never attacked them.
        /// Npc_GetPermAttitude: the permanent attitude only, never the temporary one (ZS_Attack_End's hostile check);
        /// none set (Null, most G1 NPCs): the guild table.
        /// </summary>
        public VmGothicEnums.Attitude GetPermanentAttitude(NpcContainer self, NpcContainer other)
        {
            if (!IsHero(self) && !IsHero(other))
                return GetGuildAttitude(self.Vob.Guild, other.Vob.Guild);

            var npc = IsHero(self) ? other : self;
            return npc.Vob.Attitude != (int)VmGothicEnums.Attitude.Null
                ? (VmGothicEnums.Attitude)npc.Vob.Attitude
                : GetGuildAttitude(self.Vob.Guild, other.Vob.Guild);
        }

        private VmGothicEnums.Attitude GetGuildAttitude(int selfGuild, int otherGuild)
        {
            return (VmGothicEnums.Attitude)_gameStateService.GuildAttitudes[selfGuild * _gameStateService.GuildCount + otherGuild];
        }

        private static bool IsHero(NpcContainer npc)
        {
            return npc.PrefabProps != null && npc.PrefabProps.IsHero();
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
            // An NPC that isn't in the world (culled, not spawned yet) senses nothing and can't be sensed.
            var selfGo = self?.GetUserData()?.Go;
            var otherGo = other?.GetUserData()?.Go;
            if (selfGo == null || otherGo == null)
                return false;

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

            // Like the engine: sight in the full senses range (in front, or freeLOS), hearing only noise (walking,
            // running) within half the range, smell only close by. Hearing/smell sensed the hero all around in the full
            // range before (orcs: 20 m), also standing still behind them.
            // Guides/followers keep smelling all around the senses range - ZS_GuidePC sets SENSE_SMELL with
            // HAI_DIST_ASSESS_MONSTER so B_AssessEnemy spots monsters on the way ("another one of these beasts"); with the
            // quiet senses Baal Parvez only reacted once bitten.
            if (!IsInEscortState(self))
                return CanSenseNpcQuietly(self, other, senses, freeLOS, range, senseRangeMeters);

            if ((senses & (VmGothicEnums.NpcSenses.Hear | VmGothicEnums.NpcSenses.Smell)) != 0)
            {
                // Hearing/smell don't require facing the source (ignore FOV), but sound and scent
                // still don't travel through solid geometry — reuse the sight raycast for that.
                return CanSeeNpc(self, other, true);
            }

            return CanSeeNpc(self, other, freeLOS);
        }

        /// <summary>
        /// Knocked out (BS_UNCONSCIOUS), like the scripts' C_NpcIsDown (ZS_Unconscious, ZS_MagicSleep).
        /// </summary>
        public static bool IsKnockedOut(NpcContainer npc)
        {
            return npc?.Props != null && npc.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious;
        }

        /// <summary>
        /// Dead or knocked out.
        /// </summary>
        public static bool IsDown(NpcContainer npc)
        {
            return npc?.Props != null && (npc.Props.BodyState == VmGothicEnums.BodyState.BsDead || IsKnockedOut(npc));
        }

        /// <summary>
        /// Guide/follow states (G1 ZS_GuidePC/ZS_FollowPC, G2 ZS_Guide_Player/ZS_Follow_Player).
        /// </summary>
        public static bool IsInEscortState(NpcInstance npc)
        {
            var state = npc?.GetUserData()?.Vob?.CurrentStateName;
            return state != null && (state.StartsWith("ZS_GUIDE", StringComparison.OrdinalIgnoreCase) ||
                                     state.StartsWith("ZS_FOLLOW", StringComparison.OrdinalIgnoreCase));
        }

        private bool CanSenseNpcQuietly(NpcInstance self, NpcInstance other, VmGothicEnums.NpcSenses senses,
            bool freeLOS, float range, float senseRangeMeters)
        {
            if ((senses & VmGothicEnums.NpcSenses.See) != 0 && CanSeeNpc(self, other, freeLOS))
                return true;

            var isAround = (senses & VmGothicEnums.NpcSenses.Smell) != 0 && range <= _smellRangeMeters;
            if (!isAround && (senses & VmGothicEnums.NpcSenses.Hear) != 0 &&
                range <= senseRangeMeters * _hearRangeFactor)
            {
                var bodyState = other.GetUserData()?.Props.BodyState;
                isAround = bodyState is VmGothicEnums.BodyState.BsWalk or VmGothicEnums.BodyState.BsRun
                    or VmGothicEnums.BodyState.BsJump;
            }

            // Sound and scent don't go through walls either - the sight raycast without FOV.
            return isAround && CanSeeNpc(self, other, true);
        }

        private const float _hearRangeFactor = 0.5f;
        private const float _smellRangeMeters = 3f;

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
