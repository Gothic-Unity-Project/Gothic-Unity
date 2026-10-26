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
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _configService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        private const float _fpLookupDistance = 7f; // meter
        private static readonly int _raycastLayersToUse = 1 << Constants.DefaultLayer;

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

            return container != null;
        }

        public int ExtWldGetMobState(NpcInstance npcInstance, string scheme)
        {
            var npcGo = GetNpc(npcInstance);

            var prefabProps = npcInstance.GetUserData().PrefabProps;

            InteractiveProperties props;

            if (prefabProps.CurrentInteractable != null)
            {
                try
                {
                    // Check current gameobject and children as well
                    props = prefabProps.CurrentInteractable.PropsAs<InteractiveProperties>();
                }
                catch (Exception)
                {
                    Logger.LogError($"Wld_GetMobState() returned an exception for {npcGo.name}", LogCat.Npc);
                    return -1;
                }
            }
            else
                props = _vobService.GetFreeInteractableWithin10M(npcGo.transform.position, scheme)?.PropsAs<InteractiveProperties>();

            if (props == null)
                return -1;

            return Math.Max(0, props.State);
        }

        /// <summary>
        /// The melee weapon the VR hero holds in a hand right now (set by VRWeaponService) - his "readied" weapon for
        /// Npc_GetReadiedWeapon (e.g. orcs tolerate a hero holding the Ulu-Mulu, C_OtherIsToleratedEnemy).
        /// </summary>
        public ItemInstance HeroReadiedMeleeWeapon { get; set; }

        /// <summary>
        /// Melee weapons in the VR hero's holsters/quick slots - his "equipped" weapon for Npc_GetEquippedMeleeWeapon
        /// (orcs also tolerate the Ulu-Mulu at the belt).
        /// </summary>
        public readonly List<ItemInstance> HeroHolsteredMeleeWeapons = new();

        /// <summary>
        /// The hero's equipped melee weapon for scripts that ask which one (Npc_GetEquippedMeleeWeapon): in a hand, else
        /// at the belt. Npc_HasEquippedMeleeWeapon stays false for him (G2 guards would react to every belt weapon).
        /// </summary>
        public ItemInstance GetHeroEquippedMeleeWeapon()
        {
            return HeroReadiedMeleeWeapon ?? HeroHolsteredMeleeWeapons.FirstOrDefault();
        }

        public ItemInstance ExtNpcGetEquippedMeleeWeapon(NpcInstance npc)
        {
            // VR hero never "draws" a melee weapon in the Gothic sense — physical VR hands handle combat.
            // EquippedItems is populated by the startup script but does not reflect drawn state.
            // Returning null prevents G2 guards from triggering ZS_REACTTOWEAPON via this check.
            if (npc.Index == _gameStateService.GothicVm.GlobalHero?.Index)
                return null;

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
            if (npc.Index == _gameStateService.GothicVm.GlobalHero?.Index)
                return null;

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
                .Where(i => !_configService.Dev.EnableDetectNpcSkipsDead ||
                            i.Props.BodyState != VmGothicEnums.BodyState.BsDead)
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

            if (npcGo == null)
            {
                Logger.LogWarning($"ExtNpcGetDistToWp: npcGo is null for npc={npc?.GetName(NpcNameSlot.Slot0)} waypoint={waypointName}", LogCat.Npc);
                return int.MaxValue;
            }

            var waypoint = _wayNetService.GetWayNetPoint(waypointName);

            if (waypoint == null)
            {
                Logger.LogWarning($"ExtNpcGetDistToWp: waypoint '{waypointName}' not found for npc={npc?.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
                return int.MaxValue;
            }

            // *100 as Gothic metrics are in cm, not m.
            return (int)(Vector3.Distance(npcGo.transform.position, waypoint.Position) * 100);
        }

        // The world item Wld_DetectItem found, per NPC - scripts only get its instance data (no position) as 'item'.
        private readonly Dictionary<int, VobContainer> _detectedItems = new();

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
            if (npc == null || !_detectedItems.TryGetValue(npc.Index, out var detected) || detected?.Go == null ||
                detected.Vob is not IItem detectedItem)
                return null;

            var detectedName = !string.IsNullOrEmpty(detectedItem.Instance) ? detectedItem.Instance : detectedItem.Name;
            return _vmCacheService.TryGetItemData(detectedName)?.Index == item.Index ? detected : null;
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

            foreach (var container in _multiTypeCacheService.VobCache)
            {
                if (container.Vob is not IItem vobItem) continue;

                var itemSymName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
                if (string.IsNullOrEmpty(itemSymName)) continue;

                var itemData = _vmCacheService.TryGetItemData(itemSymName);
                if (itemData == null) continue;

                if (flags != 0 && (itemData.MainFlag & flags) == 0) continue;

                // Skip items whose GO was destroyed (player already picked them up via loot menu)
                if (container.Go == null) continue;

                // The player's item - NPCs must not detect (and later AI_TakeItem) it out of their hand.
                if (container.IsHeldByPlayer) continue;

                var rb = container.Go.GetComponent<Rigidbody>();
                if (rb != null && rb.isKinematic) continue;

                // Use actual GO position (physics may have moved it)
                var dist = Vector3.Distance(container.Go.transform.position, npcPos);
                if (dist > searchRadius || dist >= nearestDist) continue;

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
            _detectedItems[npc.Index] = nearestContainer;
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

        public VmGothicEnums.Attitude GetPersonAttitude(NpcContainer self, NpcContainer other)
        {
            // A summon and its master are always friendly to each other — in BOTH directions and above
            // every other rule: the summon's ZS_MM_Summoned sets Npc_SetAttitude(HOSTILE) (it fights
            // everyone on its master's behalf), and the guild attitude table only covers human guilds,
            // so monster-vs-monster falls back to hostile — a SkeletonMage (GIL_DEMON) would otherwise
            // pick its own fresh GIL_SKELETON summons (spawned 2-5m away, closer than any real enemy)
            // as targets, and the summons would then retaliate via the hit-response path. Vanilla gets
            // this peace from engine-internal target filtering we don't replicate; SummonedBy is our
            // equivalent, and every targeting path (GetNextTarget, UpdateEnemyNpc, armed-threat scan,
            // hit-response switch) already respects Friendly.
            if (self.SummonedBy != null && other.Instance != null && self.SummonedBy.Index == other.Instance.Index)
                return VmGothicEnums.Attitude.Friendly;
            if (other.SummonedBy != null && self.Instance != null && other.SummonedBy.Index == self.Instance.Index)
                return VmGothicEnums.Attitude.Friendly;

            // DeveloperConfig.EnableNpcsIgnoreHeroSummons: the hero's summon and an NPC see each other the way that
            // NPC sees the hero - bandits and wolves fight it, Cavalorn or Xardas leave it alone. A deviation: the G2
            // scripts make humans hostile to the GIL_SUMMONED_* guilds (B_InitMonsterAttitudes), so in vanilla
            // guards attack the hero's summons too.
            if (_configService.Dev.EnableNpcsIgnoreHeroSummons)
            {
                if (IsHeroSummon(other) && self.SummonedBy == null && !IsHero(self))
                    return GetAttitudeTowardsHero(self);
                if (IsHeroSummon(self) && other.SummonedBy == null && !IsHero(other))
                    return GetAttitudeTowardsHero(other);
            }

            const int attNull = (int)VmGothicEnums.Attitude.Null;

            // DeveloperConfig.EnablePersonalAttitudeOnlyForHero: like the engine (OpenGothic personAttitude), an NPC's own
            // attitude (Npc_SetAttitude/Npc_SetTempAttitude) is its attitude towards the hero - between two NPCs only
            // the guild table counts. Baal Parvez (friendly to the hero as a guide) was friendly to every lurker and
            // scavenger too and never attacked them.
            if (_configService.Dev.EnablePersonalAttitudeOnlyForHero)
            {
                if (!IsHero(self) && !IsHero(other))
                    return GetGuildAttitude(self.Vob.Guild, other.Vob.Guild);

                var npc = IsHero(self) ? other : self;
                if (npc.Vob.AttitudeTemp != attNull)
                    return (VmGothicEnums.Attitude)npc.Vob.AttitudeTemp;
                if (npc.Vob.Attitude != attNull)
                    return (VmGothicEnums.Attitude)npc.Vob.Attitude;
                return GetGuildAttitude(self.Vob.Guild, other.Vob.Guild);
            }

            // Temp attitude is a hero-specific override (e.g. theft, summoned ally via ZS_MM_SummonedByPC)
            if (other.PrefabProps.IsHero() && self.Vob.AttitudeTemp != attNull)
                return (VmGothicEnums.Attitude)self.Vob.AttitudeTemp;
            // Permanent personal attitude overrides guild when explicitly set via Npc_SetAttitude
            if (self.Vob.Attitude != attNull)
                return (VmGothicEnums.Attitude)self.Vob.Attitude;
            return GetGuildAttitude(self.Vob.Guild, other.Vob.Guild);
        }

        private VmGothicEnums.Attitude GetGuildAttitude(int selfGuild, int otherGuild)
        {
            return (VmGothicEnums.Attitude)_gameStateService.GuildAttitudes[selfGuild * _gameStateService.GuildCount + otherGuild];
        }

        private static bool IsHero(NpcContainer npc)
        {
            return npc.PrefabProps != null && npc.PrefabProps.IsHero();
        }

        private bool IsHeroSummon(NpcContainer npc)
        {
            return npc.SummonedBy != null && _gameStateService.GothicVm?.GlobalHero is NpcInstance hero &&
                   npc.SummonedBy.Index == hero.Index;
        }

        /// <summary>
        /// The NPC's attitude towards the hero, like GetPersonAttitude(npc, hero): temp, else permanent, else guild.
        /// </summary>
        private VmGothicEnums.Attitude GetAttitudeTowardsHero(NpcContainer npc)
        {
            const int attNull = (int)VmGothicEnums.Attitude.Null;
            if (npc.Vob.AttitudeTemp != attNull)
                return (VmGothicEnums.Attitude)npc.Vob.AttitudeTemp;
            if (npc.Vob.Attitude != attNull)
                return (VmGothicEnums.Attitude)npc.Vob.Attitude;
            var hero = (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
            return hero?.Vob != null ? GetGuildAttitude(npc.Vob.Guild, hero.Vob.Guild) : VmGothicEnums.Attitude.Neutral;
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
            var senseRangeMeters = Mathf.Min(self.SensesRange / 100f, maxRangeMeters); // daedalus values are in cm, we need them in m
            var range = Vector3.Distance(other.GetUserData().Go.transform.position,
                self.GetUserData().Go.transform.position);

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

            // DeveloperConfig.EnableQuietSenses: hearing/smell sensed the hero all around within the full senses range
            // (orcs: 20 m) - also standing still behind them. Sight keeps the full range (in front, or freeLOS);
            // hearing needs noise (walking/running) within half the range; smell only works close by.
            // DeveloperConfig.EnableEscortSensesHero: guides/followers keep the engine's senses (smell all around the senses
            // range) - ZS_GuidePC sets SENSE_SMELL with HAI_DIST_ASSESS_MONSTER so B_AssessEnemy spots monsters on the way
            // ("another one of these beasts"); with the quiet senses Baal Parvez only reacted once bitten.
            if (_configService.Dev.EnableQuietSenses && !(_configService.Dev.EnableEscortSensesHero && IsInEscortState(self)))
                return CanSenseNpcQuietly(self, other, senses, freeLOS, range, senseRangeMeters);

            if ((senses & (VmGothicEnums.NpcSenses.Hear | VmGothicEnums.NpcSenses.Smell)) != 0)
            {
                // Hearing/smell don't require facing the source (ignore FOV), but sound and scent
                // still don't travel through solid geometry — reuse the sight raycast for that.
                return CanSeeNpc(self, other, true);
            }

            return CanSeeNpc(self, other, freeLOS);
        }

        private static Gothic.Core.Services.Config.ConfigService _downConfigService;

        /// <summary>
        /// Knocked out (BS_UNCONSCIOUS) - DeveloperConfig.EnableFrozenIsNotDown: not a magically frozen NPC. Like the
        /// scripts (C_NpcIsDown: ZS_Unconscious, ZS_MagicSleep, dead), ZS_MagicFreeze sets BS_UNCONSCIOUS via
        /// AI_PlayAniBS but the victim isn't down: Mroczne Tajemnice's crawlers freeze with their web and then bite.
        /// Before the frozen NPC was invisible to monsters, dropped as a target and killed by the next hit.
        /// </summary>
        public static bool IsKnockedOut(NpcContainer npc)
        {
            if (npc?.Props == null || npc.Props.BodyState != VmGothicEnums.BodyState.BsUnconscious)
                return false;
            _downConfigService ??= ReflexProjectInstaller.DIContainer.Resolve<Gothic.Core.Services.Config.ConfigService>();
            return !_downConfigService.Dev.EnableFrozenIsNotDown || !IsMagicFrozen(npc);
        }

        /// <summary>
        /// Dead or knocked out (a frozen NPC isn't, see IsKnockedOut).
        /// </summary>
        public static bool IsDown(NpcContainer npc)
        {
            return npc?.Props != null && (npc.Props.BodyState == VmGothicEnums.BodyState.BsDead || IsKnockedOut(npc));
        }

        public static bool IsMagicFrozen(NpcContainer npc)
        {
            var state = npc?.Vob?.CurrentStateName;
            return state != null && state.StartsWith("ZS_MAGICFREEZE", StringComparison.OrdinalIgnoreCase);
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
            var selfRealHeadPosition = new Vector3(selfRoot.position.x, selfHead.position.y, selfRoot.position.z);
            var otherRealHeadPosition = new Vector3(otherRoot.position.x, otherHead.position.y, otherRoot.position.z);

            var distanceToNpc = Vector3.Distance(selfRealHeadPosition, otherRealHeadPosition);
            var inSightRange = distanceToNpc <= self.SensesRange / 100f; // SensesRange is in cm.

            var hasLineOfSightCollisions = Physics.Linecast(selfRealHeadPosition, otherRealHeadPosition, _raycastLayersToUse);

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
        /// Range set via Perc_SetRange() in meters. Unset perceptions are unlimited (senses range still applies).
        /// </summary>
        public float GetPerceptionRange(VmGothicEnums.PerceptionType type)
        {
            return PerceptionRanges.TryGetValue(type, out var range) ? range : float.MaxValue;
        }
    }
}
