using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Creator;
using Gothic.Core.Domain.Culling;
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
        [Inject] private readonly EquipmentService _equipmentService;
        [Inject] private readonly NpcRoutineService _npcRoutineService;
        [Inject] private readonly MobRoutineService _mobRoutineService;
        [Inject] private readonly NpcLookService _npcLookService;
        [Inject] private readonly AnimationService _animationService;

        
        // Supporter class where the whole Init() logic is outsourced for better readability.
        private readonly NpcInitializerDomain _initializerDomain = new NpcInitializerDomain().Inject();
        private NpcDeathDomain _deathDomain;
        private NpcDeathDomain DeathDomain => _deathDomain ??= new NpcDeathDomain(SyncHeroInstanceToVob).Inject();


        private readonly NearestFirstQueue<NpcLoader> _objectsToInitQueue = new();
        private Queue<NpcContainer> _objectToReEnableQueue = new();
        // NPCs to put back on their routine after the time jumped - culled ones once they show up again.
        private readonly HashSet<NpcContainer> _routineResets = new();

        private DaedalusVm _vm => _gameStateService.GothicVm;

        private const float _fpLookupDistance = 7f; // meter

        
        public void Init()
        {
            _unityMonoService.StartCoroutine(InitNpcCoroutine());
            _unityMonoService.StartCoroutine(ReEnableNpcCoroutine());
            
            GlobalEventDispatcher.LoadGameStart.AddListener(() =>
            {
                ClearQueues();
                // The new game's VM has its own dialogs (BootstrapDomain).
                _ambientInfoByPrefix = null;
            });
            
            GlobalEventDispatcher.NpcMeshCullingChanged.AddListener(EventNpcMeshCullingChanged);
            GlobalEventDispatcher.CreateNpc.AddListener(CreateVobNpc);
        }

        public void ClearQueues()
        {
            _objectsToInitQueue.Clear();
            _objectToReEnableQueue.Clear();
            _routineResets.Clear();
        }

        /// <summary>
        /// Whether an NPC within radius of center still waits for its lazy initialization.
        /// </summary>
        public bool HasQueuedNpcWithin(Vector3 center, float radius)
        {
            return _objectsToInitQueue.HasEntryWithin(center, radius);
        }

        /// <summary>
        /// Only NPCs within radius of center are initialized until ClearInitFocus(), e.g. while the view is black
        /// after loading.
        /// </summary>
        public void SetInitFocus(Vector3 center, float radius)
        {
            _objectsToInitQueue.SetFocus(center, radius);
        }

        public void ClearInitFocus()
        {
            _objectsToInitQueue.ClearFocus();
        }

        /// <summary>
        /// Initializes queued NPCs nearest to the camera first, until this frame's share is used up.
        /// </summary>
        private IEnumerator InitNpcCoroutine()
        {
            while (true)
            {
                if (_objectsToInitQueue.IsFocused)
                {
                    // The black wait after loading: sorted around the focus center, the camera may not be there yet.
                    _objectsToInitQueue.UpdateOrder(_objectsToInitQueue.FocusCenter);
                }
                else if (_objectsToInitQueue.Count > 0)
                {
                    var camera = Camera.main;
                    if (camera != null)
                        _objectsToInitQueue.UpdateOrder(camera.transform.position);
                }

                while (_objectsToInitQueue.TryDequeue(out var npcElement))
                {
                    // Destroyed in the meantime (e.g. world change).
                    if (npcElement == null)
                        continue;

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

                    try
                    {
                        _initializerDomain.InitNpc(npcElement.Npc, npcElement.gameObject);
                    }
                    catch (Exception e)
                    {
                        Logger.LogError($"Failed to init NPC {npcElement.name}: {e}", LogCat.Npc);
                    }

                    if (_frameSkipperService.IsFrameBudgetUsed)
                        break;
                }

                yield return null;
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
                    if (_routineResets.Remove(npcData))
                        npcData.PrefabProps?.AiHandler?.ResetToRoutine();
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
            _mobRoutineService.ClearRoutines();
            
            // STARTUP_* runs only the first time a world is entered (new game or a world not inside the save yet).
            // Otherwise, the NPCs are restored from the save game / the state stored when we left the world.
            if (_saveGameService.IsWorldEnteredFirstTime)
                await _initializerDomain.InitNpcsNewGame(loading);
            else
                await _initializerDomain.InitNpcsSaveGame(loading);
        }

        /// <summary>
        /// World Vobs from a SaveGame contains NPCs if they're close to our hero during save time.
        /// We will create them here as a "normal" lazy loaded NPC.
        /// </summary>
        public void CreateVobNpc(INpc vobNpc)
        {
            // Mods can use another hero instance than PC_HERO (Gothic.ini/mod ini: player=).
            if (vobNpc.Player || vobNpc.NpcInstance.EqualsIgnoreCase(_configService.GothicMod.Player))
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

            // An armor that isn't an item (a mod's broken instance) is worn as no armor - it threw and stopped the
            // creation of every NPC after this one (found on BoroBongo's branch).
            var armorData = data.Armor >= 0 ? _vmCacheService.TryGetItemData(data.Armor) : null;
            if (data.Armor >= 0 && armorData == null)
                Logger.LogWarning($"[NpcService] Mdl_SetVisualBody: armor {data.Armor} of " +
                                  $"{data.Npc.GetName(NpcNameSlot.Slot0)} is no item - no armor", LogCat.Npc);

            if (armorData != null)
            {
                // Like the engine (OpenGothic mdl_setvisualbody): the armor is worn like with EquipItem - it protects.
                _equipmentService.EquipByScript(data.Npc.GetUserData(), armorData);
                props.MdmName = armorData.VisualChange;
            }
            else
            {
                props.MdmName = data.Body;
            }
        }

        public NpcInstance ExtHlpGetNpc(int instanceId)
        {
            var npc = _multiTypeCacheService.NpcCache
                .FirstOrDefault(i => i.Instance.Index == instanceId)?
                .Instance;
            if (npc != null)
                return npc;

            // Like the engine, a variable (SELF, OTHER, ...) gives the NPC it points to: Hlp_GetNpc(self) in
            // B_KillNpc(self) was null (found on BoroBongo's branch).
            var symbol = _gameStateService.GothicVm.GetSymbolByIndex(instanceId);
            var variableNpc = symbol is { Type: DaedalusDataType.Instance } ? symbol.GetInstance() as NpcInstance : null;
            return variableNpc?.GetUserData()?.Vob != null ? variableNpc : null;
        }

        public void ExtNpcChangeAttribute(NpcInstance npc, int attributeId, int value)
        {
            // A script passing no NPC (Hlp_GetNpc found none) mustn't break the calling function.
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

            if (attribute == NpcAttribute.HitPoints && value <= 0 && !npc.Props.IsDead)
                DeathDomain.Die(npc);
        }

        /// <summary>
        /// The NPC dies now, killed by the killer (a fight). The scripts' part follows (ZS_Dead, NpcDeathDomain).
        /// </summary>
        public void Kill(NpcContainer npc, NpcContainer killer)
        {
            if (!npc.Props.IsDead)
                DeathDomain.Die(npc, killer);
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
            // Wonderbranch merge: #447 copies more than #465's early-out compares (guild, protections, missions) -
            // without the early-out until both are in main.
            if (hero == null)
                return;

            hero.Vob.CopyStatsFromInstance(hero.Instance);

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

            // A loaded save game brings its own hero (attributes, inventory, talents, ...).
            var vobNpc = _saveGameService.GetSavedHero() ?? new NpcProxy(heroDaedalusInstance.Index)
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
            if (vobNpc.IsRestored)
            {
                _saveGameService.InitNpcInstanceFromSave(npcData, () => _vm.InitInstance(heroInstance));
            }
            else
            {
                _vm.InitInstance(heroInstance);
                vobNpc.CopyFromInstanceData(heroInstance);
            }

            if (!vobNpc.IsRestored && _configService.Dev.PlayerInventoryAddition.NotNullOrEmpty())
            {
                var items = _configService.Dev.PlayerInventoryAddition.UnpackItems();
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
            var container = npc.GetUserData();
            if (container == null)
            {
                Logger.LogWarning($"[NpcService] Mdl_SetModelFatness: NPC {npc.Index} has no container - " +
                    $"fatness {fatness} ignored.", LogCat.Npc);
                return;
            }

            _npcLookService.SetFatness(container, fatness);
        }

        /// <summary>
        /// Like the engine, the NPC keeps the scripts' overlays in a list (saved with it), the newest one plays. A
        /// drawn weapon keeps its fight stance until it's put away (UndrawWeapon).
        /// </summary>
        public void ExtApplyOverlayMds(NpcInstance npc, string overlayName)
        {
            var container = npc.GetUserData();
            if (container.Vob.Overlays.FindIndex(i => i.EqualsIgnoreCase(overlayName)) < 0)
                container.Vob.AddOverlay(overlayName);

            if ((VmGothicEnums.WeaponState)container.Vob.FightMode == VmGothicEnums.WeaponState.NoWeapon)
                container.Props.MdsNameOverlay = overlayName;
        }

        public void ExtRemoveOverlayMds(NpcInstance npc, string overlayName)
        {
            var container = npc.GetUserData();
            var index = container.Vob.Overlays.FindIndex(i => i.EqualsIgnoreCase(overlayName));
            if (index < 0)
                return;

            container.Vob.RemoveOverlay(index);
            if ((VmGothicEnums.WeaponState)container.Vob.FightMode == VmGothicEnums.WeaponState.NoWeapon)
                container.Props.MdsNameOverlay = container.GetScriptOverlay();
        }

        /// <summary>
        /// Mdl_ApplyOverlayMDSTimed, like the engine (OpenGothic Npc::addOverlay with a time): the overlay for that long,
        /// then the one before is back. Used by G1's haste potions and G2's speed potion (HUMANS_SPRINT.MDS). The VR hero
        /// has no animations: he walks and runs as much faster as the overlay's walk and run (HeroSpeedChanged).
        /// </summary>
        public void ExtApplyOverlayMdsTimed(NpcInstance npc, string overlayName, int milliseconds)
        {
            var container = npc?.GetUserData();
            var seconds = milliseconds / 1000f;
            if (container?.Props == null || string.IsNullOrEmpty(overlayName) || seconds <= 0f)
                return;

            if (container.PrefabProps?.IsHero() == true)
            {
                var walkFactor = GetOverlaySpeedFactor(container, overlayName, "S_WALKL");
                var runFactor = GetOverlaySpeedFactor(container, overlayName, "S_RUNL");
                Logger.Log($"[NpcService] Hero: {overlayName} for {seconds:F0} s - walk x{walkFactor:F2}, " +
                           $"run x{runFactor:F2}", LogCat.Npc);
                GlobalEventDispatcher.HeroSpeedChanged.Invoke(walkFactor, runFactor, seconds);
                return;
            }

            var previous = container.Props.MdsNameOverlay;
            container.Props.MdsNameOverlay = overlayName;
            _unityMonoService.StartCoroutine(RemoveOverlayLater(container, overlayName, previous, seconds));
        }

        private static IEnumerator RemoveOverlayLater(NpcContainer npc, string overlayName, string previous,
            float seconds)
        {
            yield return new WaitForSeconds(seconds);
            // Another overlay came meanwhile (drawn weapon, routine): it stays.
            if (npc.Props != null && npc.Props.MdsNameOverlay == overlayName)
                npc.Props.MdsNameOverlay = previous;
        }

        /// <summary>
        /// How much faster the animation's root motion is with the overlay than without. 1 if it can't be told.
        /// </summary>
        private float GetOverlaySpeedFactor(NpcContainer npc, string overlayName, string animationName)
        {
            var normal = _animationService.GetTrack(animationName, npc.Props.MdsNameBase, null);
            var overlay = _animationService.GetTrack(animationName, npc.Props.MdsNameBase, overlayName);
            if (normal == null || overlay == null || normal.MovementSpeed.z <= 0f || overlay.MovementSpeed.z <= 0f)
            {
                Logger.LogWarning($"[NpcService] No forward root motion of {animationName} in " +
                                  $"{npc.Props.MdsNameBase} with and without {overlayName} - speed unchanged.",
                    LogCat.Npc);
                return 1f;
            }

            return overlay.MovementSpeed.z / normal.MovementSpeed.z;
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
                _objectsToInitQueue.Enqueue(loaderComp, loaderComp.transform.position);
            }

            return true;
        }
        
        public void ReEnableNpc(NpcContainer npcData)
        {
            _objectToReEnableQueue.Enqueue(npcData);
        }

        /// <summary>
        /// The time jumped (the hero slept, Wld_SetTime): like the engine (OpenGothic WorldObjects::resetPositionToTA)
        /// every living NPC is put on the routine point of the new time and starts it over (found on BoroBongo's
        /// branch). Before, NPCs stood where they were and kept chasing the hero. One per frame, so they don't spawn
        /// into each other; culled ones when they show up again. The dead and the unconscious stay.
        /// </summary>
        public void ResetNpcsToRoutines()
        {
            _npcRoutineService.RecalculateAllRoutines();

            var heroIndex = (_gameStateService.GothicVm.GlobalHero as NpcInstance)?.Index;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc?.Props == null || npc.Instance == null || npc.Instance.Index == heroIndex)
                    continue;
                if (npc.Props.IsDead || npc.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                    continue;
                if (!_routineResets.Add(npc))
                    continue;

                if (npc.Go != null && npc.Go.activeInHierarchy)
                    _objectToReEnableQueue.Enqueue(npc);
            }

            Logger.Log($"[NpcService] Time jumped - {_routineResets.Count} NPCs go back to their routine", LogCat.Ai);
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

                // Its first initialization starts the routine anyway: a reset after a time jump is done with it.
                if (initializedNow)
                    _routineResets.Remove(npcContainer);

                // If the NPC !wasOutOfDistance (==wasInDistanceAlready), then we spawned our VRPlayer next to the NPC
                // (e.g. from a save game) and we need to go on with the current routine instead of "resetting" the routine.
                // (Which would respawn NPC at a waypoint, which is wrong.)
                if (wasOutOfDistance && !initializedNow)
                {
                    ReEnableNpc(npcContainer);
                }

                // An NPC built before keeps its body while culled, but loses a ghost's see-through look.
                if (!initializedNow)
                    _npcLookService.Apply(npcContainer);
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
