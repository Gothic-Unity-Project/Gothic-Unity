using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GUZ.Core.Adapters.UI.LoadingBars;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;
using TextureFormat = ZenKit.TextureFormat;

namespace GUZ.Core.Domain.StaticCache
{
    public class WorldChunkCacheCreatorDomain
    {
        [Serializable] // For cache saving reasons
        public class WorldChunk
        {
            public List<int> PolygonIds = new();
        }

        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        
        public Dictionary<TextureCacheService.TextureArrayTypes, List<WorldChunk>> MergedChunksByLights;

        /// <summary>
        /// G1.OrcTempel.zen has no dynamic lights. Therefore, all ~40k polygons will be placed in one chunk and Unity complains:
        /// 1. Detected one or more triangles where the distance between any 2 vertices is greater than 500 units. The resulting Triangle Mesh can impact simulation and query stability. It is recommended to tessellate meshes that have large triangles.
        /// 2. Part of the world will simply not being rendered.
        ///
        /// Quick fix: Simply having a max polygon amount per chunk. 10k proved to only slice the temple into 4 elements. No other G1 world was affected.
        /// </summary>
        private const int _maxAmountOfPolygonsPerChunk = 10000;
        
        private List<Bounds> _stationaryLightBounds;


        public async Task CalculateWorldChunks(IWorld world, List<Bounds> stationaryLightBounds, int worldIndex)
        {
            var cachedBspTree = (CachedBspTree)world.BspTree.Cache();
            
            var elementAmount = CalculateElementAmount(cachedBspTree);
            _loadingService.SetPhase($"{nameof(PreCachingLoadingBarHandler.ProgressTypesPerWorld.CalculateWorldChunks)}_{worldIndex}", elementAmount);

            _stationaryLightBounds = stationaryLightBounds;
            // Hint: We need to cache BspTree, otherwise looping through it will take ages.
            await BuildBspTree(world.Mesh, cachedBspTree);
            
            _loadingService.FinalizePhase();

        }

        private int CalculateElementAmount(CachedBspTree cachedBspTree)
        {
            // There are 2 calculations: LeafNode loop and Merging via Lights. But the second one is not yet known. Let's simply assume its the same size (worst case).
            return cachedBspTree.LeafNodeIndices.Count * 2;
        }

        private async Task BuildBspTree(IMesh worldMesh, CachedBspTree bspTree)
        {
            var leafsWithPolygons = await CalculatePolygonsToLeafNodes(worldMesh, bspTree, bspTree.LeafNodeIndices);

            await MergeWorldChunksByLightCount(worldMesh, bspTree, leafsWithPolygons);
        }

        /// <summary>
        /// Returns NODE_ID => List{POLYGON_ID}
        /// </summary>
        private async Task<Dictionary<int, List<int>>> CalculatePolygonsToLeafNodes(IMesh mesh, CachedBspTree bspTree, List<int> leafNodes)
        {
            var returnData = new Dictionary<int, List<int>>();

            // Multiple Nodes reference the same polygons. We need to ignore these duplicates (otherwise, we will draw them multiple times with no further use ;-).
            var usedPolygonIndices = new HashSet<int>();

            foreach (var nodeId in leafNodes)
            {
                // Hint: If calculation still stutters in framerate, then set this check at polygon-loop level.
                if (!_configService.Dev.SpeedUpLoading)
                {
                    await _frameSkipperService.TrySkipToNextFrame();
                }
                _loadingService.Tick();

                var currentNodePolygonIds = new List<int>();

                var node = bspTree.GetNode(nodeId);
                for (var polygonId = node.PolygonIndex; polygonId < node.PolygonIndex + node.PolygonCount; polygonId++)
                {
                    var polygonIndexToUse = bspTree.PolygonIndices[polygonId];

                    // Can't be added twice and therefore returns false the second time.
                    if (usedPolygonIndices.Contains(polygonIndexToUse))
                    {
                        continue;
                    }

                    // Different leaf nodes reference the same polygons.
                    // Manually check if polygons have been used to avoid creating overlapping geometry.
                    usedPolygonIndices.Add(polygonIndexToUse);

                    var polygon = mesh.GetPolygon(polygonIndexToUse);

                    // We ignore portals for now.
                    if (polygon.IsPortal)
                    {
                        continue;
                    }

                    currentNodePolygonIds.Add(polygonIndexToUse);
                }

                returnData.Add(nodeId, currentNodePolygonIds);
            }

            return returnData;
        }

