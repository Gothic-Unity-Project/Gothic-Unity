using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Creator;
using Gothic.Core.Domain.Npc;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Extensions;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Manage all NPC related calls a(Ext* engine calls and e.g. load Npcs at WorldSceneManager time)
    /// </summary>
    public class NpcService
    {
        public Dictionary<string, List<(int hour, int minute, int status)>> MobRoutines = new();

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly NpcRoutineService _npcRoutineService;
        [Inject] private readonly Gothic.Core.Services.Culling.NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;

        
        // Supporter class where the whole Init() logic is outsourced for better readability.
        private readonly NpcInitializerDomain _initializerDomain = new NpcInitializerDomain().Inject();


        private Queue<NpcLoader> _objectsToInitQueue = new();
        private Queue<NpcContainer> _objectToReEnableQueue = new();
        // NPCs to reset after the hero slept - culled ones when they show up again (DeveloperConfig.EnableTimeSkipResetsNpcs).
        private readonly HashSet<NpcContainer> _timeSkipResets = new();

        private DaedalusVm _vm => _gameStateService.GothicVm;

        private const float _fpLookupDistance = 7f; // meter

        
        public void Init()
        {
            _unityMonoService.StartCoroutine(InitNpcCoroutine());
            _unityMonoService.StartCoroutine(ReEnableNpcCoroutine());
            GlobalEventDispatcher.GameTimeSkipped.AddListener(ResetNpcsAfterTimeSkip);
            
            GlobalEventDispatcher.LoadGameStart.AddListener(() =>
            {
                ClearQueues();
            });
            
            GlobalEventDispatcher.NpcMeshCullingChanged.AddListener(EventNpcMeshCullingChanged);
            GlobalEventDispatcher.CreateNpc.AddListener(CreateVobNpc);
        }

        public void ClearQueues()
        {
            _objectsToInitQueue.Clear();
            _objectToReEnableQueue.Clear();
        }

        private IEnumerator InitNpcCoroutine()
        {
            while (true)
            {
                if (_objectsToInitQueue.IsEmpty())
                {
                    yield return null;
                }
                else
                {
                    var npcElement = _objectsToInitQueue.Dequeue();
                    var npcId = npcElement.Npc.Id;
                    var monsterId = npcElement.Npc.GetAiVar(_vmService.AIVMMRealId);

                    // Do not load NPCs we don't want to have via Debug flags.
                    if (npcId != 0 && _configService.Dev.SpawnNpcInstances.Value.Any() &&
                        !_configService.Dev.SpawnNpcInstances.Value.Contains(npcElement.Npc.Id))
                    {
                        continue;
                    }

                    // Do not load Monsters we don't want to have via Debug flags.
                    if (npcId == 0 && monsterId != 0 && _configService.Dev.SpawnMonsterInstances.Value.Any() &&
                        !_configService.Dev.SpawnMonsterInstances.Value.Contains(
                            (DeveloperConfigEnums.MonsterId)monsterId))
                    {
                        continue;
                    }

                    // One broken NPC (e.g. a monster instance without a visual, spawned by a transformation scroll) must
                    // not kill this coroutine - no NPC would be built anymore afterwards (summons stayed invisible).
                    try
                    {
                        _initializerDomain.InitNpc(npcElement.Npc, npcElement.gameObject);
                    }
                    catch (Exception e)
                    {
                        Logger.LogError($"[NpcService] Can't build NPC {npcElement.gameObject.name}: {e}", LogCat.Npc);
                    }
                }

                yield return _frameSkipperService.TrySkipToNextFrameCoroutine();
            }
        }

        /// <summary>
        /// Reset one-by-one. Otherwise NPCs will spawn over another if starting from the same WayPoint.
        /// </summary>
        private IEnumerator ReEnableNpcCoroutine()
        {
            while (true)
            {
                if (_objectToReEnableQueue.IsEmpty())
                {
                    yield return null;
                }
                else
                {
                    var npcData = _objectToReEnableQueue.Dequeue();
                    // If we walked to an NPC in our game, the NPC will be re-enabled and Routines get reset.
                    if (_timeSkipResets.Remove(npcData))
                        npcData.PrefabProps?.AiHandler?.ResetAfterTimeSkip();
                    else
                        npcData.PrefabProps?.AiHandler?.ReEnableNpc();
                    yield return _frameSkipperService.TrySkipToNextFrameCoroutine();
                }
            }
        }

        public void SetRootGo(GameObject rootGo)
        {
            _initializerDomain.RootGo = rootGo;
        }

        public Vector3 GetFreeAreaAtSpawnPoint(Vector3 positionToScan)
        {
            return _initializerDomain.GetFreeAreaAtSpawnPoint(positionToScan);
        }

        /// <summary>
        /// Spawns the NPC instance with the given Daedalus symbol name during gameplay.
        /// </summary>
        public bool SpawnNpcByName(string symbolName, Vector3 position, Quaternion rotation)
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName(symbolName);
            if (symbol == null || symbol.Type != DaedalusDataType.Instance)
            {
                Logger.LogWarning($"[NpcService] SpawnNpcByName: '{symbolName}' is no Daedalus instance", LogCat.Npc);
                return false;
            }

            _initializerDomain.SpawnNpcRuntime(symbol.Index, position, rotation);
            return true;
        }

        public async Task CreateWorldNpcs(LoadingService loading)
        {
            MobRoutines.ClearAndReleaseMemory();
            MobRoutines = new();

            var npcInit = _configService.Dev.EnableSaveLoadSystem ? _saveGameService.PendingNpcInit : null;
            // DeveloperConfig.EnableWorldRevisitState: a world visited before uses its stored state - also in a new game.
            if ((_saveGameService.IsNewGame && !_saveGameService.IsRevisitingWorld) || npcInit == null)
            {
                if (_saveGameService.IsWorldEnteredFirstTime)
                    await _initializerDomain.InitNpcsNewGame(loading);
                else
                    await _initializerDomain.InitNpcsSaveGame(loading);
            }
            else
            {
                await _initializerDomain.InitNpcsFromMergedSnapshots(loading, npcInit, _saveGameService.PendingNpcRestore);
            }
        }

        /// <summary>
        /// World Vobs from a SaveGame contains NPCs if they're close to our hero during save time.
        /// We will create them here as a "normal" lazy loaded NPC.
        /// </summary>
        public void CreateVobNpc(INpc vobNpc)
        {
            if (vobNpc.Name.EqualsIgnoreCase(Constants.DaedalusHeroInstanceName))
            {
                _playerService.HeroSpawnPosition = vobNpc.Position.ToUnityVector();
                _playerService.HeroSpawnRotation = vobNpc.Rotation.ToUnityQuaternion();
                return;
            }

            // InitNpcsFromMergedSnapshots is the primary NPC path when loading a saved game.
            // Creating a duplicate container from the WORLD.SAV NPC VOB causes it to run ZS_* routines
            // and lock FreePoints before the real container starts, so GoToNextFp picks the wrong FP.
            if (_saveGameService.PendingNpcInit != null)
                return;

            // Initialize NPC and set its data from SaveGame (VOB entry).
            _initializerDomain.InitNpcVobSaveGame(vobNpc);
        }

        public void ExtWldInsertNpc(int npcInstanceIndex, string spawnPoint)
        {
            _initializerDomain.ExtWldInsertNpc(npcInstanceIndex, spawnPoint);
        }

        public GameObject SpawnNpcRuntime(int npcIndex, Vector3 position, Quaternion rotation, NpcInstance summonedBy = null)
        {
            return _initializerDomain.SpawnNpcRuntime(npcIndex, position, rotation, summonedBy);
        }

        // FIXME - I think they are overwritten when an NPC is loaded from a SaveGame, as we Initialize them again...
        public void ExtNpcSetTalentValue(NpcInstance npc, VmGothicEnums.Talent talent, int level)
        {
            var vob = npc.GetUserData()!.Vob;
            var existing = vob.GetTalent((int)talent);
            vob.SetTalent((int)talent, new Talent
            {
                Type  = (int)talent,
                Skill = existing?.Skill ?? 0,
                Value = level
            });
        }

        // FIXME - In OpenGothic it adds MDS overlays based on skill level.
        public void ExtNpcSetTalentSkill(NpcInstance npc, VmGothicEnums.Talent talent, int skillValue)
        {
            var vob = npc.GetUserData()!.Vob;
            var existing = vob.GetTalent((int)talent);
            vob.SetTalent((int)talent, new Talent
            {
                Type  = (int)talent,
                Skill = skillValue,
                Value = existing?.Value ?? 0
            });
        }

        public void ExtMdlSetVisual(NpcInstance npc, string visual)
        {
            var props = npc.GetUserData().Props;
            props.MdsNameBase = visual;
        }

        public void ExtSetVisualBody(ExtSetVisualBodyData data)
        {
            var props = data.Npc.GetUserData().Props;

            props.BodyData = data;

            if (data.Armor >= 0)
            {
                var armorData = _vmCacheService.TryGetItemData(data.Armor);

                // Mdl_SetVisualBody(..., armorInstance) equips the armor in the engine - incl. its protection.
                // Most NPCs (and test heroes like PC_Rockefeller) get their armor this way, not via EquipItem().
                // Same armor again (re-dressing): the slot conflict unequips + re-equips it, so nothing is doubled.
                // Like the engine: an armor that isn't an item leaves the NPC in its body - a null armor threw here and
                // stopped the whole world's NPC creation (startup scripts run inside Wld_InsertNpc).
                if (armorData == null)
                {
                    Logger.LogWarning($"[Mdl_SetVisualBody] {data.Npc.GetName(NpcNameSlot.Slot0)}: armor " +
                                      $"'{_gameStateService.GothicVm.GetSymbolByIndex(data.Armor)?.Name}' ({data.Armor}) " +
                                      "isn't an item - no armor.", LogCat.Npc);
                    props.MdmName = data.Body;
                    return;
                }

                if (_configService.Dev.EnableScriptEquipEffects)
                    _npcInventoryService.EquipItemWithEffects(data.Npc, armorData, deferItemFunctions: true);
                else
                    props.EquippedItems.Add(armorData);

                props.MdmName = armorData.VisualChange;
            }
            else
            {
                props.MdmName = data.Body;
            }
        }

        /// <summary>
        /// Wld_RemoveNpc: every NPC of this instance leaves the world - out of the NPC cache (Hlp_GetNpc then finds
        /// nothing, like in the engine), out of the culling, GameObject destroyed. G2's wisp amulet removes its wisp
        /// on unequip and before spawning a new one - wisps piled up without it.
        /// </summary>
        public void ExtWldRemoveNpc(int instanceId)
        {
            var removed = _multiTypeCacheService.NpcCache.Where(i => i.Instance.Index == instanceId).ToList();
            foreach (var npc in removed)
            {
                _npcMeshCullingService.RemoveCullingEntry(npc.Instance);
                _multiTypeCacheService.NpcCache.Remove(npc);
                _timeSkipResets.Remove(npc);
                if (npc.Go != null)
                    Object.Destroy(npc.Go);
            }

            Logger.Log($"[Wld_RemoveNpc] instance={instanceId} - removed {removed.Count} NPC(s)", LogCat.Npc);
        }

        public NpcInstance ExtHlpGetNpc(int instanceId)
        {
            var npc = _multiTypeCacheService.NpcCache
                .FirstOrDefault(i => i.Instance.Index == instanceId)?
                .Instance;
            if (npc != null || !_configService.Dev.EnableHlpGetNpcVariables)
                return npc;

            // DeveloperConfig.EnableHlpGetNpcVariables: the engine resolves any instance symbol, also a variable like
            // SELF/OTHER - Mroczne Tajemnice's ZS_DragonEscape_Loop calls B_KillNpc(self) and then inserts the dragon's
            // human form (NON_2013_ALGIRION); a null NPC broke the function before the insert and the dragon just vanished.
            var symbol = _gameStateService.GothicVm.GetSymbolByIndex(instanceId);
            if (symbol is not { Type: DaedalusDataType.Instance } || symbol.GetInstance() is not NpcInstance variableNpc)
                return null;

            Logger.Log($"[NpcService] Hlp_GetNpc({symbol.Name}): the NPC the variable points to", LogCat.Npc);
            return variableNpc;
        }

        public void ExtNpcChangeAttribute(NpcInstance npc, int attributeId, int value)
        {
            // A script passing a missing NPC (Hlp_GetNpc found none) mustn't break the calling function.
            if (npc?.GetUserData()?.Vob == null)
            {
                Logger.LogWarning($"[NpcService] Npc_ChangeAttribute({attributeId}, {value}) without an NPC - skipped",
                    LogCat.Npc);
                return;
            }

            var container = npc.GetUserData();
            var vob = container.Vob;
            var newValue = vob.GetAttribute(attributeId) + value;

            // Clamp paired attributes (HP/HP_MAX, Mana/Mana_MAX): max is at attributeId+1 by G1 convention.
            // Only those two! Strength (4) is followed by Dexterity (5), which isn't its max - clamping it there made
            // e.g. an amulet's +10 strength do nothing while the -10 on unequip still applied.
            var isPairedWithMax = attributeId == (int)NpcAttribute.HitPoints || attributeId == (int)NpcAttribute.Mana;
            if (isPairedWithMax && attributeId + 1 < vob.Attributes.Count)
            {
                var max = vob.GetAttribute(attributeId + 1);
                if (max > 0)
                    newValue = Mathf.Clamp(newValue, 0, max);
            }

            SetAttribute(container, (NpcAttribute)attributeId, newValue);

            // DeveloperConfig.EnableScriptHitPointsDeath: like the engine a script taking the last hit points kills
            // (G1 summons lose 1 HP every few seconds in ZS_MM_SummonedByPC_Loop - at 0 they stood on).
            if (_configService.Dev.EnableScriptHitPointsDeath && attributeId == (int)NpcAttribute.HitPoints &&
                value < 0 && newValue <= 0)
                GlobalEventDispatcher.ScriptHitPointsDepleted.Invoke(container);
        }

        /// <summary>
        /// The engine keeps these values in one place (oCNpc), we have two: scripts read the instance, C# the vob.
        /// Every C# write goes through these setters, so both stay equal.
        /// </summary>
        public void SetAttribute(NpcContainer npc, NpcAttribute attribute, int value)
        {
            npc.Vob.SetAttribute((int)attribute, value);
            npc.Instance.SetAttribute(attribute, value);
            GlobalEventDispatcher.NpcAttributeChanged.Invoke(npc, attribute);
        }

        public void SetLevel(NpcContainer npc, int level)
        {
            npc.Vob.Level = level;
            npc.Instance.Level = level;
        }

        public void SetLp(NpcContainer npc, int lp)
        {
            npc.Vob.Lp = lp;
            npc.Instance.Lp = lp;
        }

        public NpcContainer GetHeroContainer()
        {
            // Hero doesn't exist yet in menus / during boot (e.g. the player's health bar asks for it there).
            return (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
        }

        /// <summary>
        /// Daedalus writes hero values directly on the instance (hero.lp, hero.attribute[] at teachers, permanent
        /// potions). Copies them to the vob - safe because C# writes both stores (SetAttribute/SetLevel/SetLp).
        /// </summary>
        public void SyncHeroInstanceToVob()
        {
            var hero = GetHeroContainer();
            if (hero == null)
                return;

            hero.Vob.Level = hero.Instance.Level;
            hero.Vob.Xp = hero.Instance.Exp;
            hero.Vob.XpNextLevel = hero.Instance.ExpNext;
            hero.Vob.Lp = hero.Instance.Lp;
            for (var i = 0; i < Enum.GetNames(typeof(NpcAttribute)).Length; i++)
                hero.Vob.SetAttribute(i, hero.Instance.GetAttribute((NpcAttribute)i));

            GlobalEventDispatcher.NpcAttributeChanged.Invoke(hero, NpcAttribute.HitPoints);
            GlobalEventDispatcher.NpcAttributeChanged.Invoke(hero, NpcAttribute.HitPointsMax);
        }

        public GameObject GetHeroGameObject()
        {
            return ((NpcInstance)_gameStateService.GothicVm.GlobalHero).GetUserData().Go;
        }

        /// <summary>
        /// We need to first Alloc() hero data space and put the instance to the cache.
        /// Then we initialize it. (During Init, PC_HERO:Npc_Default->Prototype:Npc_Default will call SetTalentValue where we need the lookup to fetch the NpcInstance).
        ///
        /// This method will get called every time we spawn into another world. We therefore need to check if initialize the first time or we only need to set the lookup cache.
        /// </summary>
        public void CacheHero()
        {
            if (_gameStateService.GothicVm.GlobalHero != null)
            {
                // We assume that this call is only made when the cache got cleared before as we loaded another world.
                // Therefore, we re-add it now.
                var heroContainer = ((NpcInstance)_gameStateService.GothicVm.GlobalHero).GetUserData();
                _multiTypeCacheService.NpcCache.Add(heroContainer);

                return;
            }

            // Initial setup
            var playerGo = GameObject.FindWithTag(Constants.PlayerTag);

            // Flat player
            if (playerGo == null)
            {
                playerGo = GameObject.FindWithTag(Constants.MainCameraTag);
            }

            var heroInstance = _gameStateService.GothicVm.AllocInstance<NpcInstance>(_configService.GothicMod.Player);
            var heroDaedalusInstance = _gameStateService.GothicVm.GetSymbolByName(_configService.GothicMod.Player)!;

            var vobNpc = new NpcProxy(heroDaedalusInstance.Index)
            {
                Player = true
            };

            var npcData = new NpcContainer
            {
                Instance = heroInstance,
                Vob = vobNpc,
                Go = playerGo,
                Props = new(),
                // We need to set it now, as the normal "init" logic of the Awake function in this Comp won't work.
                PrefabProps = playerGo.GetComponentInChildren<NpcPrefabProperties>()
            };

            npcData.PrefabProps.Head = Camera.main!.transform;
            npcData.PrefabProps.NpcSubtitles = playerGo.GetComponentInChildren<INpcSubtitles>(true);

            heroInstance.UserData = npcData;

            _playerService.SetHero(npcData);
            _multiTypeCacheService.NpcCache.Add(npcData);
            _vm.InitInstance(heroInstance);
            vobNpc.CopyFromInstanceData(heroInstance);
            // ZenKit initialises FightMode to 6 (its None enum), which we would otherwise
            // misinterpret as WeaponState.Mage. Hero in VR never uses the AI draw-weapon flow,
            // so force NoWeapon (0) so C_NpcIsInFightMode and AssessFighter checks are correct.
            vobNpc.FightMode = (int)VmGothicEnums.WeaponState.NoWeapon;

            if (_configService.Dev.PlayerInventoryAddition.NotNullOrEmpty())
            {
                var items = _vobService.UnpackItems(_configService.Dev.PlayerInventoryAddition);
                foreach (var item in items)
                {
                    _playerService.AddItem(item.Name, item.Amount);
                }
            }

            _vm.GlobalHero = heroInstance;
        }

        public void ExtMdlSetModelScale(NpcInstance npc, System.Numerics.Vector3 scale)
        {
            var container = npc.GetUserData();
            container.Vob.ModelScale = scale;
            _initializerDomain.ApplyModelScale(container);
        }

        public void ExtSetModelFatness(NpcInstance npc, float fatness)
        {
            // FIXME - Set this value on actual GameObject later.
            npc.GetUserData().Vob.ModelFatness = fatness;
        }

        public void ExtApplyOverlayMds(NpcInstance npc, string overlayName)
        {
            var props = npc.GetUserData().Props;
            props.MdsNameOverlay = overlayName;
            props.MdsNameRoutineOverlay = overlayName;
        }

        public void ExtNpcSetToFistMode(NpcInstance npc)
        {
            var npcProperties = npc.GetUserData().Props;

            npc.GetUserData()!.Vob.FightMode = (int)VmGothicEnums.WeaponState.Fist;

            // if npc has item in hand remove it and set weapon to fist
            // Some animations need to force remove items, some not.
            if (npcProperties.UsedItemSlot.IsNullOrEmpty())
                return;

            var slotGo = npc.GetUserData().Go.FindChildRecursively(npcProperties.UsedItemSlot);
            var item = slotGo!.transform.GetChild(0);

            Object.Destroy(item.gameObject);
        }
        
        /// <summary>
        /// Method is called, when a NPCInstance from Daedalus is initialized.
        /// As it's before any mesh is created, we do not spawn the item now.
        /// </summary>
        public void ExtNpcSetToFightMode(NpcInstance npc, int itemIndex)
        {
            var item = _vmCacheService.TryGetItemData(itemIndex);
            var container = npc.GetUserData();
            // VR hero never draws weapons via Gothic's fight-mode system — physical VR hands handle combat.
            // Only update FightMode for non-hero NPCs so Daedalus fmode stays 0 for the hero.
            if (container?.PrefabProps?.IsHero() != true)
                container!.Vob.FightMode = (int)VmGothicEnums.WeaponState.W1H;
            container!.Props.CurrentItem = itemIndex;
            _npcInventoryService.ExtCreateInvItems(npc, item.Index, 1);
        }

        public void ExtTaMin(NpcInstance npc, int startH, int startM, int stopH, int stopM, int action, string waypoint)
        {
            var props = npc.GetUserData().Props;

            RoutineData routine = new()
            {
                StartH = startH,
                StartM = startM,
                NormalizedStart = startH % 24 * 60 + startM,
                StopH = stopH,
                StopM = stopM,
                NormalizedEnd = stopH % 24 * 60 + stopM,
                Action = action,
                Waypoint = waypoint
            };

            props.Routines.Add(routine);
        }

        public bool InitNpc(GameObject go, bool initImmediately = false)
        {
            go.TryGetComponent(out NpcLoader loaderComp);

            if (loaderComp == null || loaderComp.IsLoaded)
            {
                return false;
            }

            // Do not put element into queue a second time.
            loaderComp.IsLoaded = true;

            if (initImmediately)
            {
                _initializerDomain.InitNpc(loaderComp.Npc, loaderComp.gameObject);
            }
            else
            {
                _objectsToInitQueue.Enqueue(loaderComp);
            }

            return true;
        }
        
        public void ReEnableNpc(NpcContainer npcData)
        {
            _objectToReEnableQueue.Enqueue(npcData);
        }

        /// <summary>
        /// DeveloperConfig.EnableTimeSkipResetsNpcs: after sleeping only the time jumped - NPCs kept chasing the hero,
        /// stood where they were and kept their aggro. Now every living NPC starts over like after culling: routines are
        /// recalculated for the new time, visible NPCs reset one per frame (no spawning into each other), culled ones
        /// when they show up again. Dead and knocked out NPCs stay as they are.
        /// </summary>
        private void ResetNpcsAfterTimeSkip()
        {
            if (!_configService.Dev.EnableTimeSkipResetsNpcs)
                return;

            _npcRoutineService.RecalculateAllNpcRoutines();

            var visibleCount = 0;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc?.Props == null || npc.Vob == null || npc.Vob.Player)
                    continue;
                if (npc.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious)
                    continue;
                if (!_timeSkipResets.Add(npc))
                    continue;

                if (npc.Go != null && npc.Go.activeInHierarchy)
                {
                    _objectToReEnableQueue.Enqueue(npc);
                    visibleCount++;
                }
            }

            Logger.Log($"[TimeSkipReset] time skipped - resetting {visibleCount} visible NPC(s), " +
                       $"{_timeSkipResets.Count - visibleCount} culled ones when they show up", LogCat.Ai);
        }

        public string ExtNpcGetNextWp(NpcInstance npc)
        {
            var pos = npc.GetUserData().Go.transform.position;

            return _wayNetService.FindNearestWayPoint(pos, true)?.Name ?? "";
        }

        public bool ExtWldIsFpAvailable(NpcInstance npc, string fpNamePart)
        {
            var props = npc.GetUserData().Props;
            var npcGo = npc.GetUserData().Go;
            var freePoints =
                _wayNetService.FindFreePointsWithName(npcGo.transform.position, fpNamePart, _fpLookupDistance);

            foreach (var fp in freePoints)
            {
                // Kind of: If we're already standing on a FreePoint, then there is one available.
                if (props.CurrentFreePoint == fp)
                {
                    return true;
                }

                // Alternatively, we found a free one within range.
                if (!fp.IsLocked)
                {
                    return true;
                }
            }

            return false;
        }

        public string ExtGetNearestWayPoint(NpcInstance npc)
        {
            var pos = npc.GetUserData().Go.transform.position;

            return _wayNetService.FindNearestWayPoint(pos)?.Name ?? "";
        }

        public bool ExtIsNextFpAvailable(NpcInstance npc, string fpNamePart)
        {
            var props = npc.GetUserData().Props;
            var pos = npc.GetUserData().Go.transform.position;
            var fp = _wayNetService.FindNearestFreePoint(pos, fpNamePart, null);

            if (fp == null)
            {
                return false;
            }
            // Ignore if we're already on this FP.

            if (fp == props.CurrentFreePoint)
            {
                return false;
            }

            if (fp.IsLocked)
            {
                return false;
            }

            return true;
        }

        public void SetDialogs(NpcContainer npcContainer)
        {
            var npcIndex = npcContainer.Instance.Index;
            // A mod's hero can be another instance (Mroczne Tajemnice: PC_Rockefeller) - the mob dialogs (bed, shrine)
            // are still C_INFOs with npc = PC_Hero.
            var heroAliasIndex = npcIndex == (_vm.GlobalHero as NpcInstance)?.Index
                ? _vm.GetSymbolByName("PC_HERO")?.Index ?? npcIndex
                : npcIndex;
            npcContainer.Props.Dialogs = _gameStateService.Dialogs.Instances
                .Where(dialog => dialog.Npc == npcIndex || dialog.Npc == heroAliasIndex)
                .OrderByDescending(dialog => dialog.Important)
                .ToList();

            if (!npcContainer.Props.Dialogs.Any())
                TryAssignAmbientDialogs(npcContainer);
        }

        // ZenKit bug: Daedalus member assignment like `Info_Grd_6_EXIT.npc = X` inside
        // function bodies fails with "C_INFO.NPC without an instance set". B_AssignAmbientInfos
        // relies on this to link ambient C_INFO instances to NPCs at runtime. We replicate
        // the assignment here by matching symbol name patterns (INFO_<GUILD>_<VOICE>_*)
        // and setting InfoInstance.Npc directly via the ZenKitCS setter.
        private void TryAssignAmbientDialogs(NpcContainer npcContainer)
        {
            var guildAbbr = GetAmbientGuildAbbreviation(npcContainer.Instance.Guild);
            if (guildAbbr == null)
                return;

            // Rebuilt when the dialogs were initialized anew (new game VM, DeveloperConfig.EnableDialogsPerGameVm).
            var firstInfo = _gameStateService.Dialogs.Instances.FirstOrDefault();
            if (_ambientInfoByPrefix == null || !ReferenceEquals(firstInfo, _ambientInfoSourceFirst))
            {
                _ambientInfoByPrefix = BuildAmbientInfoPrefixLookup();
                _ambientInfoSourceFirst = firstInfo;
            }

            var voice = npcContainer.Instance.Voice;
            if (!_ambientInfoByPrefix.TryGetValue($"INFO_{guildAbbr}_{voice}", out var ambientDialogs) &&
                !_ambientInfoByPrefix.TryGetValue($"INFO_MINE_{guildAbbr}_{voice}", out ambientDialogs))
                return;

            var npcIndex = npcContainer.Instance.Index;
            foreach (var dialog in ambientDialogs)
                dialog.Npc = npcIndex;

            npcContainer.Props.Dialogs = ambientDialogs.OrderByDescending(d => d.Important).ToList();
        }

        // Cached lookup: "INFO_GRD_6" → list of ambient C_INFO instances with that guild+voice prefix.
        // Built lazily from symbol names to avoid repeated GetSymbolByIndex calls per frame.
        private Dictionary<string, List<InfoInstance>> _ambientInfoByPrefix;
        private InfoInstance _ambientInfoSourceFirst;

        private Dictionary<string, List<InfoInstance>> BuildAmbientInfoPrefixLookup()
        {
            var lookup = new Dictionary<string, List<InfoInstance>>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in _gameStateService.Dialogs.Instances)
            {
                var name = _vm.GetSymbolByIndex(info.Index)?.Name;
                if (name == null) continue;

                var parts = name.Split('_');
                if (parts.Length < 4 || !parts[0].Equals("INFO", StringComparison.OrdinalIgnoreCase))
                    continue;

                string key;
                if (parts[1].Equals("MINE", StringComparison.OrdinalIgnoreCase) && parts.Length >= 5 && int.TryParse(parts[3], out var mineVoice) && mineVoice <= 15)
                    key = $"INFO_MINE_{parts[2]}_{parts[3]}";
                else if (int.TryParse(parts[2], out var campVoice) && campVoice <= 15)
                    key = $"INFO_{parts[1]}_{parts[2]}";
                else
                    continue;

                if (!lookup.TryGetValue(key, out var list))
                {
                    list = new List<InfoInstance>();
                    lookup[key] = list;
                }
                list.Add(info);
            }
            return lookup;
        }

        private static string GetAmbientGuildAbbreviation(int guild) => guild switch
        {
            (int)VmGothicEnums.Guild.GIL_GRD => "GRD",
            (int)VmGothicEnums.Guild.GIL_STT => "STT",
            (int)VmGothicEnums.Guild.GIL_VLK => "VLK",
            (int)VmGothicEnums.Guild.GIL_SLD => "SLD",
            (int)VmGothicEnums.Guild.GIL_ORG => "ORG",
            (int)VmGothicEnums.Guild.GIL_BAU => "BAU",
            (int)VmGothicEnums.Guild.GIL_SFB => "SFB",
            (int)VmGothicEnums.Guild.GIL_NOV => "NOV",
            (int)VmGothicEnums.Guild.GIL_TPL => "TPL",
            _ => null
        };

        private void EventNpcMeshCullingChanged(NpcContainer npcContainer, NpcLoader npcLoader, bool isInVisibleRange, bool wasOutOfDistance)
        {
            // Alter position tracking of NPC
            if (isInVisibleRange)
            {
                var initializedNow = InitNpc(npcLoader.gameObject);

                // If the NPC !wasOutOfDistance (==wasInDistanceAlready), then we spawned our VRPlayer next to the NPC
                // (e.g. from a save game) and we need to go on with the current routine instead of "resetting" the routine.
                // (Which would respawn NPC at a waypoint, which is wrong.)
                if (wasOutOfDistance && !initializedNow)
                {
                    ReEnableNpc(npcContainer);
                }

                // "-12 HP" numbers over the head (our settings menu). Not for the hero - you don't see your own head.
                if (!npcContainer.Vob.Player && npcLoader.GetComponent<NpcHitNumbers>() == null)
                    npcLoader.gameObject.AddComponent<NpcHitNumbers>().Init(npcLoader, _configService);
            }
        }
        
        /// <summary>
        /// An item the NPC holds from AI_UseItemToState (food, a torch, ...) is put away: its mesh in the slot goes,
        /// the item state resets.
        /// </summary>
        public void RemoveUsedItemFromHand(NpcContainer npcContainer)
        {
            var props = npcContainer?.Props;
            if (props == null || string.IsNullOrEmpty(props.UsedItemSlot))
                return;

            var slotGo = npcContainer.PrefabProps?.Bip01 != null
                ? npcContainer.PrefabProps.Bip01.gameObject.FindChildRecursively(props.UsedItemSlot)
                : null;
            if (slotGo != null)
            {
                for (var i = slotGo.transform.childCount - 1; i >= 0; i--)
                    Object.Destroy(slotGo.transform.GetChild(i).gameObject);
            }

            Logger.Log($"[NpcService] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)} puts away the item in " +
                       $"'{props.UsedItemSlot}'", LogCat.Npc);
            props.UsedItemSlot = string.Empty;
            props.ItemAnimationState = -1;
        }

        public void InsertItem(NpcContainer npcContainer, string slot1, string slot2 = "")
        {
            if (slot2.Any())
            {
                throw new Exception("Slot 2 is set but not yet handled by InsertItem as AnimationEvent.");
            }

            var slotGo = npcContainer.PrefabProps.Bip01.gameObject.FindChildRecursively(slot1);

             _vobService.CreateItemMesh(npcContainer.Props.CurrentItem, slotGo);

            npcContainer.Props.UsedItemSlot = slot1;
        }
    }
}
