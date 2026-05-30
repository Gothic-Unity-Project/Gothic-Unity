using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GUZ.Core.Const;
using GUZ.Core.Logging;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Proxy;
using GUZ.Core.Models.Container;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Context;
using GUZ.Core.Services.Culling;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = GUZ.Core.Logging.Logger;
using Mesh = ZenKit.Mesh;
using Texture = ZenKit.Texture;
using TextureFormat = ZenKit.TextureFormat;

namespace GUZ.Core.Services.World
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
            // Nothing to do for now.
        }

        public void LoadNewGame()
        {
            GlobalEventDispatcher.LoadGameStart.Invoke();

            SaveGameId = 0;
            Save = new SaveGame(_contextGameVersionService.Version);
            IsFirstWorldLoadingFromSaveGame = true;
            _worlds.ClearAndReleaseMemory();
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
            CurrentWorldName = Path.GetFileName(worldName);

            // 1. World was already loaded.
            if (_worlds.ContainsKey(CurrentWorldName))
            {
                IsWorldLoadedForTheFirstTime = false;
                return;
            }

            IsWorldLoadedForTheFirstTime = true;
            ZenKit.World originalWorld = _resourceCacheService.TryGetWorld(CurrentWorldName)!; // Always needed for some data not present in SaveGame.
            ZenKit.World saveGameWorld = null;
            bool worldFoundInSaveGame = false;

            // 2. Try to load world from save game.
            if (IsLoadedGame)
            {
                saveGameWorld = Save.LoadWorld(CurrentWorldName);
                worldFoundInSaveGame = saveGameWorld != null;
            }

            ZenKit.World worldToUse;
            if (worldFoundInSaveGame)
            {
                worldToUse = saveGameWorld;
                IsWorldLoadedForTheFirstTime = false;
                IsWorldEnteredFirstTime = false;
            }
            else
            {
                // If there is no save game used or world not saved, we visit it for the first time.
                worldToUse = originalWorld;
                IsWorldEnteredFirstTime = true;
            }

            // 3. Store this world into runtime data as it's now loaded and cached during gameplay. (To save later when needed.)
            // TODO - If we get memory consumption issue, we can consider removing some data to free memory once world is loaded later.
            _worlds[CurrentWorldName] = new WorldContainer
            {
                OriginalWorld = originalWorld,
                SaveGameWorld = saveGameWorld,

                // Only existing in normal world (not in save game)
                Mesh = (Mesh)originalWorld.Mesh, // Do not cache or memory consumption will be way too high
                BspTree = (CachedBspTree)originalWorld.BspTree.Cache(),

                // Only existing in SaveGame world
                Npcs = WrapVobs(worldToUse.Npcs), // (if it's a new world, it's simply null)

                // Contained inside both: normal .zen file and also saveGame.
                Vobs = WrapVobs(worldToUse!.RootObjects),
                WayNet = (CachedWayNet)worldToUse.WayNet.Cache()
            };
        }

        /// <summary>
        /// Load a Save Game.
        /// 
        /// Hint: If you want to compare an original Gothic save and an UnZENity save, use zen2zen and convert a save file
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
        /// Hint: If you want to compare an original Gothic save and an UnZENity save, use zen2zen and convert a save file
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

            foreach (var worldData in _worlds)
            {
                var worldContainer = worldData.Value;
                // FIXME - We need to create a new combined world first.
                
                // World not yet saved
                if (worldContainer.SaveGameWorld == null)
                {
                    // We simply load the world an additional time to have a Pointer to save later.
                    worldContainer.SaveGameWorld = _resourceCacheService.TryGetWorld(worldData.Key)!;
                }
                
                PrepareWorldDataForSaving(worldData.Key == CurrentWorldName, worldContainer);
                saveGame.Save(GetSaveGamePath(saveGameId), worldContainer.SaveGameWorld, worldData.Key.TrimEndIgnoreCase(".ZEN").ToUpper());
            }
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
                
                allVobs.Add(heroContainer.Vob);
            }

            _npcMeshCullingService.UpdateVobPositionOfVisibleNpcs();
            var visibleNpcs = _npcMeshCullingService.GetVisibleNpcs();

            foreach (var visibleNpc in visibleNpcs)
            {
                allVobs.Add(visibleNpc.Vob);
            }
            
            container.SaveGameWorld.RootObjects = UnwrapVobs(allVobs);
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

        private string GetSaveGamePath(SlotId folderSaveId)
        {
            var gothicDir = _contextGameVersionService.RootPath;
            return Path.GetFullPath(Path.Join(gothicDir, $"Saves/savegame{(int)folderSaveId}"));
        }
    }
}
