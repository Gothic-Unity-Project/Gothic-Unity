using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Domain.SaveGames;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Models.SaveGames;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Extensions;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using Mesh = ZenKit.Mesh;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Usage:
    ///
    /// Loading (each returns false and keeps the running game if the save game or world can't be read):
    /// * LoadNewGame(world) | LoadSavedGame(slot, world) -> Initializes the game state and its first world.
    /// * ChangeWorld(world) -> Level change. The world comes from this session, the save game or its .zen.
    ///
    /// Saving:
    /// * CanSave(out reason) -> Like Gothic: no saving while dead, in a dialog or without a world.
    /// * SaveCurrentGameAtEndOfFrame(slot, title) -> Stores all worlds of this session (needs the frame's end for
    ///   the thumbnail; SaveCurrentGame() if you're inside such a coroutine already).
    ///
    /// Helper methods:
    /// * GetSaveGame(slot) -> Return a save game object (or null), e.g. for the load menu.
    ///
    /// A save game consists of Gothic's files (written by ZenKit) and UNITYSAVE.json for state Gothic doesn't know
    /// (see ISaveStateSection). Details: Docs/save-game.md
    /// </summary>
    public class SaveGameService
    {
        public SlotId SaveGameId;
        public bool IsNewGame => SaveGameId == SlotId.NewGame;
        public bool IsLoadedGame => !IsNewGame;
        public bool IsWorldEnteredFirstTime;

        public bool IsFirstWorldLoadingFromSaveGame; // Check if we load save game right now!

        /// <summary>
        /// The current world's state comes from a save game file or from a visit earlier in this session (not from its
        /// .zen). E.g. world start triggers don't fire then (OpenGothic: no triggerOnStart() when loading).
        /// </summary>
        public bool IsWorldRestored;

        public SaveGame Save;

        /// <summary>
        /// INIT_* scripts of a world restored from a save/visit run. Wld_Insert* calls are skipped meanwhile.
        /// </summary>
        public bool IsRunningInitScriptsOfRestoredWorld { get; private set; }

        private const string _savesFolderName = "Saves_GothicUnity";

        private readonly Dictionary<string, ISaveStateSection> _sections = new();
        private bool _isInitialized;

        // UNITYSAVE.json of the loaded save game. Its sections without a registered owner are saved again.
        private UnitySaveData _loadedUnitySave;

        // World names differ in casing (e.g. World.zen from the ini, WORLD from a save/level change).
        private readonly Dictionary<string, WorldContainer> _worlds = new(StringComparer.OrdinalIgnoreCase);
        public string CurrentWorldName;
        public WorldContainer CurrentWorldData => _worlds[CurrentWorldName];

        // Gothic's play time (SAVEINFO PlayTimeSeconds): counted while a world runs, not on loading screens.
        private double _playTimeSeconds;
        private float _playTimeCountingSince = -1f;

        public TimeSpan PlayTime => TimeSpan.FromSeconds(_playTimeSeconds +
            (_playTimeCountingSince >= 0f ? Time.realtimeSinceStartup - _playTimeCountingSince : 0f));


        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        private readonly SaveGameWorldDomain _worldDomain = new SaveGameWorldDomain().Inject();
        private readonly SaveGameNpcDomain _npcDomain = new SaveGameNpcDomain().Inject();
        private readonly SaveGameDaedalusDomain _daedalusDomain = new SaveGameDaedalusDomain().Inject();
        private readonly SaveGameThumbnailDomain _thumbnailDomain = new SaveGameThumbnailDomain().Inject();
        private readonly UnitySaveDomain _unitySaveDomain = new UnitySaveDomain().Inject();


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
            if (_isInitialized)
                return;
            _isInitialized = true;

            // Core's sections. Modules (VR, Flat, G1/G2, mods) register their own.
            RegisterSection(new ScriptArraysSaveSection().Inject());
            RegisterSection(new ScriptVarsSaveSection().Inject());
            RegisterSection(new SkySaveSection().Inject());
            RegisterSection(new MobStatesSaveSection().Inject());

            // All NPCs of the world exist now.
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(() =>
                _npcDomain.RestoreTargets(_multiTypeCacheService.NpcCache));

            GlobalEventDispatcher.WorldSceneLoaded.AddListener(() =>
                _playTimeCountingSince = Time.realtimeSinceStartup);
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(() => StartPlayTime(PlayTime.TotalSeconds));
        }

        /// <summary>
        /// Continues from seconds once the next world runs (WorldSceneLoaded).
        /// </summary>
        private void StartPlayTime(double seconds)
        {
            _playTimeSeconds = seconds;
            _playTimeCountingSince = -1f;
        }

        /// <summary>
        /// Register state which needs to be stored inside UNITYSAVE.json. See ISaveStateSection.
        /// </summary>
        public void RegisterSection(ISaveStateSection section)
        {
            if (!_sections.TryAdd(section.Key, section))
                Logger.LogError($"[SaveGame] Section {section.Key} is registered already.", LogCat.Loading);
        }

        /// <summary>
        /// Returns false (and keeps the running game) if the world can't be read.
        /// </summary>
        public bool LoadNewGame(string worldName)
        {
            worldName = GetWorldKey(worldName);
            if (!TryReadWorld(worldName, null, out var world, out _))
                return false;

            GlobalEventDispatcher.UnloadGameStart.Invoke();
            GlobalEventDispatcher.LoadGameStart.Invoke();

            SaveGameId = 0;
            Save = new SaveGame(_contextGameVersionService.Version);
            _loadedUnitySave = null;
            ResetSections();
            StartPlayTime(0);

            // A previously loaded save changed the time.
            _gameTimeService.ResetToStartTime();
            IsFirstWorldLoadingFromSaveGame = true;
            _worlds.ClearAndReleaseMemory();
            EnterWorld(worldName, world, false);
            return true;
        }

        /// <summary>
        /// Hint: G1 save game folders start with 1. We leverage the same numbering.
        /// Returns false (and keeps the running game) if the save game or its world can't be read.
        /// </summary>
        public bool LoadSavedGame(SlotId saveGameId, string worldName)
        {
            // Check before the running game is replaced (LoadGameStart creates a new VM).
            var save = GetSaveGame(saveGameId);
            if (save == null)
            {
                Logger.LogError($"SaveGame with id {saveGameId} can't be loaded.", LogCat.Loading);
                return false;
            }

            worldName = GetWorldKey(worldName);
            if (!TryReadWorld(worldName, save, out var world, out var isWorldInSave))
                return false;

            GlobalEventDispatcher.UnloadGameStart.Invoke();
            GlobalEventDispatcher.LoadGameStart.Invoke();

            SaveGameId = saveGameId;
            Save = save;
            IsFirstWorldLoadingFromSaveGame = true;
            _worlds.ClearAndReleaseMemory();
            EnterWorld(worldName, world, isWorldInSave);
            StartPlayTime(save.Metadata.PlayTime.TotalSeconds);

            // LoadGameStart created a fresh VM. Script globals, time and guild attitudes come from SAVEDAT.SAV.
            _daedalusDomain.Restore(save);

            _loadedUnitySave = _unitySaveDomain.Read(GetSaveGamePath(saveGameId));
            ResetSections();
            _unitySaveDomain.Restore(_loadedUnitySave, _sections);
            return true;
        }

        private void ResetSections()
        {
            foreach (var section in _sections.Values)
                section.Reset();
        }

        /// <summary>
        /// Level change. Loading logic order:
        /// 1. Check if the world is already loaded (cached) for this game session (i.e. we visited it already in this
        ///    session)
        /// 2. Try to load the world state from the save game
        /// 3. Either use this saved world data or load it from normal .zen file
        /// Returns false (and keeps the current world) if the world can't be read.
        /// </summary>
        public bool ChangeWorld(string worldName)
        {
            worldName = GetWorldKey(worldName);

            // 1. World was already loaded.
            var isRestored = _worlds.TryGetValue(worldName, out var world);
            if (!isRestored && !TryReadWorld(worldName, IsLoadedGame ? Save : null, out world, out isRestored))
                return false;

            // Keep the state of the world we leave (the caches are cleared when the next world loads).
            StoreCurrentWorldState();

            EnterWorld(worldName, world, isRestored);
            return true;
        }

        /// <summary>
        /// G2 has for example: AddonWorld\NewWorld.zen --> NewWorld.zen
        /// Linux doesn't see \ as a directory separator, Windows sees both \ and /
        /// </summary>
        private static string GetWorldKey(string worldName)
        {
            return Path.GetFileName(worldName.Replace("\\", "/"));
        }

        private void EnterWorld(string worldName, WorldContainer world, bool isRestored)
        {
            _worlds[worldName] = world;
            CurrentWorldName = worldName;
            IsWorldRestored = isRestored;
            IsWorldEnteredFirstTime = !isRestored;
        }

        /// <summary>
        /// Reads a world from the save game (if it contains it) or from its .zen. Nothing is changed, so a world which
        /// can't be read (e.g. an unsupported VOB class in a mod's .zen) keeps the running game.
        /// </summary>
        private bool TryReadWorld(string worldName, [CanBeNull] SaveGame save, out WorldContainer world,
            out bool isWorldInSave)
        {
            world = null;
            isWorldInSave = false;
            try
            {
                // Always needed for some data not present in SaveGame.
                var originalWorld = _resourceCacheService.TryGetWorld(worldName);
                if (originalWorld == null)
                {
                    Logger.LogError($"[SaveGame] World {worldName} doesn't exist.", LogCat.Loading);
                    return false;
                }

                // 2. Try to load world from save game.
                // Worlds are saved upper case without extension (e.g. NEWWORLD.SAV). ZenKit's lookup is case-sensitive.
                var saveGameWorld = save?.LoadWorld(worldName.ToGothicWorldName());
                isWorldInSave = saveGameWorld != null;

                // If there is no save game used or world not saved, we visit it for the first time.
                var worldToUse = saveGameWorld ?? originalWorld;

                // 3. Store this world into runtime data as it's now loaded and cached during gameplay. (To save later
                // when needed.)
                // TODO - If we get memory consumption issue, we can consider removing some data to free memory once
                //        world is loaded later.
                world = new WorldContainer
                {
                    OriginalWorld = originalWorld,
                    SaveGameWorld = saveGameWorld,

                    // Only existing in normal world (not in save game)
                    Mesh = (Mesh)originalWorld.Mesh, // Do not cache or memory consumption will be way too high
                    BspTree = (CachedBspTree)originalWorld.BspTree.Cache(),

                    // Only existing in SaveGame world
                    Npcs = WrapVobs(worldToUse.Npcs), // (if it's a new world, it's simply null)

                    // Contained inside both: normal .zen file and also saveGame.
                    Vobs = WrapVobs(FlattenLevelCompos(worldToUse.RootObjects)),
                    WayNet = (CachedWayNet)worldToUse.WayNet.Cache()
                };
                return true;
            }
            catch (Exception e)
            {
                // Mirrors PreCachingScene's per-world try/catch: a world can be unparsable without crashing the
                // whole session.
                Logger.LogError($"[SaveGame] World {worldName} can't be loaded: {e}", LogCat.Loading);
                return false;
            }
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
                Logger.LogWarning($"SaveGame inside folder >{saveGamePath}< doesn't exist.", LogCat.Loading);
                return null;
            }

            // E.g. an empty THUMB.SAV makes ZenKit stop before SAVEDAT.SAV is read.
            if (!save.Load(saveGamePath))
            {
                Logger.LogError($"SaveGame inside folder >{saveGamePath}< couldn't be loaded.", LogCat.Loading);
                return null;
            }

            return save;
        }

        /// <summary>
        /// Saving means:
        /// 1. Collect changed data from all the worlds visited during this gameplay
        /// 2. Alter its values inside the ZenKit data
        /// 3. Save world-by-world into the save game itself
        ///
        /// Hint: Needs to be called after EndOfFrame to ensure we can do a screenshot as thumbnail.
        ///       Use SaveCurrentGameAtEndOfFrame() if you're not inside such a coroutine already.
        /// Hint: If you want to compare an original Gothic save and a Gothic Unity save, use zen2zen and convert a save file
        ///       to ascii for comparison: https://github.com/GothicKit/ZenKit/blob/main/examples/zen2zen.cc
        /// </summary>
        public bool SaveCurrentGame(SlotId saveGameId, string title)
        {
            var hero = (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
            if (hero == null || CurrentWorldName == null || Save == null || !_worlds.ContainsKey(CurrentWorldName))
            {
                Logger.LogError("[SaveGame] No world loaded. Can't save.", LogCat.Loading);
                return false;
            }

            if (!CanSave(out var blockReason))
            {
                Logger.LogWarning($"[SaveGame] Saving isn't possible right now ({blockReason}).", LogCat.Loading);
                return false;
            }

            var slotPath = GetSaveGamePath(saveGameId);
            var tmpPath = slotPath + ".tmp";
            var isSaved = false;
            try
            {
                StoreCurrentWorldState(true);

                // We save through the runtime SaveGame (State contains told infos and log topics already).
                // ZenKit copies the folder of a loaded save into the target first. This keeps worlds we didn't visit
                // in this session.
                var saveGame = Save;
                saveGame.Metadata.Title = title;
                saveGame.Metadata.SaveDate = DateTime.Now.ToString("dd.MM.yyyy - HH:mm");
                saveGame.Metadata.World = CurrentWorldName.ToGothicWorldName();
                saveGame.Metadata.PlayTime = PlayTime;
                saveGame.Thumbnail = _thumbnailDomain.Create();
                _daedalusDomain.Flush(saveGame);

                if (Directory.Exists(tmpPath))
                    Directory.Delete(tmpPath, true);

                // The current world is saved last: SAVEHDR.SAV names the world of the last Save() call.
                var areWorldsSaved = true;
                foreach (var (worldName, worldContainer) in _worlds.OrderBy(i => IsCurrentWorld(i.Key)))
                {
                    var worldHero = IsCurrentWorld(worldName) ? hero : null;
                    if (_worldDomain.SaveWorld(saveGame, tmpPath, worldName, worldContainer, worldHero))
                        continue;

                    areWorldsSaved = false;
                    break;
                }

                if (areWorldsSaved)
                {
                    _unitySaveDomain.Write(tmpPath, _loadedUnitySave, _sections);
                    ReplaceSlotFolder(tmpPath, slotPath);
                    isSaved = true;
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"[SaveGame] Saving into {slotPath} failed: {e}", LogCat.Loading);
            }

            if (!isSaved)
            {
                ReopenRuntimeSaveGame();
                return false;
            }

            // ZenKit's SaveGame now points to the .tmp folder. Re-open it from the slot, so that worlds which aren't
            // loaded yet are fetched from the new save.
            var savedGame = GetSaveGame(saveGameId);
            if (savedGame != null)
            {
                Save = savedGame;
                SaveGameId = saveGameId;
                _loadedUnitySave = _unitySaveDomain.Read(slotPath);
            }

            Logger.Log($"[SaveGame] Saved >{title}< into {slotPath} ({_worlds.Count} world(s) of this session).",
                LogCat.Loading);
            return true;
        }

        /// <summary>
        /// Waits for the end of the frame (needed for the thumbnail) and saves then.
        /// </summary>
        public void SaveCurrentGameAtEndOfFrame(SlotId saveGameId, string title, Action<bool> onDone = null)
        {
            _unityMonoService.StartCoroutine(SaveAtEndOfFrame(saveGameId, title, onDone));
        }

        private IEnumerator SaveAtEndOfFrame(SlotId saveGameId, string title, Action<bool> onDone)
        {
            // One additional frame, so that menus/keyboards closed by the caller aren't part of the thumbnail.
            yield return null;
            yield return new WaitForEndOfFrame();

            onDone?.Invoke(SaveCurrentGame(saveGameId, title));
        }

        /// <summary>
        /// Like Gothic (OpenGothic: isInGameAndAlive, the dialog keeps the menu closed): a world is running, the hero
        /// is alive and no dialog runs. Otherwise, blockReason is the localization key of the reason.
        /// </summary>
        public bool CanSave(out string blockReason)
        {
            var hero = (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();
            if (hero == null || Save == null || CurrentWorldName == null ||
                SceneManager.GetSceneByName(Constants.SceneLoading).isLoaded)
                blockReason = "menuitem.save.blocked.noWorld";
            // HP <= 0 too: a script can take the hero's HP without the death transition (see ExtNpcIsDead()).
            else if (hero.Props.IsDead || hero.Instance.GetAttribute(NpcAttribute.HitPoints) <= 0)
                blockReason = "menuitem.save.blocked.dead";
            else if (_gameStateService.Dialogs.IsInDialog)
                blockReason = "menuitem.save.blocked.dialog";
            else
                blockReason = null;

            return blockReason == null;
        }

        /// <summary>
        /// The world is about to be left or saved. Store its runtime state into the world data (the caches are cleared
        /// when we leave), so that a revisit or save game restores it.
        /// </summary>
        public void StoreCurrentWorldState(bool isSaving = false)
        {
            if (CurrentWorldName == null || !_worlds.TryGetValue(CurrentWorldName, out var container))
                return;

            // E.g. VR: Items inside hands and holsters are inventory, items lying around are world items.
            GlobalEventDispatcher.StoreWorldStateStart.Invoke(isSaving);

            _worldDomain.StoreWorldState(container, isSaving);
        }

        /// <summary>
        /// Items taken from a chest/backpack aren't part of the world list. Nothing to do for them.
        /// </summary>
        public void RemoveVobFromWorld(IVirtualObject vob)
        {
            CurrentWorldData.Vobs.Remove(vob);
        }

        public void AddVobToWorld(IVirtualObject vob)
        {
            if (!CurrentWorldData.Vobs.Contains(vob))
                CurrentWorldData.Vobs.Add(vob);
        }

        /// <summary>
        /// A child VOB (e.g. an item lying on a mover) is taken: it's no longer part of its parent's VOB tree and is
        /// handled like any other item of the world from now on (world list, position sync).
        /// </summary>
        public void DetachChildVob(VobContainer container)
        {
            var parentVob = container.ParentVob;
            if (parentVob == null)
                return;

            // IVirtualObject.Children creates new wrappers on every call. We find the child by its values instead.
            var vob = container.Vob;
            for (var i = parentVob.ChildCount - 1; i >= 0; i--)
            {
                var child = parentVob.GetChild(i);
                if (child.Type != vob.Type || child.Name != vob.Name || child.Position != vob.Position)
                    continue;

                parentVob.RemoveChild(i);
                break;
            }

            container.ParentVob = null;
            _multiTypeCacheService.VobCache.Add(container);
        }

        /// <summary>
        /// Runs the INIT_* scripts of a restored world. Wld_Insert* calls are skipped meanwhile, as its NPCs and items
        /// exist already.
        /// </summary>
        public void RunInitScriptsOfRestoredWorld(Action initScripts)
        {
            IsRunningInitScriptsOfRestoredWorld = true;
            try
            {
                initScripts();
            }
            finally
            {
                IsRunningInitScriptsOfRestoredWorld = false;
            }
        }

        public void InitNpcInstanceFromSave(NpcContainer npc, Action initInstance)
        {
            _npcDomain.InitInstanceFromSave(npc, initInstance);
        }

        /// <summary>
        /// The hero of a loaded save is inside the VOB tree of its world.
        /// </summary>
        [CanBeNull]
        public NpcProxy GetSavedHero()
        {
            if (!IsLoadedGame || IsWorldEnteredFirstTime || CurrentWorldName == null)
                return null;

            return CurrentWorldData.Vobs.OfType<NpcProxy>().FirstOrDefault(i => i.Player);
        }

        /// <summary>
        /// Our saves never share the folder with original Gothic (Saves/) or a mod's GothicStarter saves.
        /// Each mod gets its own folder: e.g. Saves_GothicUnity_mod_DM_E/
        /// </summary>
        public string SavesFolderPath => Path.GetFullPath(Path.Join(_contextGameVersionService.RootPath,
            $"{_savesFolderName}{_configService.ModCacheSuffix}"));

        private string GetSaveGamePath(SlotId folderSaveId)
        {
            return Path.GetFullPath(Path.Join(SavesFolderPath, $"savegame{(int)folderSaveId}"));
        }

        private bool IsCurrentWorld(string worldName)
        {
            return string.Equals(worldName, CurrentWorldName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A save interrupted between the two moves of ReplaceSlotFolder() (crash, power loss) left savegameN.bak
        /// without savegameN: the old slot comes back. Leftover .tmp folders (half-written saves) are removed.
        /// </summary>
        public void RecoverInterruptedSaves()
        {
            if (!Directory.Exists(SavesFolderPath))
                return;

            try
            {
                foreach (var backupPath in Directory.GetDirectories(SavesFolderPath, "savegame*.bak"))
                {
                    var slotPath = backupPath[..^".bak".Length];
                    if (Directory.Exists(slotPath))
                        Directory.Delete(backupPath, true);
                    else
                    {
                        Directory.Move(backupPath, slotPath);
                        Logger.LogWarning($"[SaveGame] Restored {slotPath} from an interrupted save.", LogCat.Loading);
                    }
                }

                foreach (var tmpPath in Directory.GetDirectories(SavesFolderPath, "savegame*.tmp"))
                    Directory.Delete(tmpPath, true);
            }
            catch (Exception e)
            {
                Logger.LogError($"[SaveGame] Recovering interrupted saves failed: {e}", LogCat.Loading);
            }
        }

        /// <summary>
        /// The old slot is removed only once the new one is in place. If moving fails, the old slot comes back.
        /// A crash in between is recovered by RecoverInterruptedSaves().
        /// </summary>
        private static void ReplaceSlotFolder(string newSlotPath, string slotPath)
        {
            var backupPath = slotPath + ".bak";
            if (Directory.Exists(backupPath))
                Directory.Delete(backupPath, true);

            if (Directory.Exists(slotPath))
                Directory.Move(slotPath, backupPath);

            try
            {
                Directory.Move(newSlotPath, slotPath);
            }
            catch
            {
                if (Directory.Exists(backupPath) && !Directory.Exists(slotPath))
                    Directory.Move(backupPath, slotPath);
                throw;
            }

            if (Directory.Exists(backupPath))
                Directory.Delete(backupPath, true);
        }

        /// <summary>
        /// After a failed save, ZenKit's SaveGame points to the half-written .tmp folder (worlds not visited in this
        /// session would be read from there). We open the last successful save again. Our runtime SaveGame holds told
        /// infos and log topics since then, which we keep.
        /// </summary>
        private void ReopenRuntimeSaveGame()
        {
            var runtimeState = Save.State;
            var reopened = IsLoadedGame ? GetSaveGame(SaveGameId) : null;
            reopened ??= new SaveGame(_contextGameVersionService.Version);

            reopened.State.InfoStates = runtimeState.InfoStates;
            reopened.State.LogTopics = runtimeState.LogTopics;
            Save = reopened;
        }

        /// <summary>
        /// A .zen file has LevelCompos as roots, a save game not (G1 doesn't save them). We use their children
        /// directly: ZenKit creates new wrappers on every Children call, but items picked up later are removed from
        /// this list by reference (the same wrapper objects are used to create the world).
        /// </summary>
        private static List<IVirtualObject> FlattenLevelCompos(List<IVirtualObject> rootVobs)
        {
            var vobs = new List<IVirtualObject>();
            foreach (var vob in rootVobs)
            {
                if (vob.Type == VirtualObjectType.zCVobLevelCompo)
                    vobs.AddRange(FlattenLevelCompos(vob.Children));
                else
                    vobs.Add(vob);
            }

            return vobs;
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
    }
}
