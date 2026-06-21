using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Vm;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using Mesh = ZenKit.Mesh;
using Texture = ZenKit.Texture;
using TextureFormat = ZenKit.TextureFormat;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Usage:
    ///
    /// Loading:
    /// 1. LoadNewGame() | LoadSavedGame()  -> Initializes the save game state
    /// 2. ChangeWorld(worldName:str)       -> Load the required world. Will be fetched from save or from game data itself.
    ///
    /// Saving:
    /// 1. + 2. Load*() and ChangeWorld()   -> Needs to be called before to fill the data.
    /// 3. SaveGame(saveGameId:int)         -> Will use the currently loaded world from runtime and stores changes.
    ///
    /// Helper methods:
    /// * GetSaveGame(saveGameId:int)       -> Return a save game object (or null) if requested. (e.g. used for LoadMenu to prepare data.
    /// </summary>
    public class SaveGameService
    {
        public SlotId SaveGameId;
        public bool IsNewGame => SaveGameId == SlotId.NewGame;
        public bool IsLoadedGame => !IsNewGame;
        public bool IsWorldEnteredFirstTime;

        public bool IsFirstWorldLoadingFromSaveGame; // Check if we load save game right now!
        
        /// <summary>
        /// Values can be:
        /// - true - When we start a new game and load first world | when we visit another world for the first time
        /// - false - We load a save game and spawn where we left off last time | when we visit another world for a n-th time
        ///
        /// Visiting a world for the first time can be triggered with or without leveraging a save game.
        /// It only matters if it's the first time! (Save games only include world saves if we visited it before.)
        /// </summary>
        public bool IsWorldLoadedForTheFirstTime;

        public SaveGame Save;

        private readonly Dictionary<string, WorldContainer> _worlds = new();
        public string CurrentWorldName;
        public WorldContainer CurrentWorldData => _worlds[CurrentWorldName];


        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameTimeService _gameTimeService;

        // NpcInventoryService intentionally NOT injected — VobService already injects SaveGameService,
        // so injecting NpcInventoryService (which injects VobService) here would create a circular dependency.
        // Resolved lazily at call time instead.
        private NpcInventoryService _npcInventorySvc
            => ReflexProjectInstaller.DIContainer.Resolve<NpcInventoryService>();

        private UnityCustomSave _pendingHeroRestore;
        private SaveGame _pendingSaveRestore;
        private readonly Dictionary<string, NpcSaveEntry> _npcSnapshots = new();
        private Dictionary<string, NpcSaveEntry> _pendingNpcRestore;
        private List<NpcInitEntry> _pendingNpcInit;

        // Items currently loose in the world (taken out of backpack, or loot dropped on ground).
        // Keyed by the ZenKit VOB so PrepareWorldDataForSaving can sync their Unity transforms before writing WORLD.SAV.
        private readonly Dictionary<IVirtualObject, VobContainer> _looseWorldItems = new();

        public Dictionary<string, NpcSaveEntry> PendingNpcRestore => _pendingNpcRestore;
        public List<NpcInitEntry> PendingNpcInit => _pendingNpcInit;

        public void ClearPendingNpcData()
        {
            _pendingNpcRestore = null;
            _pendingNpcInit = null;
        }

        /// <summary>
        /// Clears only the per-world init list (used on world change).
        /// Unlike ClearPendingNpcData, this keeps _pendingNpcRestore alive so dirty NPCs
        /// from previously visited worlds are still restored when we enter those worlds again.
        /// </summary>
        public void ClearPendingNpcInit()
        {
            _pendingNpcInit = null;
        }

        /// <summary>
        /// Call when an item enters the world as a physical object (e.g. taken out of backpack, dropped by NPC).
        /// Registers it so PrepareWorldDataForSaving can sync the Unity transform → ZenKit VOB position before writing WORLD.SAV.
        /// </summary>
        public void TrackLooseItem(VobContainer container)
        {
            if (container?.Vob == null || container.Go == null) return;
            _looseWorldItems[container.Vob] = container;
        }

        /// <summary>
        /// Call when the item leaves the world (put into backpack or inventory).
        /// </summary>
        public void UntrackLooseItem(VobContainer container)
        {
            if (container?.Vob == null) return;
            _looseWorldItems.Remove(container.Vob);
        }

        /// <summary>
        /// Call when a fresh item VOB (created by chest/container spawn, not originally in the world tree)
        /// is dropped into the world and needs to be persisted in WORLD.SAV.
        /// Adds the VOB to the runtime world list and tracks its position for save time.
        /// </summary>
        public void PromoteChestItemToWorld(VobContainer container)
        {
            if (container?.Vob == null || container.Go == null) return;
            if (!_worlds.ContainsKey(CurrentWorldName)) return;
            if (!CurrentWorldData.Vobs.Contains(container.Vob))
                CurrentWorldData.Vobs.Add(container.Vob);
            _looseWorldItems[container.Vob] = container;
        }

        
        public enum SlotId
        {
            WorldChangeOnly = -1,
            NewGame = 0,
            Slot1 = 1,
            Slot2 = 2,
            Slot3 = 3,
            Slot4 = 4,
            Slot5 = 5,
            Slot6 = 6,
            Slot7 = 7,
            Slot8 = 8,
            Slot9 = 9,
            Slot10 = 10,
            Slot11 = 11,
            Slot12 = 12,
            Slot13 = 13,
            Slot14 = 14,
            Slot15 = 15
        }

            
        public void Init()
        {
            if (!_configService.Dev.EnableSaveLoadSystem) return;
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldSceneLoaded);
            GlobalEventDispatcher.NpcMeshCullingChanged.AddListener(OnNpcCullingChanged);
        }

        private void OnWorldSceneLoaded()
        {
            Logger.Log($"OnWorldSceneLoaded: pendingSave={_pendingSaveRestore != null} pendingHero={_pendingHeroRestore != null}", LogCat.Loading);
            if (_pendingSaveRestore != null)
            {
                RestoreDaedalusState(_pendingSaveRestore);
                _pendingSaveRestore = null;
            }
            if (_pendingHeroRestore != null)
            {
                ApplyHeroRestore(_pendingHeroRestore);
                _pendingHeroRestore = null;
            }
        }

        public void LoadNewGame()
        {
            GlobalEventDispatcher.LoadGameStart.Invoke();

            SaveGameId = 0;
            Save = new SaveGame(_contextGameVersionService.Version);
            IsFirstWorldLoadingFromSaveGame = true;
            _worlds.ClearAndReleaseMemory();
            _npcSnapshots.Clear();
            _looseWorldItems.Clear();
            _pendingNpcRestore = null;
            _pendingNpcInit = null;
        }

        /// <summary>
        /// Hint: G1 save game folders start with 1. We leverage the same numbering.
        /// </summary>
        public void LoadSavedGame(SlotId saveGameId)
        {
            GlobalEventDispatcher.LoadGameStart.Invoke();

            LoadSavedGame(saveGameId, GetSaveGame(saveGameId));
        }

        public void LoadSavedGame(SlotId saveGameId, SaveGame save)
        {
            if (save == null)
            {
                Logger.LogError($"SaveGame with id {saveGameId} doesn't exist.", LogCat.Loading);
                return;
            }

            SaveGameId = saveGameId;
            Save = save;
            IsFirstWorldLoadingFromSaveGame = true;
            _worlds.ClearAndReleaseMemory();
            _npcSnapshots.Clear();
            _looseWorldItems.Clear();
            _pendingNpcRestore = null;
            _pendingNpcInit = null;
            _pendingSaveRestore = save;
            LoadUnityCustomData(saveGameId);
        }

        /// <summary>
        /// Loading logic order:
        /// 1. Check if the world is already loaded (cached) for this game session (i.e. we visited it already in this session)
        /// 2. Try to load the world state from the save game
        /// 3. Either use this saved world data or load it from normal .zen file
        /// </summary>
        public void ChangeWorld(string worldName)
        {
            // G2 has for example: AddonWorld\NewWorld.zen --> NewWorld.zen
            // Linux doesn't see \ as a directory separator, Windows sees both \ and /
            CurrentWorldName = Path.GetFileName(worldName.Replace("\\","/"));

            // For world-change during a loaded-game session, ClearPendingNpcInit() wiped _pendingNpcInit.
            // Reload the per-world init snapshot for the destination world so InitNpcsFromMergedSnapshots works.
            // On initial load, LoadUnityCustomData already set _pendingNpcInit — only reload if it's missing.
            if (_configService.Dev.EnableSaveLoadSystem && IsLoadedGame && _pendingNpcInit == null)
            {
                Logger.Log($"ChangeWorld: _pendingNpcInit null — reloading init for '{CurrentWorldName}'", LogCat.Loading);
                TryLoadNpcInitForWorld(CurrentWorldName);
            }

            // 1. World was already loaded.
            if (_worlds.ContainsKey(CurrentWorldName))
            {
                IsWorldLoadedForTheFirstTime = false;
                return;
            }

            IsWorldLoadedForTheFirstTime = true;
            IsWorldEnteredFirstTime = true; // Always true — VOBs always come from .zen, never WORLD.SAV
            ZenKit.World originalWorld = _resourceCacheService.TryGetWorld(CurrentWorldName)!;

            // Always build VOBs from the original .zen.
            // ZenKit's round-tripped WORLD.SAV crashes on reload; all runtime state lives in UNITYSAVE.json.
            // Item/chest/mover persistence via WORLD.SAV is a future TODO.
            // Flatten zCVobLevelCompo children so CurrentWorldData.Vobs.Remove() works correctly
            // when items enter the backpack.
            var worldVobs = new List<IVirtualObject>();
            foreach (var vob in originalWorld.RootObjects)
            {
                if (vob.Type == VirtualObjectType.zCVobLevelCompo)
                {
                    foreach (var child in vob.Children)
                        if (child.Type != VirtualObjectType.oCNpc)
                            worldVobs.Add(child);
                }
                else if (vob.Type != VirtualObjectType.oCNpc)
                    worldVobs.Add(vob);
            }

            _worlds[CurrentWorldName] = new WorldContainer
            {
                OriginalWorld = originalWorld,
                SaveGameWorld = null, // Set lazily in SaveCurrentGame when writing WORLD.SAV

                Mesh = (Mesh)originalWorld.Mesh, // Do not cache or memory consumption will be way too high
                BspTree = (CachedBspTree)originalWorld.BspTree.Cache(),

                // NPC list from .zen is always empty — Daedalus startup scripts handle NPC spawning.
                Npcs = WrapVobs(originalWorld.Npcs),

                Vobs = WrapVobs(worldVobs),
                // Always use original .zen WayNet — WORLD.SAV can have corrupt/shifted waypoint positions
                // that cause NPCs to route to wrong coordinates on load.
                WayNet = (CachedWayNet)originalWorld.WayNet.Cache()
            };
        }

        /// <summary>
        /// Load a Save Game.
        /// 
        /// Hint: If you want to compare an original Gothic save and a Gothic Unity save, use zen2zen and convert a save file
        ///       to ascii for comparison: https://github.com/GothicKit/ZenKit/blob/main/examples/zen2zen.cc
        /// </summary>
        [CanBeNull]
        public SaveGame GetSaveGame(SlotId folderSaveId)
        {
            // Load metadata
            var save = new SaveGame(_contextGameVersionService.Version);
            var saveGamePath = GetSaveGamePath(folderSaveId);

            if (!Directory.Exists(saveGamePath))
            {
                Logger.LogError($"SaveGame inside folder >{saveGamePath}< doesn't exist.", LogCat.Loading);
                return null;
            }

            save.Load(GetSaveGamePath(folderSaveId));

            return save;
        }

        /// <summary>
        /// Saving means:
        /// 1. Collect changed data from all the worlds visited during this gameplay
        /// 2. Alter its values inside the ZenKit data
        /// 3. Save world-by-world into the save game itself
        ///
        /// Hint: Needs to be called after EndOfFrame to ensure we can do a screenshot as thumbnail.
        /// Hint: If you want to compare an original Gothic save and a Gothic Unity save, use zen2zen and convert a save file
        ///       to ascii for comparison: https://github.com/GothicKit/ZenKit/blob/main/examples/zen2zen.cc
        /// 
        /// </summary>
        public void SaveCurrentGame(SlotId saveGameId, string title)
        {
            var saveGame = new SaveGame(_contextGameVersionService.Version);
            saveGame.Metadata.Title = title;
            saveGame.Metadata.SaveDate = DateTime.Now.ToString();
            saveGame.Thumbnail = CreateThumbnail();
            saveGame.Metadata.World = CurrentWorldName.ToUpper();

            FlushDaedalusState(saveGame);

            foreach (var worldData in _worlds)
            {
                var worldContainer = worldData.Value;
                // FIXME - We need to create a new combined world first.

                // World not yet saved
                if (worldContainer.SaveGameWorld == null)
                {
                    // We simply load the world an additional time to have a Pointer to save later.
                    worldContainer.SaveGameWorld = _resourceCacheService.TryGetWorld(worldData.Key);
                }

                PrepareWorldDataForSaving(worldData.Key == CurrentWorldName, worldContainer);
                saveGame.Save(GetSaveGamePath(saveGameId), worldContainer.SaveGameWorld, worldData.Key.TrimEndIgnoreCase(".ZEN").ToUpper());
            }

            SaveUnityCustomData(saveGameId);
        }

        private Texture CreateThumbnail()
        {
            int pixelsPerAxis = 256; // Default size of a G1 Thumbnail
            Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture(ScreenCapture.StereoScreenCaptureMode.BothEyes);
            Texture2D formattedScreenshot;

            // Alter dimensions of screenshots to align with Gothic thumbnail format.
            {
                RenderTexture rt = RenderTexture.GetTemporary(pixelsPerAxis, pixelsPerAxis);
                rt.filterMode = FilterMode.Bilinear;

                RenderTexture.active = rt;
                Graphics.Blit(screenshot, rt);

                formattedScreenshot = new Texture2D(pixelsPerAxis, pixelsPerAxis, UnityEngine.TextureFormat.RGB565, false);
                formattedScreenshot.ReadPixels(new Rect(0, 0, pixelsPerAxis, pixelsPerAxis), 0, 0);
                formattedScreenshot.Apply();

                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);

                // Gothic textures need to be flipped to be shown correctly.
                var originalPixels = formattedScreenshot.GetPixels();
                var yFlippedPixels = new Color[originalPixels.Length];
                for (var row = 0; row < pixelsPerAxis; ++row)
                {
                    // We iterate through every row and reverse the whole row (aka flipping y-axis)
                    Array.Copy(originalPixels, row * pixelsPerAxis, yFlippedPixels, (pixelsPerAxis - row - 1) * pixelsPerAxis, pixelsPerAxis);
                }
                formattedScreenshot.SetPixels(yFlippedPixels);
            }

            TextureBuilder builder = new TextureBuilder(pixelsPerAxis, pixelsPerAxis);

            builder.AddMipmap(formattedScreenshot.GetRawTextureData(), TextureFormat.R5G6B5);

            return builder.Build(TextureFormat.R5G6B5);
        }

        /// <summary>
        /// For VOBs (no NPC magic)
        /// 1. Fetch all VOBs from current world. (either new one or from save game.)
        /// 2. Drop LevelCompo and move all VOBs one level up
        /// 3. The SaveTree is nearly flat (except some sub-elements from special VOBs.)
        /// 4. Save it
        ///
        /// HINT: Whenever G1 saves a game, the VOB tree gets reversed. I.e. you need to save1 + load1 + save2 in G1 to get the same result twice.
        ///       It also means, that the order of VOBs is irrelevant for the game itself.
        /// </summary>
        private void PrepareWorldDataForSaving(bool isCurrentWorld, WorldContainer container)
        {
            List<IVirtualObject> allVobs = new();

            // If the root elements are LevelCompos, then we save a new game.
            // Let's use its children as the levelCompo isn't saved in G1.
            foreach (var vob in container.Vobs)
            {
                if (vob.Type == VirtualObjectType.zCVobLevelCompo)
                    allVobs.AddRange(vob.Children);
                else
                    allVobs.Add(vob);
            }

            // We need to set Hero at the beginning of the list like in a G1 save game.
            if (isCurrentWorld)
            {
                var heroContainer = ((NpcInstance)_gameStateService.GothicVm.GlobalHero).GetUserData()!;
                heroContainer.Vob.Position = heroContainer.Go.transform.position.ToZkVector();
                heroContainer.Vob.Rotation = heroContainer.Go.transform.rotation.ToZkMatrix();
                FlushAiVars(heroContainer);
                FlushTalents(heroContainer);
                allVobs.Add(heroContainer.Vob);
            }

            // Sync Unity transforms → ZenKit VOB positions for items that were dropped in the world.
            // Without this, dropped items save at their original .zen position and teleport back on load.
            // Items held in hand at save time also get their hand position synced (they load frozen/kinematic there).
            foreach (var (vob, looseItem) in _looseWorldItems)
            {
                if (looseItem.Go == null) continue;
                vob.Position = looseItem.Go.transform.position.ToZkVector();
                vob.Rotation = looseItem.Go.transform.rotation.ToZkMatrix();
            }

            _npcMeshCullingService.UpdateVobPositionOfVisibleNpcs();
            var visibleNpcs = _npcMeshCullingService.GetVisibleNpcs();

            foreach (var visibleNpc in visibleNpcs)
            {
                FlushAiVars(visibleNpc);
                FlushTalents(visibleNpc);
                allVobs.Add(visibleNpc.Vob);
            }
            
            container.SaveGameWorld.RootObjects = UnwrapVobs(allVobs);
        }

        /// <summary>
        /// Gothic 1's C++ engine archives exactly 12 talent slots (0–11) per NPC, regardless
        /// of what NPC_TALENT_MAX is set to in mod scripts (DM_E sets it to 16).
        /// If we write 16 talents ZenKit stores numTalents=16 and Gothic reads only 12, leaving
        /// 4 extra talent chunks where the next string field should be → RestoreStringEOL crash.
        /// </summary>
        private static void FlushTalents(NpcContainer npc)
        {
            const int G1NpcTalentMax = 12;
            var before = npc.Vob.TalentCount;
            for (var i = before - 1; i >= G1NpcTalentMax; i--)
                npc.Vob.RemoveTalent(i);
            Logger.Log($"FlushTalents [{npc.Vob.NpcInstance}]: {before} → {npc.Vob.TalentCount}", LogCat.Npc);
        }

        /// <summary>
        /// Gothic always writes exactly 50 AiVars (200 bytes raw) per NPC.
        /// A brand-new NpcProxy has AiVars.Length == 0; writing that would corrupt the
        /// fixed-size scriptVars field in the archive and cause std::bad_alloc on load.
        /// </summary>
        private static void FlushAiVars(NpcContainer npc)
        {
            const int AiVarCount = 50;
            var aiVars = new int[AiVarCount];
            for (var i = 0; i < AiVarCount; i++)
                aiVars[i] = npc.Instance.GetAiVar(i);
            npc.Vob.AiVars = aiVars;
        }

        private void SaveUnityCustomData(SlotId saveGameId)
        {
            var hero = (NpcInstance)_gameStateService.GothicVm.GlobalHero;
            var heroContainer = hero.GetUserData();
            var vob = heroContainer.Vob; // Vob is the runtime truth — attributes are updated here by ExtNpcChangeAttribute

            var heroAttrs = new int[8];
            for (var i = 0; i < 8; i++)
                heroAttrs[i] = vob.GetAttribute(i);

            var heroPos = heroContainer.Go.transform.position;
            var heroRot = heroContainer.Go.transform.rotation;

            // Hero inventory — read all categories from packed storage (runtime truth)
            var heroInvItems = _npcInventorySvc.GetAllInventoryItems(hero);
            var heroInventory = heroInvItems
                .Select(i => new HeroInventoryEntry { Name = i.Name, Amount = i.Amount })
                .ToList();

            var data = new UnityCustomSave
            {
                WorldName = CurrentWorldName,
                HeroPosition = new[] { heroPos.x, heroPos.y, heroPos.z },
                HeroRotation = new[] { heroRot.x, heroRot.y, heroRot.z, heroRot.w },
                HeroAttributes = heroAttrs,
                HeroGuild = heroContainer.Props.TrueGuild != VmGothicEnums.Guild.GIL_NONE
                    ? (int)heroContainer.Props.TrueGuild
                    : vob.Guild,
                HeroLevel = vob.Level,
                HeroXp = vob.Xp,
                HeroExpNext = vob.XpNextLevel,
                HeroLp = vob.Lp,
                HeroInventory = heroInventory,
                GuildAttitudes = _gameStateService.GuildAttitudes != null
                    ? (int[])_gameStateService.GuildAttitudes.Clone()
                    : null
            };

            // Load existing UNITYSAVE.json as base so saves are additive (update/add, never delete)
            var existingPath = GetUnityCustomSavePath(saveGameId);
            var npcEntries = new Dictionary<string, NpcSaveEntry>();
            if (File.Exists(existingPath))
            {
                var existing = JsonConvert.DeserializeObject<UnityCustomSave>(File.ReadAllText(existingPath));
                if (existing?.Npcs != null)
                    foreach (var e in existing.Npcs)
                        npcEntries[e.Key] = e;
            }

            // NPCs that were loaded from a previous save but never became visible this session
            // (e.g. saving to a new slot). Carry them forward so they're not lost.
            if (_pendingNpcRestore != null)
                foreach (var (k, v) in _pendingNpcRestore)
                    npcEntries[k] = v;

            // Merge: culled-out snapshots then currently visible NPCs override previous entries
            foreach (var (k, v) in _npcSnapshots)
                npcEntries[k] = v;
            foreach (var visibleNpc in _npcMeshCullingService.GetVisibleNpcs())
            {
                if (visibleNpc.Vob.Player) continue;
                var key = MakeNpcKey(visibleNpc);
                npcEntries[key] = CreateNpcSnapshot(key, visibleNpc);
            }
            data.Npcs = npcEntries.Values.ToList();

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            var savePath = GetUnityCustomSavePath(saveGameId);
            Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            File.WriteAllText(savePath, json);
            Logger.Log($"SaveUnityCustomData: level={data.HeroLevel} hp={data.HeroAttributes[0]}/{data.HeroAttributes[1]} inv={data.HeroInventory?.Count ?? 0} npcs={data.Npcs.Count} attitudes={data.GuildAttitudes?.Length ?? 0}", LogCat.Loading);
        }

        private void LoadUnityCustomData(SlotId saveGameId)
        {
            var path = GetUnityCustomSavePath(saveGameId);
            if (!File.Exists(path))
            {
                Logger.LogWarning($"LoadUnityCustomData: no UNITYSAVE.json at {path}", LogCat.Loading);
                return;
            }

            var unitySave = JsonConvert.DeserializeObject<UnityCustomSave>(File.ReadAllText(path));
            _pendingHeroRestore = unitySave;

            if (_pendingHeroRestore == null) return;

            _pendingNpcRestore = unitySave?.Npcs?.Count > 0
                ? unitySave.Npcs.ToDictionary(e => e.Key, e => e)
                : null;

            // Load world-specific NPC init baseline.
            // IMPORTANT: CurrentWorldName is NULL here — ChangeWorld() hasn't been called yet.
            // Use unitySave.WorldName (the world where the game was saved) as the lookup key.
            var targetWorld = unitySave?.WorldName ?? "";
            Logger.Log($"LoadUnityCustomData: save world='{targetWorld}', CurrentWorldName='{CurrentWorldName}' (not yet set)", LogCat.Loading);
            TryLoadNpcInitForWorld(targetWorld);

            var playerService = ReflexProjectInstaller.DIContainer.Resolve<PlayerService>();
            playerService.HeroSpawnPosition = new Vector3(
                _pendingHeroRestore.HeroPosition[0],
                _pendingHeroRestore.HeroPosition[1],
                _pendingHeroRestore.HeroPosition[2]);
            playerService.HeroSpawnRotation = new Quaternion(
                _pendingHeroRestore.HeroRotation[0],
                _pendingHeroRestore.HeroRotation[1],
                _pendingHeroRestore.HeroRotation[2],
                _pendingHeroRestore.HeroRotation[3]);

            Logger.Log($"LoadUnityCustomData: spawning at {playerService.HeroSpawnPosition}, npcInit={_pendingNpcInit?.Count ?? 0}, dirty={_pendingNpcRestore?.Count ?? 0}", LogCat.Loading);
        }

        private void TryLoadNpcInitForWorld(string worldName)
        {
            if (string.IsNullOrEmpty(worldName)) return;

            // World-specific file takes priority over the legacy single file
            var worldPath = GetNpcInitPath(worldName);
            var legacyPath = LegacyNpcInitPath;

            string pathToUse = null;
            if (File.Exists(worldPath))
            {
                pathToUse = worldPath;
                Logger.Log($"TryLoadNpcInitForWorld: using world-specific file for '{worldName}'", LogCat.Loading);
            }
            else if (File.Exists(legacyPath))
            {
                var legacy = JsonConvert.DeserializeObject<UnityNpcInit>(File.ReadAllText(legacyPath));
                if (legacy != null && string.Equals(legacy.WorldName, worldName, StringComparison.OrdinalIgnoreCase))
                {
                    pathToUse = legacyPath;
                    Logger.Log($"TryLoadNpcInitForWorld: falling back to legacy UNITYNPCINIT.json for '{worldName}'", LogCat.Loading);
                }
                else
                    Logger.LogWarning($"TryLoadNpcInitForWorld: legacy file world='{legacy?.WorldName}' doesn't match '{worldName}' — no init found", LogCat.Loading);
            }
            else
                Logger.LogWarning($"TryLoadNpcInitForWorld: no init snapshot found for '{worldName}' — NPCs will be re-initialized from Daedalus", LogCat.Loading);

            if (pathToUse == null) return;

            var npcInit = JsonConvert.DeserializeObject<UnityNpcInit>(File.ReadAllText(pathToUse));
            if (npcInit?.Npcs?.Count > 0)
            {
                _pendingNpcInit = npcInit.Npcs;
                Logger.Log($"TryLoadNpcInitForWorld: loaded {_pendingNpcInit.Count} NPC entries for '{worldName}'", LogCat.Loading);
            }
            else
                Logger.LogWarning($"TryLoadNpcInitForWorld: file '{pathToUse}' is empty or invalid", LogCat.Loading);
        }

        private void ApplyHeroRestore(UnityCustomSave data)
        {
            Logger.Log($"ApplyHeroRestore: data level={data.HeroLevel} hp={data.HeroAttributes?[0]}/{data.HeroAttributes?[1]} attrs={data.HeroAttributes?.Length}", LogCat.Loading);

            var hero = (NpcInstance)_gameStateService.GothicVm.GlobalHero;
            if (hero == null)
            {
                Logger.LogError("ApplyHeroRestore: GlobalHero is null", LogCat.Loading);
                return;
            }

            var heroContainer = hero.GetUserData();
            if (heroContainer == null)
            {
                Logger.LogError("ApplyHeroRestore: hero has no container", LogCat.Loading);
                return;
            }

            var vob = heroContainer.Vob;
            Logger.Log($"ApplyHeroRestore: vob BEFORE level={vob.Level} hp={vob.GetAttribute(0)}/{vob.GetAttribute(1)}", LogCat.Loading);

            for (var i = 0; i < data.HeroAttributes.Length && i < 8; i++)
            {
                hero.SetAttribute((NpcAttribute)i, data.HeroAttributes[i]);
                vob.SetAttribute(i, data.HeroAttributes[i]);
            }

            hero.Level = data.HeroLevel; vob.Level = data.HeroLevel;
            hero.Exp = data.HeroXp; vob.Xp = data.HeroXp;
            hero.ExpNext = data.HeroExpNext; vob.XpNextLevel = data.HeroExpNext;
            hero.Lp = data.HeroLp; vob.Lp = data.HeroLp;
            if (data.HeroGuild > 0)
            {
                hero.Guild = data.HeroGuild; vob.Guild = data.HeroGuild; vob.GuildTrue = data.HeroGuild;
                heroContainer.Props.TrueGuild = (VmGothicEnums.Guild)data.HeroGuild;
            }

            // Restore hero inventory: clear Daedalus-assigned defaults then re-add saved items.
            // The backpack reads from GetPacked() at open time, so it will reflect this automatically.
            if (data.HeroInventory != null && data.HeroInventory.Count > 0)
            {
                _npcInventorySvc.ExtNpcClearInventory(hero);
                var restored = 0;
                foreach (var entry in data.HeroInventory)
                {
                    var sym = _gameStateService.GothicVm.GetSymbolByName(entry.Name);
                    if (sym == null)
                    {
                        Logger.LogWarning($"ApplyHeroRestore: item '{entry.Name}' not found in VM, skipping", LogCat.Loading);
                        continue;
                    }
                    _npcInventorySvc.ExtCreateInvItems(hero, sym.Index, entry.Amount);
                    restored++;
                }
                Logger.Log($"ApplyHeroRestore: restored {restored}/{data.HeroInventory.Count} inventory entries", LogCat.Loading);
            }

            // Restore guild attitude matrix so faction changes (e.g. joining a camp) persist.
            if (data.GuildAttitudes != null && _gameStateService.GuildAttitudes != null &&
                data.GuildAttitudes.Length == _gameStateService.GuildAttitudes.Length)
            {
                Array.Copy(data.GuildAttitudes, _gameStateService.GuildAttitudes, data.GuildAttitudes.Length);
                Logger.Log($"ApplyHeroRestore: restored guild attitude matrix ({data.GuildAttitudes.Length} entries)", LogCat.Loading);
            }

            Logger.Log($"ApplyHeroRestore: vob AFTER level={vob.Level} hp={vob.GetAttribute(0)}/{vob.GetAttribute(1)}", LogCat.Loading);
        }

        private void OnNpcCullingChanged(NpcContainer npc, NpcLoader loader, bool isInVisibleRange, bool wasOutOfDistance)
        {
            if (npc.Vob.Player) return;

            var key = MakeNpcKey(npc);

            if (!isInVisibleRange)
            {
                if (npc.Go == null) return;
                _npcSnapshots[key] = CreateNpcSnapshot(key, npc);
            }
            else if (_pendingNpcRestore != null && _pendingNpcRestore.TryGetValue(key, out var entry))
            {
                Logger.Log($"OnNpcCullingChanged: restoring '{key}' dead={entry.IsDead} hp={entry.Attributes?[0]} goSet={npc.Go != null}", LogCat.Loading);
                ApplyNpcSavedState(npc, entry);
                _pendingNpcRestore.Remove(key);
            }
        }

        private static string MakeNpcKey(NpcContainer npc)
        {
            // GoName is set in InitLazyLoadNpc before npc.Go is assigned, so it's stable at
            // restore time (PostWorldCreate fires before InitNpc creates the "Root" GO).
            if (!string.IsNullOrEmpty(npc.GoName)) return npc.GoName;
            if (npc.Go != null)
            {
                var parent = npc.Go.transform.parent;
                return parent != null ? parent.name : npc.Go.name;
            }
            return npc.Vob.NpcInstance;
        }

        private static NpcSaveEntry CreateNpcSnapshot(string key, NpcContainer npc)
        {
            var pos = npc.Go.transform.position;
            var rot = npc.Go.transform.rotation;
            var vob = npc.Vob;
            var attrs = new int[8];
            for (var i = 0; i < 8; i++) attrs[i] = vob.GetAttribute(i);
            var isDead = npc.Props.BodyState == VmGothicEnums.BodyState.BsDead || attrs[0] <= 0;
            // Normalize HP to 0 for dead NPCs — human knockouts leave HP=1 but the NPC is dead.
            if (isDead) attrs[0] = 0;
            return new NpcSaveEntry
            {
                Key = key,
                NpcInstance = vob.NpcInstance,
                Position = new[] { pos.x, pos.y, pos.z },
                Rotation = new[] { rot.x, rot.y, rot.z, rot.w },
                Attributes = attrs,
                CurrentStateName = vob.CurrentStateName ?? "",
                CurrentRoutine = vob.CurrentRoutine ?? "",
                CurrentFreePointName = npc.Props.CurrentFreePoint?.Name,
                IsDead = isDead
            };
        }

        private static void ApplyNpcSavedState(NpcContainer npc, NpcSaveEntry entry)
        {
            var vob = npc.Vob;
            if (entry.Attributes != null)
                for (var i = 0; i < entry.Attributes.Length && i < 8; i++)
                    vob.SetAttribute(i, entry.Attributes[i]);

            if (npc.Go != null && entry.Position != null)
            {
                npc.Go.transform.position = new Vector3(entry.Position[0], entry.Position[1], entry.Position[2]);
                npc.Go.transform.rotation = new Quaternion(entry.Rotation[0], entry.Rotation[1], entry.Rotation[2], entry.Rotation[3]);
            }

            if (entry.IsDead)
            {
                npc.Props.BodyState = VmGothicEnums.BodyState.BsDead;
                // AiHandler.Start() runs before PostWorldCreate fires NpcMeshCullingChanged,
                // so it saw full HP and initialized the NPC alive. Force dead state now.
                npc.Props.AnimationQueue.Clear();
                if (npc.PrefabProps?.AnimationSystem != null)
                {
                    npc.PrefabProps.AnimationSystem.StopAllAnimations();
                    npc.PrefabProps.AnimationSystem.PlayAnimation("S_DEADB");
                }
                Logger.Log($"ApplyNpcSavedState: {entry.NpcInstance} marked dead", LogCat.Npc);
            }

            Logger.Log($"ApplyNpcSavedState: {entry.NpcInstance} hp={vob.GetAttribute(0)}/{vob.GetAttribute(1)} dead={entry.IsDead}", LogCat.Npc);
        }

        private string GetUnityCustomSavePath(SlotId saveGameId)
            => Path.GetFullPath(Path.Join(GetSaveGamePath(saveGameId), "UNITYSAVE.json"));

        // Per-world init file: UNITYNPCINIT_{WORLDNAME}.json (e.g. UNITYNPCINIT_WORLD.json)
        private string GetNpcInitPath(string worldName)
            => Path.GetFullPath(Path.Join(SavesFolderPath, $"UNITYNPCINIT_{worldName.TrimEndIgnoreCase(".ZEN").ToUpper()}.json"));

        // Legacy single-file path — checked as fallback when world-specific file doesn't exist
        private string LegacyNpcInitPath => Path.GetFullPath(Path.Join(SavesFolderPath, "UNITYNPCINIT.json"));

        public bool HasNpcInitSnapshot(string worldName = null)
        {
            if (worldName != null) return File.Exists(GetNpcInitPath(worldName));
            // Any world has an init if either world-specific or legacy file exists
            return Directory.Exists(SavesFolderPath) &&
                   (Directory.GetFiles(SavesFolderPath, "UNITYNPCINIT_*.json").Length > 0 ||
                    File.Exists(LegacyNpcInitPath));
        }

        public void SaveNpcInitSnapshot(IList<NpcContainer> allNpcs)
        {
            var npcInit = new UnityNpcInit { WorldName = CurrentWorldName };
            foreach (var npc in allNpcs)
            {
                if (npc.Vob.Player) continue;
                var attrs = new int[8];
                for (var i = 0; i < 8; i++) attrs[i] = npc.Vob.GetAttribute(i);
                npcInit.Npcs.Add(new NpcInitEntry
                {
                    InstanceId = npc.InstanceId,
                    SymbolIndex = npc.SymbolIndex,
                    GoName = npc.GoName ?? "",
                    WaypointName = npc.SpawnWaypoint ?? "",
                    NpcInstance = npc.Vob.NpcInstance,
                    Attributes = attrs
                });
            }
            Directory.CreateDirectory(SavesFolderPath);
            var path = GetNpcInitPath(CurrentWorldName);
            File.WriteAllText(path, JsonConvert.SerializeObject(npcInit, Formatting.Indented));
            Logger.Log($"SaveNpcInitSnapshot: {npcInit.Npcs.Count} NPCs for '{CurrentWorldName}' → {path}", LogCat.Loading);
        }

        private void FlushDaedalusState(SaveGame saveGame)
        {
            var vm = _gameStateService.GothicVm;
            if (vm == null)
            {
                Logger.LogWarning("FlushDaedalusState: GothicVm not available, skipping.", LogCat.Loading);
                return;
            }

            var now = _gameTimeService.GetCurrentDateTime();
            var day = now.Day - 1; // DateTime.Day is 1-indexed; Gothic day is 0-indexed
            var hour = now.Hour;
            var minute = now.Minute;

            saveGame.State.Day = day;
            saveGame.State.Hour = hour;
            saveGame.State.Minute = minute;
            saveGame.Metadata.TimeDay = day;
            saveGame.Metadata.TimeHour = hour;
            saveGame.Metadata.TimeMinute = minute;

            var symbolStates = new List<SaveSymbolState>();

            for (var i = 0; i < vm.SymbolCount; i++)
            {
                var sym = vm.GetSymbolByIndex(i);
                if (sym == null || sym.IsConst || sym.IsMember || sym.IsExternal || sym.IsGenerated) continue;
                if (sym.Type != DaedalusDataType.Int && sym.Type != DaedalusDataType.Float) continue;
                if (sym.Size == 0) continue;

                var values = new List<int>(sym.Size);
                for (var j = 0; j < sym.Size; j++)
                {
                    values.Add(sym.Type == DaedalusDataType.Float
                        ? BitConverter.SingleToInt32Bits(sym.GetFloat((ushort)j))
                        : sym.GetInt((ushort)j));
                }

                symbolStates.Add(new SaveSymbolState { Name = sym.Name, Values = values });
            }

            saveGame.State.SymbolStates = symbolStates;

            // Copy quest log topics and dialog told-states — both services mutate Save.State directly
            if (Save?.State != null)
            {
                for (var i = 0; i < Save.State.LogTopicCount; i++)
                    saveGame.State.AddLogTopic(Save.State.GetLogTopic(i));

                for (var i = 0; i < Save.State.InfoStateCount; i++)
                    saveGame.State.AddInfoState(Save.State.GetInfoState(i));
            }

            Logger.Log($"FlushDaedalusState: {symbolStates.Count} symbols, {saveGame.State.LogTopicCount} log topics, {saveGame.State.InfoStateCount} info states, day={day} {hour}:{minute:D2}", LogCat.Loading);
        }

        private void RestoreDaedalusState(SaveGame save)
        {
            if (save.State.SymbolStateCount == 0)
            {
                Logger.LogWarning("RestoreDaedalusState: No symbols in save, skipping.", LogCat.Loading);
                return;
            }

            var vm = _gameStateService.GothicVm;
            if (vm == null)
            {
                Logger.LogWarning("RestoreDaedalusState: GothicVm not available.", LogCat.Loading);
                return;
            }

            var restored = 0;
            var skipped = 0;

            foreach (var state in save.State.SymbolStates)
            {
                var sym = vm.GetSymbolByName(state.Name);
                if (sym == null) { skipped++; continue; }

                for (var j = 0; j < state.Values.Count && j < sym.Size; j++)
                {
                    if (sym.Type == DaedalusDataType.Float)
                        sym.SetFloat(BitConverter.Int32BitsToSingle(state.Values[j]), (ushort)j);
                    else
                        sym.SetInt(state.Values[j], (ushort)j);
                }
                restored++;
            }

            // SetTime supports hour > 23 to advance days (hour = day*24 + hour)
            _gameTimeService.SetTime(save.State.Day * 24 + save.State.Hour, save.State.Minute);

            Logger.Log($"RestoreDaedalusState: {restored} symbols restored, {skipped} skipped (not in script), day={save.State.Day} {save.State.Hour}:{save.State.Minute:D2}", LogCat.Loading);
        }

        private List<NpcProxy> WrapVobs(List<ZenKit.Vobs.Npc> npcs)
        {
            return npcs.Select(i => new NpcProxy(i)).ToList();
        }
        
        /// <summary>
        /// Wrap VOB types with our Adapter grants us more flexibility in using it at runtime (e.g., fetching setter and altering logic).
        /// </summary>
        private List<IVirtualObject> WrapVobs(List<IVirtualObject> vobs)
        {
            var wrappedVobs = new List<IVirtualObject>();

            foreach (var vob in vobs)
            {
                
                wrappedVobs.Add(vob.Type switch
                {
                    VirtualObjectType.oCNpc => new NpcProxy(vob),
                    _ => vob
                });
            }

            return wrappedVobs;
        }
        
        /// <summary>
        /// Before saving the VOBs in a SaveGame, we need to unwrap our Adapters. Otherwise we get a Cast Exception from ZK C++ side.
        /// </summary>
        private List<IVirtualObject> UnwrapVobs(List<IVirtualObject> vobs)
        {
            var unwrappedVobs = new List<IVirtualObject>();

            foreach (var vob in vobs)
            {
                unwrappedVobs.Add(vob.Type switch
                {
                    VirtualObjectType.oCNpc => ((NpcProxy)vob).GetVob(),
                    _ => vob
                });
            }

            return unwrappedVobs;
        }

        /// <summary>
        /// Gothic mirrors save folder per mod: vanilla uses "Saves/", mods use "saves_{modname}/".
        /// E.g. DM_E.ini → saves_dm_e/
        /// </summary>
        public string SavesFolderPath
        {
            get
            {
                var modIni = _configService.EffectiveModIni;
                var folderName = string.IsNullOrEmpty(modIni)
                    ? "Saves"
                    : $"saves_{Path.GetFileNameWithoutExtension(modIni).ToLower()}";
                return Path.GetFullPath(Path.Join(_contextGameVersionService.RootPath, folderName));
            }
        }

        private string GetSaveGamePath(SlotId folderSaveId)
            => Path.GetFullPath(Path.Join(SavesFolderPath, $"savegame{(int)folderSaveId}"));

    }
}
