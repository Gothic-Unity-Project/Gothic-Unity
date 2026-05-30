using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using GUZ.Core.Const;
using GUZ.Core.Domain.Config;
using GUZ.Core.Domain.StaticCache;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Services.StaticCache
{
    public class StaticCacheService
    {
        private const string _gzipExt = ".gz";

        private const string _fileNameGlobalMetadata = "metadata.json";
        private const string _fileNameGlobalVobBounds = "vob-bounds.json";
        private const string _fileNameGlobalVobItemCollider ="vob-item-collider.json"; 
        private const string _fileNameGlobalTextureArrayData = "texture-arrays.json";

        private const string _fileNameWorldChunks = "world-chunks.json";
        private const string _fileNameStationaryLights = "world-stationary-lights.json";

        private const string _debugFileName = "debug.json";

        private string _cacheRootFolderPath;
        private bool _configIsCompressed;

        
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;


        public bool IsGlobalCacheLoaded { get; private set; }

        public Dictionary<string, Bounds> LoadedVobsBounds { get; private set; }
        public Dictionary<string, List<VobItemColliderCacheCreatorDomain.Data>> LoadedVobItemColliders { get; private set; }


        // During Mesh creation, we need to get the index of a TextureArray entry. For efficient lookup, we store the index here.
        public Dictionary<string, (int Index, TextureInfo Data)> LoadedTextureInfoOpaque { get; private set; }
        public Dictionary<string, (int Index, TextureInfo Data)> LoadedTextureInfoTransparent { get; private set; }
        public Dictionary<string, (int Index, TextureInfo Data)> LoadedTextureInfoWater { get; private set; }

        public WorldChunkContainer LoadedWorldChunks;
        public StationaryLightContainer LoadedStationaryLights;
        public DebugDataContainer LoadedDebugData;

        
        [Inject] private readonly ConfigService _configService;

        public struct TextureInfo
        {
            public TextureInfo(TextureCacheService.TextureArrayTypes textureArrayType, int maxDimension, int animFrameCount)
            {
                T = textureArrayType;
                MaxDim = maxDimension;
                AnimFrameC = animFrameCount;
            }

            public TextureCacheService.TextureArrayTypes T; // TextureArrayType
            public int MaxDim; // MaxDimension
            public int AnimFrameC; // AnimFrameCount
        }

        public struct StationaryLightInfo
        {
            public StationaryLightInfo(Vector3 position, float range, Color linearColor)
            {
                P = position;
                R = range;
                C = linearColor;
            }

            public Vector3 P; // Position
            public float R; // Range
            public Color C; // LinearColor
        }

        [Serializable]
        public class MetadataContainer
        {
            public string Version;
            public string CreationTime;
        }

        [Serializable]
        public class VobBoundsContainer
        {
            public List<VobBoundsEntry> BoundsEntries;
        }

        [Serializable]
        public class VobBoundsEntry
        {
            public VobBoundsEntry(string meshName, Bounds bounds)
            {
                Mesh = meshName;
                Bounds = bounds;
            }

            public string Mesh; // MeshName
            public Bounds Bounds;
        }
        
        [Serializable]
        public class VobItemColliderContainer
        {
            public List<VobColliderEntry> ColliderEntries;
        }
        
        [Serializable]
        public class VobColliderEntry
        {
            public VobColliderEntry(string meshName, List<VobItemColliderCacheCreatorDomain.Data> colliderData)
            {
                Mesh = meshName;
                Colls = colliderData;
            }

            public string Mesh; // MeshName
            public List<VobItemColliderCacheCreatorDomain.Data> Colls; // Colliders
        }

        [Serializable]
        public class TextureArrayContainer
        {
            public List<TextureArrayEntry> TexturesOpaque;
            public List<TextureArrayEntry> TexturesTransparent;
            public List<TextureArrayEntry> TexturesWater;
        }

        [Serializable]
        public class TextureArrayEntry
        {
            public TextureArrayEntry(string textureName, int maxDimension, int animationFrameCount)
            {
                Tex = textureName;
                MaxDim = maxDimension;
                AnimFrameC = animationFrameCount;
            }

            public string Tex; // TextureName
            public int MaxDim; // MaxDimension
            public int AnimFrameC; // AnimationFrameCount
        }

        [Serializable]
        public class WorldChunkContainer
        {
            public List<WorldChunkCacheCreatorDomain.WorldChunk> OpaqueChunks;
            public List<WorldChunkCacheCreatorDomain.WorldChunk> TransparentChunks;
            public List<WorldChunkCacheCreatorDomain.WorldChunk> WaterChunks;
        }

        [Serializable]
        public class StationaryLightContainer
        {
            public List<StationaryLightEntry> StationaryLights;
        }

        [Serializable]
        public class StationaryLightEntry
        {
            public StationaryLightEntry(Vector3 position, float range, Color linearColor)
            {
                P = position;
                R = range;
                Col = linearColor;
            }

            public Vector3 P; // Position
            public float R; // Range
            public Color Col; // LinearColor
        }

        [Serializable]
        public class DebugDataContainer
        {
            public List<Vector3> LightPositions;
        }


        public void Init()
        {
            _configIsCompressed = _configService.Dev.CompressStaticCacheFiles;
            _cacheRootFolderPath = $"{Application.persistentDataPath}/Cache/{_contextGameVersionService.Version}/";
        }

        /// <summary>
        /// We check for all required cache files once. If any of these are missing, the whole cache is marked as invalid.
        /// </summary>
        public bool DoCacheFilesExist(List<string> worldNames)
        {
            // Check all world specific files.
            foreach (var worldName in worldNames)
            {
                if (!File.Exists(BuildFilePathName(_fileNameWorldChunks, worldName)) ||
                    !File.Exists(BuildFilePathName(_fileNameStationaryLights, worldName)))
                {
                    return false;
                }
            }

            return DoGlobalCacheFilesExist();
        }

        public bool DoGlobalCacheFilesExist()
        {
            // Global files
            return File.Exists(BuildFilePathName(_fileNameGlobalMetadata)) &&
                   File.Exists(BuildFilePathName(_fileNameGlobalTextureArrayData)) &&
                   File.Exists(BuildFilePathName(_fileNameGlobalVobBounds)) &&
                   File.Exists(BuildFilePathName(_fileNameGlobalVobItemCollider))
                   ;
        }

        /// <summary>
        /// Inside PreCachingScene, we want to fetch this file separately. Its value will tell us if we can skip caching this time.
        /// </summary>
        public async Task<MetadataContainer> ReadMetadata()
        {
            var metadataString = await ReadData(BuildFilePathName(_fileNameGlobalMetadata));
            return await ParseJson<MetadataContainer>(metadataString);
        }

        /// <summary>
        /// Create cache folder and delete existing - stalled - files (if any).
        /// </summary>
        public void InitCacheFolder()
        {
            // Extract root directory from "fake" file path.
            var directory = Directory.GetParent(BuildFilePathName(""))!;

            // Create cache folder if it doesn't exist
            Directory.CreateDirectory(directory.FullName);

            // Cleanup existing files and directories (if we renamed some with a new version, these stalled files will be deleted as well)
            Directory.EnumerateFiles(directory.FullName).ForEach(File.Delete);
            Directory.EnumerateDirectories(directory.FullName).ForEach(dir => Directory.Delete(dir, true));
        }

        public async Task SaveGlobalCache(Dictionary<string, Bounds> vobBounds,
            Dictionary<string, List<VobItemColliderCacheCreatorDomain.Data>> itemCollider,
            Dictionary<string, TextureInfo> textureArrayInformation)
        {
            try
            {
                var metadataContainer = new MetadataContainer
                {
                    Version = Constants.StaticCacheVersion,
                    CreationTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.K")
                };
                await SaveCacheFile(metadataContainer, BuildFilePathName(_fileNameGlobalMetadata));

                var vobBoundsContainer = new VobBoundsContainer
                {
                    BoundsEntries = vobBounds.Select(i => new VobBoundsEntry(i.Key, i.Value)).ToList()
                };
                await SaveCacheFile(vobBoundsContainer, BuildFilePathName(_fileNameGlobalVobBounds));

                var vobItemCollider = new VobItemColliderContainer()
                {
                    ColliderEntries = itemCollider.Select(i => new VobColliderEntry(i.Key, i.Value)).ToList()
                };
                await SaveCacheFile(vobItemCollider, BuildFilePathName(_fileNameGlobalVobItemCollider));

                var textureArrayContainer = new TextureArrayContainer
                {
                    TexturesOpaque = textureArrayInformation
                        .Where(i => i.Value.T == TextureCacheService.TextureArrayTypes.Opaque)
                        .Select(i => new TextureArrayEntry(i.Key, i.Value.MaxDim, i.Value.AnimFrameC)).ToList(),
                    TexturesTransparent = textureArrayInformation
                        .Where(i => i.Value.T == TextureCacheService.TextureArrayTypes.Transparent)
                        .Select(i => new TextureArrayEntry(i.Key, i.Value.MaxDim, i.Value.AnimFrameC)).ToList(),
                    TexturesWater = textureArrayInformation
                        .Where(i => i.Value.T == TextureCacheService.TextureArrayTypes.Water)
                        .Select(i => new TextureArrayEntry(i.Key, i.Value.MaxDim, i.Value.AnimFrameC)).ToList(),
                };
                await SaveCacheFile(textureArrayContainer, BuildFilePathName(_fileNameGlobalTextureArrayData));
            }
            catch (Exception e)
            {
                Logger.LogError($"There was some error while storing Global Cache: {e}", LogCat.PreCaching);
                throw;
            }
        }

        public async Task SaveWorldCache(
            string worldName,
            Dictionary<TextureCacheService.TextureArrayTypes, List<WorldChunkCacheCreatorDomain.WorldChunk>> mergedChunksByLights,
            List<StationaryLightInfo> stationaryLightInfos)
        {
            try
            {
                var worldChunkData = new WorldChunkContainer()
                {
                    OpaqueChunks = mergedChunksByLights[TextureCacheService.TextureArrayTypes.Opaque],
                    TransparentChunks = mergedChunksByLights[TextureCacheService.TextureArrayTypes.Transparent],
                    WaterChunks = mergedChunksByLights[TextureCacheService.TextureArrayTypes.Water],
                };

                var stationaryLightData = new StationaryLightContainer()
                {
                    StationaryLights = stationaryLightInfos.Select(i => new StationaryLightEntry(i.P, i.R, i.C)).ToList()
                };

                await SaveCacheFile(worldChunkData, BuildFilePathName(_fileNameWorldChunks, worldName));
                await SaveCacheFile(stationaryLightData, BuildFilePathName(_fileNameStationaryLights, worldName));
            }
            catch (Exception e)
            {
                Logger.LogError($"There was some error while storing Mesh Cache: {e}", LogCat.PreCaching);
                throw;
            }
        }

        public async Task SaveDebugCache(
            string worldName,
            List<Bounds> lightBoundsUsedForWorldChunkCreation)
        {
            try
            {
                var lightPosForWorldChunks = new DebugDataContainer
                {
                    LightPositions = lightBoundsUsedForWorldChunkCreation.Select(i => i.center).ToList()
                };

                await SaveCacheFile(lightPosForWorldChunks, BuildFilePathName(_debugFileName, worldName));
            }
            catch (Exception e)
            {
                Logger.LogError($"There was some error while storing Mesh Cache: {e}", LogCat.PreCaching);
                throw;
            }
        }

        public async Task LoadGlobalCache()
        {
            if (IsGlobalCacheLoaded)
            {
                return;
            }
            IsGlobalCacheLoaded = true;
            
            var vobBoundsString = await ReadData(BuildFilePathName(_fileNameGlobalVobBounds));
            var vobItemColliderString = await ReadData(BuildFilePathName(_fileNameGlobalVobItemCollider));
            var textureArrayString = await ReadData(BuildFilePathName(_fileNameGlobalTextureArrayData));

            var vobBoundsContainer = await ParseJson<VobBoundsContainer>(vobBoundsString);
            var vobItemsColliderContainer = await ParseJson<VobItemColliderContainer>(vobItemColliderString);
            var textureArrayContainer = await ParseJson<TextureArrayContainer>(textureArrayString);
            
            LoadedVobsBounds = vobBoundsContainer.BoundsEntries.ToDictionary(i => i.Mesh, i => i.Bounds);
            LoadedVobItemColliders = vobItemsColliderContainer.ColliderEntries.ToDictionary(i => i.Mesh, i => i.Colls);

            var loopIndex = 0;
            LoadedTextureInfoOpaque = textureArrayContainer.TexturesOpaque
                .ToDictionary(i => i.Tex, i => (index: loopIndex++, data: new TextureInfo(TextureCacheService.TextureArrayTypes.Opaque, i.MaxDim, i.AnimFrameC)));

            loopIndex = 0;
            LoadedTextureInfoTransparent = textureArrayContainer.TexturesTransparent
                .ToDictionary(i => i.Tex, i => (index: loopIndex++, data: new TextureInfo(TextureCacheService.TextureArrayTypes.Transparent, i.MaxDim, i.AnimFrameC)));

            loopIndex = 0;
            LoadedTextureInfoWater = textureArrayContainer.TexturesWater
                .ToDictionary(i => i.Tex, i => (index: loopIndex++, data: new TextureInfo(TextureCacheService.TextureArrayTypes.Water, i.MaxDim, i.AnimFrameC)));
        }

        public async Task LoadWorldCache(string worldName)
        {
            var stopwatch = Stopwatch.StartNew();

            var worldChunkString = await ReadData(BuildFilePathName(_fileNameWorldChunks, worldName));
            LoadedWorldChunks = await ParseJson<WorldChunkContainer>(worldChunkString);

            var stationaryLightString = await ReadData(BuildFilePathName(_fileNameStationaryLights, worldName));
            LoadedStationaryLights = await ParseJson<StationaryLightContainer>(stationaryLightString);

            stopwatch.Log("Loading world cache done.");
        }

        public async Task LoadDebugCache(string worldName)
        {
            var debugDataString = await ReadData(BuildFilePathName(_debugFileName, worldName));
            LoadedDebugData = await ParseJson<DebugDataContainer>(debugDataString);
        }

        private async Task SaveCacheFile(object data, string filePathName)
        {
            string json = null;
            var prettyPrint = !_configIsCompressed;

            // We need to call loading the data in a separate thread to unblock main thread (VR movement etc.) during this IO heavy operation.
            await Task.Run(() =>
            {
                json = JsonUtility.ToJson(data, prettyPrint);
            });

            await WriteData(json, filePathName);
        }

        private async Task WriteData(string data, string filePath)
        {
            if (_configIsCompressed)
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
                using (var writer = new StreamWriter(gzipStream))
                {
                    await writer.WriteAsync(data);
                }
            }
            else
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                using (var writer = new StreamWriter(fileStream))
                {
                    await writer.WriteAsync(data);
                }
            }
        }

        private async Task<string> ReadData(string filePath)
        {
            string data = null;
            var filePathCaseInsensitive = IniLoader.FindFileCaseInsensitive(filePath);

            if (filePathCaseInsensitive == null)
                throw new FileNotFoundException($"Can't load cached file. File at >{filePath}< not found.");
            
            if (_configIsCompressed)
            {
                using (var fileStream = new FileStream(filePathCaseInsensitive, FileMode.Open))
                using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzipStream))
                {
                    // We need to call loading the data in a separate thread to unblock main thread (VR movement etc.) during this IO heavy operation.
                    await Task.Run(() =>
                    {
                        data = reader.ReadToEnd();
                    });
                }
            }
            else
            {
                using (var fileStream = new FileStream(filePathCaseInsensitive, FileMode.Open))
                using (var reader = new StreamReader(fileStream))
                {
                    // We need to call loading the data in a separate thread to unblock main thread (VR movement etc.) during this IO heavy operation.
                    await Task.Run(() =>
                    {
                        data = reader.ReadToEnd();
                    });
                }
            }

            return data;
        }

        /// <summary>
        /// Parse string to json dynamically.
        /// </summary>
        private async Task<T> ParseJson<T>(string json)
        {
            T data = default;

            // We need to call parsing the data in a separate thread to unblock main thread (VR movement etc.) during this CPU heavy operation.
            await Task.Run(() =>
            {
                data = JsonUtility.FromJson<T>(json);
            });

            return data;
        }

        private string BuildFilePathName(string fileName, string worldName = null)
        {
            var suffix = _configIsCompressed ? _gzipExt : "";

            if (worldName == null)
            {
                return $"{_cacheRootFolderPath}/{fileName}{suffix}";
            }
            else
            {
                return $"{_cacheRootFolderPath}/{worldName}-{fileName}{suffix}";
            }
        }
    }
}
