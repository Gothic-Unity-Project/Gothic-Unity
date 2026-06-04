using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gothic.Core.Adapters.UI.LoadingBars;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using ZenKit;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.StaticCache
{
    public class VobBoundsCacheCreatorDomain
    {
        public Dictionary<string, Bounds> Bounds { get; } = new();

        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;


        public async Task CalculateVobBounds(List<IVirtualObject> vobs, int worldIndex)
        {
            var elementAmount = CalculateElementAmount(vobs);
            _loadingService.SetPhase($"{nameof(PreCachingLoadingBarHandler.ProgressTypesPerWorld.CalculateVobBounds)}_{worldIndex}", elementAmount);

            await CalculateVobBounds(vobs);
            _loadingService.FinalizePhase();
        }

        private int CalculateElementAmount(List<IVirtualObject> vobs)
        {
            var count = 0;
            foreach (var vob in vobs)
            {
                count++; // Count current vob
                count += CalculateElementAmount(vob.Children);
            }
            return count;
        }

        private async Task CalculateVobBounds(List<IVirtualObject> vobs)
        {
            foreach (var vob in vobs)
            {
                await _frameSkipperService.TrySkipToNextFrame();
                _loadingService.Tick();

                // We ignore oCItem for now as we will load them all in one afterward.
                // We also calculate bounds only for objects which are marked to be cached inside Constants.
                if (vob.Type == VirtualObjectType.oCItem || !Constants.StaticCacheVobTypes.Contains(vob.Type))
                {
                    // Check children
                    await CalculateVobBounds(vob.Children);
                    continue;
                }

                var visualName = vob.GetVisualName();

                // Already cached
                if (Bounds.ContainsKey(visualName))
                {
                    // Check children
                    await CalculateVobBounds(vob.Children);
                    continue;
                }

                Bounds boundingBox;
                switch (vob.Visual!.Type)
                {
                    case VisualType.Decal:
                        // FIXME - We can easily calculate bbox via position (0,0,0) + radius of decal.
                        var go = _meshService.CreateVobDecal(vob, (VisualDecal)vob.Visual);
                        boundingBox = CalculateBoundingBox(go);
                        Object.Destroy(go);
                        break;
                    case VisualType.ParticleEffect:
                        // FIXME - We can easily calculate bbox via position (0,0,0) + radius of emitting pfx.
                        var go2 = _meshService.CreateVobPfx(vob, parent: null);
                        boundingBox = CalculateBoundingBox(go2);
                        Object.Destroy(go2);
                        break;
                    case VisualType.Unknown:
                        continue; // Skip this Vob as it's only a placeholder with no data.
                    default:
                        // FIXME - e.g. OC_MOB_CAULDRON has vob-children like MAGICPOTIONSMOKE.pfx and TORCH.pfx inside.
                        //         The correct bounds would need to include these elements to be checked.
                        // We load BoundingBox directly now.
                        // go = CreateVobMesh(visualName);
                        boundingBox = CalculateBoundingBox(vob.Visual.Type, vob.Visual.Name);
                        break;
                }

                Bounds[visualName] = boundingBox;

                await CalculateVobBounds(vob.Children);
            }
        }

        /// <summary>
        /// As there might be VOBs which aren't in a new game, but when gamers load a save game,
        /// we need to calculate bounds for all! items.
        /// </summary>
        public async Task CalculateVobItemBounds()
        {
            var allItems = _gameStateService.GothicVm.GetInstanceSymbols("C_Item");

            _loadingService.SetPhase(nameof(PreCachingLoadingBarHandler.ProgressTypesGlobal.CalculateVobItemBounds), allItems.Count);
            
            foreach (var obj in allItems)
            {
                await _frameSkipperService.TrySkipToNextFrame();
                _loadingService.Tick();

                var item = _vmCacheService.TryGetItemData(obj.Name);

                if (item == null)
                    continue;

                var boundingBox = CalculateBoundingBox(VisualType.MultiResolutionMesh, item.Visual);

                // 99% of items are of mesh type MRM. For all the others, let's try other options.
                if (boundingBox == default)
                {
                    boundingBox = CalculateBoundingBox(VisualType.MorphMesh, item.Visual);

                    // In G1 it is only ITLSTORCHBURNING.ZEN --> RootObjects[0].Visual=ITLS_TORCHBURNED_01.3DS
                    if (item.Visual.EndsWith(".ZEN"))
                    {
                        var world = _resourceCacheService.TryGetWorld(item.Visual, _contextGameVersionService.Version);
                        if (world!.RootObjects.Count != 1)
                        {
                            Logger.LogError(
                                $"Bounds for {item.Visual} couldn't be calculated correctly as focussing on 1 G1 object as of now.",
                                LogCat.PreCaching);
                        }

                        // FIXME - This Vob has 2 children. One is a Pfx! We would need to calculate these bounds as well.
                        var firstWorldVisual = world.RootObjects.First().Visual;
                        boundingBox = CalculateBoundingBox(firstWorldVisual!.Type, firstWorldVisual.Name);
                    }
                }

                if (boundingBox == default)
                {
                    // Re-enable if you want to check which meshes couldn't be found.
                    // Logger.LogError($"ItemVisual {item.Visual} is neither .mrm, .zen, nor .mmb.");
                    continue;
                }

                Bounds[item.Visual] = boundingBox;
            }
            
            _loadingService.FinalizePhase();
        }

        /// <summary>
        /// Get mesh information from various sources of Vob. Similar to logic used in Gothic.Core.Domain.Vobs.VobInitializerDomain.CreateDefaultMesh()
        /// </summary>
        private Bounds CalculateBoundingBox(VisualType visualType, string visualName)
        {
            // MDL
            var mdl = _resourceCacheService.TryGetModel(visualName, false);
            if (mdl != null)
            {
                var bounds = new Bounds();

                var mdmFromMdl = mdl.Mesh;

                foreach (var mesh in mdmFromMdl.Meshes)
                {
                    bounds.Encapsulate(GetBoundsByOrientedBbox(mesh.Mesh.OrientedBoundingBox));
                }

                foreach (var attachment in mdmFromMdl.Attachments)
                {
                    bounds.Encapsulate(GetBoundsByOrientedBbox(attachment.Value.OrientedBoundingBox));
                }

                return bounds;
            }

            // MDH+MDM (without MDL as wrapper)
            var mdh = _resourceCacheService.TryGetModelHierarchy(visualName, false);
            var mdm = _resourceCacheService.TryGetModelMesh(visualName, false);
            if (mdh != null && mdm != null)
            {
                var bounds = new Bounds();
                foreach (var mesh in mdm!.Meshes)
                {
                    bounds.Encapsulate(GetBoundsByOrientedBbox(mesh.Mesh.OrientedBoundingBox));
                }

                foreach (var attachment in mdm.Attachments)
                {
                    bounds.Encapsulate(GetBoundsByOrientedBbox(attachment.Value.OrientedBoundingBox));
                }

                return bounds;
            }

            // MMB
            var mmb = _resourceCacheService.TryGetMorphMesh(visualName, false);
            if (mmb != null)
            {
                return GetBoundsByOrientedBbox(mmb.Mesh.OrientedBoundingBox);
            }
            
            // MRM
            var mrm = _resourceCacheService.TryGetMultiResolutionMesh(visualName, false);
            if (mrm != null)
            {
                return GetBoundsByOrientedBbox(mrm.OrientedBoundingBox);
            }
            
            // MSH - Final check if at least a mesh exists with the provided name.
            var msh = _resourceCacheService.TryGetMesh(visualName, false);
            if (msh != null)
            {
                return GetBoundsByOrientedBbox(msh.OrientedBoundingBox);
            }

            Logger.LogWarning($">{visualName}<'s has no mdl/mdh+mdm/mmb/mrm/msh.", LogCat.PreCaching);
            return default;
        }

        private enum AxesType
        {
            TypeUnknown,
            Type123 = 0b100010001,
            Type231 = 0b010001100,
            Type312 = 0b001100010
        }

        private Bounds GetBoundsByOrientedBbox(IOrientedBoundingBox bbox)
        {
            var center = new Vector3(bbox.Center.X / 100, bbox.Center.Y / 100, bbox.Center.Z / 100);
            AxesType axesType = AxesType.TypeUnknown;

            if (bbox.Axes.Item1.X == 1f && bbox.Axes.Item2.Y == 1f && bbox.Axes.Item3.Z == 1f)
            {
                axesType = AxesType.Type123;
            }
            else if (bbox.Axes.Item1.Y == 1f && bbox.Axes.Item2.Z == 1f && bbox.Axes.Item3.X == 1f)
            {
                axesType = AxesType.Type231;
            }
            else if (bbox.Axes.Item1.Z == 1f && bbox.Axes.Item2.X == 1f && bbox.Axes.Item3.Y == 1f)
            {
                axesType = AxesType.Type312;
            }

            Vector3 size;
            switch (axesType)
            {
                case AxesType.Type123:
                    size = new Vector3(bbox.HalfWidth.X / 100, bbox.HalfWidth.Y / 100, bbox.HalfWidth.Z / 100) * 2;
                    break;
                case AxesType.Type231:
                    size = new Vector3(bbox.HalfWidth.Z / 100, bbox.HalfWidth.X / 100, bbox.HalfWidth.Y / 100) * 2;
                    break;
                case AxesType.Type312:
                    size = new Vector3(bbox.HalfWidth.Y / 100, bbox.HalfWidth.Z / 100, bbox.HalfWidth.X / 100) * 2;
                    break;
                default:
                    Logger.LogError($"AxesType {bbox.Axes.Item1}/{bbox.Axes.Item2}/{bbox.Axes.Item3} not yet handled.",
                        LogCat.PreCaching);
                    return default;
            }

            return new Bounds(center, size);
        }


        [Obsolete("2024-12 - Used for Decals and Pfx but should be removed as the bbox can be created from ZenKit data. No need to create from GO.")]
        private Bounds CalculateBoundingBox(GameObject go)
        {
            try
            {
                // As we store renderer.bounds, we will get world space, but the object is at 0,0,0 and identity, we have local space again. ;-)
                if (go.TryGetComponent<ParticleSystemRenderer>(out var particleRenderer))
                {
                    return particleRenderer.bounds;
                }
                else if (go.TryGetComponent<DecalProjector>(out var decalProjector))
                {
                    return new Bounds(Vector3.zero, decalProjector.size);
                }
                else
                {
                    var meshFilters = go.GetComponentsInChildren<MeshFilter>();

                    switch (meshFilters.Length)
                    {
                        case 0:
                            return default;
                        case 1:
                            return meshFilters.First().sharedMesh.bounds;
                        default:
                            var finalBounds = new Bounds();
                            meshFilters.ForEach(i => finalBounds.Encapsulate(i.sharedMesh.bounds));
                            return finalBounds;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString(), LogCat.PreCaching);
                return default;
            }
        }
    }
}
