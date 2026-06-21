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
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;

        
        // Supporter class where the whole Init() logic is outsourced for better readability.
        private readonly NpcInitializerDomain _initializerDomain = new NpcInitializerDomain().Inject();


        private Queue<NpcLoader> _objectsToInitQueue = new();
        private Queue<NpcContainer> _objectToReEnableQueue = new();

        private DaedalusVm _vm => _gameStateService.GothicVm;

        private const float _fpLookupDistance = 7f; // meter

        
        public void Init()
        {
            _unityMonoService.StartCoroutine(InitNpcCoroutine());
            _unityMonoService.StartCoroutine(ReEnableNpcCoroutine());
            
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

                    _initializerDomain.InitNpc(npcElement.Npc, npcElement.gameObject);
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

        public async Task CreateWorldNpcs(LoadingService loading)
        {
            MobRoutines.ClearAndReleaseMemory();
            MobRoutines = new();

            var npcInit = _configService.Dev.EnableSaveLoadSystem ? _saveGameService.PendingNpcInit : null;
            if (_saveGameService.IsNewGame || npcInit == null)
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

            // Initialize NPC and set its data from SaveGame (VOB entry).
            _initializerDomain.InitNpcVobSaveGame(vobNpc);
        }

        public void ExtWldInsertNpc(int npcInstanceIndex, string spawnPoint)
        {
            _initializerDomain.ExtWldInsertNpc(npcInstanceIndex, spawnPoint);
        }

        public void SpawnNpcRuntime(int npcIndex, Vector3 position, Quaternion rotation)
        {
            _initializerDomain.SpawnNpcRuntime(npcIndex, position, rotation);
        }

        public bool SpawnNpcByName(string symbolName, Vector3 position, Quaternion rotation)
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByName(symbolName);
            if (symbol == null)
            {
                Logger.LogWarning($"[NpcService] SpawnNpcByName: symbol '{symbolName}' not found", LogCat.Npc);
                return false;
            }
            _initializerDomain.SpawnNpcRuntime(symbol.Index, position, rotation);
            return true;
        }

        // FIXME - I think they are overwritten when an NPC is loaded from a SaveGame, as we Initialize them again...
        public void ExtNpcSetTalentValue(NpcInstance npc, VmGothicEnums.Talent talent, int level)
        {
            var vob = npc.GetUserData()!.Vob;

            vob.SetTalent((int)talent, new Talent
            {
                Type =  (int)talent,
                Skill = 0,
                Value = level
            });
        }
        
        // FIXME - In OpenGothic it adds MDS overlays based on skill level.
        public void ExtNpcSetTalentSkill(NpcInstance npc, VmGothicEnums.Talent talent, int skillValue)
        {
            var vob = npc.GetUserData()!.Vob;

            vob.SetTalent((int)talent, new Talent
            {
                Type =  (int)talent,
                Skill = skillValue,
                Value = 0
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
                props.EquippedItems.Add(_vmCacheService.TryGetItemData(data.Armor));
                props.MdmName = armorData.VisualChange;
            }
            else
            {
                props.MdmName = data.Body;
            }
        }

        public NpcInstance ExtHlpGetNpc(int instanceId)
        {
            return _multiTypeCacheService.NpcCache
                .FirstOrDefault(i => i.Instance.Index == instanceId)?
                .Instance;
        }

        public void ExtNpcChangeAttribute(NpcInstance npc, int attributeId, int value)
        {
            var vob = npc.GetUserData().Vob;
            var newValue = vob.GetAttribute(attributeId) + value;

            // Clamp paired attributes (HP ↔ HP_MAX, Mana ↔ Mana_MAX): max is at attributeId+1 by G1 convention.
            if (attributeId % 2 == 0 && attributeId + 1 < vob.Attributes.Count)
            {
                var max = vob.GetAttribute(attributeId + 1);
                if (max > 0)
                    newValue = Mathf.Clamp(newValue, 0, max);
            }

            vob.SetAttribute(attributeId, newValue);
            // Keep the NpcInstance in sync so CopyFromInstanceData (called in InitZkInstance) doesn't
            // overwrite this value with the prototype default when the NPC mesh is lazy-loaded later.
            npc.SetAttribute((NpcAttribute)attributeId, newValue);
        }

        public NpcContainer GetHeroContainer()
        {
            return ((NpcInstance)_gameStateService.GothicVm.GlobalHero).GetUserData();
        }

        public void SyncHeroInstanceToVob()
        {
            var hero = GetHeroContainer();
            hero.Vob.Level = hero.Instance.Level;
            hero.Vob.Xp = hero.Instance.Exp;
            hero.Vob.XpNextLevel = hero.Instance.ExpNext;
            hero.Vob.Lp = hero.Instance.Lp;
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
            // FIXME - Set this value on actual GameObject later.
            npc.GetUserData().Vob.ModelScale = scale;
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
            npc.GetUserData()!.Vob.FightMode = (int)VmGothicEnums.WeaponState.W1H;
            npc.GetUserData().Props.CurrentItem = itemIndex;
            
            // Also add item into Inventory (G1; e.g., Gobbo NailMace).
            // TODO - Check if it's the same in G2.
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
            npcContainer.Props.Dialogs = _gameStateService.Dialogs.Instances
                .Where(dialog => dialog.Npc == npcIndex)
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

            _ambientInfoByPrefix ??= BuildAmbientInfoPrefixLookup();

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
            }
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