        /// <summary>
        /// As a BspTree is ordered (aka left-right elements are next to each other), we assume, that all the leafNode information
        /// is next to each other. Aka if a submesh is "filled" with lights, we close it. And other leafNodes will open up a new mesh.
        ///
        /// Alternatively we would need to create an adjacent method to calculate if Nodes are next to each other.
        /// </summary>
        private async Task MergeWorldChunksByLightCount(IMesh mesh, CachedBspTree bspTree, Dictionary<int, List<int>> leafNodesWithPolygons)
        {
            var finalPolygonsOpaque = new List<WorldChunk>();
            var finalPolygonsTransparent = new List<WorldChunk>();
            var finalPolygonsWater = new List<WorldChunk>();

            var currentOpaqueChunkPolygons = new WorldChunk();
            var isCurrentNodeWithOpaque = false;
            var currentOpaqueChunkLightsCount = 0;
            var currentOpaqueAlreadyAffectingLightIndices = new HashSet<int>();

            var currentTransparentChunkPolygons = new WorldChunk();
            var isCurrentNodeWithTransparent = false;
            var currentTransparentChunkLightsCount = 0;
            var currentTransparentAlreadyAffectingLightIndices = new HashSet<int>();

            var currentWaterChunkPolygons = new WorldChunk();
            var isCurrentNodeWithWater = false;
            var currentWaterChunkLightsCount = 0;
            var currentWaterAlreadyAffectingLightIndices = new HashSet<int>();

            foreach (var nodeData in leafNodesWithPolygons)
            {
                // Hint: If calculation still stutters in framerate, then set this check at polygon-loop level.
                if (!_configService.Dev.SpeedUpLoading)
                {
                    await _frameSkipperService.TrySkipToNextFrame();
                }
                _loadingService.Tick();

                var node = bspTree.GetNode(nodeData.Key);
                var polygonIds = nodeData.Value;

                // Calculate lights ONCE per node, not per polygon (otherwise 3sec will go up to 50sec)
                var nodeBounds = node.BoundingBox.ToUnityBounds();

                isCurrentNodeWithOpaque = false;
                isCurrentNodeWithTransparent = false;
                isCurrentNodeWithWater = false;

                foreach (var polygonId in polygonIds)
                {
                    var polygon = mesh.GetPolygon(polygonId);
                    var material = mesh.GetMaterial(polygon.MaterialIndex);
                    var texture = _resourceCacheService.TryGetTexture(material.Texture);

                    if (texture == null)
                    {
                        continue;
                    }

                    if (material.Group == MaterialGroup.Water)
                    {
                        // First time we have a Polygon with Water in this node.
                        if (!isCurrentNodeWithWater)
                        {
                            currentWaterChunkLightsCount += GetLightsInBound(nodeBounds, currentWaterAlreadyAffectingLightIndices);
                            isCurrentNodeWithWater = true;
                        }
                        currentWaterChunkPolygons.PolygonIds.Add(polygonId);
                    }
                    else if (texture.Format == TextureFormat.Dxt1)
                    {
                        // First time we have a Polygon with Dxt1 in this node.
                        if (!isCurrentNodeWithOpaque)
                        {
                            currentOpaqueChunkLightsCount += GetLightsInBound(nodeBounds, currentOpaqueAlreadyAffectingLightIndices);
                            isCurrentNodeWithOpaque = true;
                        }
                        currentOpaqueChunkPolygons.PolygonIds.Add(polygonId);
                    }
                    // aka TextureFormat.R8G8B8A8 or anything else (e.g. DTX3, which will be changed to uncompressed R8G8B8A8 anyways)
                    else
                    {
                        // First time we have a Polygon with R8G8B8A8 in this node.
                        if (!isCurrentNodeWithTransparent)
                        {
                            currentTransparentChunkLightsCount += GetLightsInBound(nodeBounds, currentTransparentAlreadyAffectingLightIndices);
                            isCurrentNodeWithTransparent = true;
                        }
                        currentTransparentChunkPolygons.PolygonIds.Add(polygonId);
                    }
                }

                // Close node run
                {
                    if (currentWaterChunkLightsCount > Constants.MaxLightsPerWorldChunk || currentWaterChunkPolygons.PolygonIds.Count > _maxAmountOfPolygonsPerChunk)
                    {
                        if (currentWaterChunkPolygons.PolygonIds.Count > _maxAmountOfPolygonsPerChunk)
                            Logger.Log($"Polygon threshold of {_maxAmountOfPolygonsPerChunk} reached. Slicing Water chunk for world {mesh.Name} now.", LogCat.PreCaching);
                        
                        if (currentWaterChunkPolygons.PolygonIds.NotNullOrEmpty())
                            finalPolygonsWater.Add(currentWaterChunkPolygons);

                        currentWaterChunkPolygons = new WorldChunk();
                        currentWaterChunkLightsCount = 0;
                        currentWaterAlreadyAffectingLightIndices = new();
                    }
                    if (currentOpaqueChunkLightsCount > Constants.MaxLightsPerWorldChunk || currentOpaqueChunkPolygons.PolygonIds.Count > _maxAmountOfPolygonsPerChunk)
                    {
                        if (currentOpaqueChunkPolygons.PolygonIds.Count > _maxAmountOfPolygonsPerChunk)
                            Logger.Log($"Polygon threshold of {_maxAmountOfPolygonsPerChunk} reached. Slicing Opaque chunk for world {mesh.Name} now.", LogCat.PreCaching);

                        if (currentOpaqueChunkPolygons.PolygonIds.NotNullOrEmpty())
                            finalPolygonsOpaque.Add(currentOpaqueChunkPolygons);
                        
                        currentOpaqueChunkPolygons = new WorldChunk();
                        currentOpaqueChunkLightsCount = 0;
                        currentOpaqueAlreadyAffectingLightIndices = new();
                    }
                    if (currentTransparentChunkLightsCount > Constants.MaxLightsPerWorldChunk || currentTransparentChunkPolygons.PolygonIds.Count > _maxAmountOfPolygonsPerChunk)
                    {
                        if (currentWaterChunkPolygons.PolygonIds.Count > _maxAmountOfPolygonsPerChunk)
                            Logger.Log($"Polygon threshold of {_maxAmountOfPolygonsPerChunk} reached. Slicing Transparent chunk for world {mesh.Name} now.", LogCat.PreCaching);

                        if (currentTransparentChunkPolygons.PolygonIds.NotNullOrEmpty())
                            finalPolygonsTransparent.Add(currentTransparentChunkPolygons);
                        
                        currentTransparentChunkPolygons = new WorldChunk();
                        currentTransparentChunkLightsCount = 0;
                        currentTransparentAlreadyAffectingLightIndices = new();
                    }
                }
            }


            // Close whole node run
            {
                if (currentOpaqueChunkPolygons.PolygonIds.NotNullOrEmpty())
                {
                    finalPolygonsOpaque.Add(currentOpaqueChunkPolygons);
                }
                if (currentTransparentChunkPolygons.PolygonIds.NotNullOrEmpty())
                {
                    finalPolygonsTransparent.Add(currentTransparentChunkPolygons);
                }
                if (currentWaterChunkPolygons.PolygonIds.NotNullOrEmpty())
                {
                    finalPolygonsWater.Add(currentWaterChunkPolygons);
                }
            }

            MergedChunksByLights = new()
            {
                { TextureCacheService.TextureArrayTypes.Opaque, new List<WorldChunk>(finalPolygonsOpaque) },
                { TextureCacheService.TextureArrayTypes.Transparent, finalPolygonsTransparent },
                { TextureCacheService.TextureArrayTypes.Water, finalPolygonsWater }
            };
        }

        private int GetLightsInBound(Bounds nodeBounds, HashSet<int> excludeElements)
        {
            var returnCount = 0;
            for (var i = 0; i < _stationaryLightBounds.Count; i++)
            {
                if (excludeElements.Contains(i))
                {
                    continue;
                }

                if (_stationaryLightBounds[i].Intersects(nodeBounds))
                {
                    excludeElements.Add(i);
                    returnCount++;
                }
            }

            return returnCount;
        }
    }
}
