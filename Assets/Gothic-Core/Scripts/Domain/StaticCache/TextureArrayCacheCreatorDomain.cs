using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GUZ.Core.Adapters.UI.LoadingBars;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.StaticCache;
using MyBox;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = GUZ.Core.Logging.Logger;
using TextureFormat = UnityEngine.TextureFormat;

namespace GUZ.Core.Domain.StaticCache
{
    public class TextureArrayCacheCreatorDomain
    {
        public Dictionary<string, StaticCacheService.TextureInfo> TextureArrayInformation { get; } = new();

        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        
        /// <summary>
        /// Load all materials from world mesh and assign textures to texture array accordingly.
        ///
        /// We cache all texture information. In G1.world.zen, there are about 25 which aren't used in normal mesh (maybe in portals only?)
        /// But for sake of simplicity, we use them all.
        /// </summary>
        public async Task CalculateTextureArrayInformation(IMesh worldMesh, int worldIndex)
        {
            _loadingService.SetPhase(
                $"{nameof(PreCachingLoadingBarHandler.ProgressTypesPerWorld.CalculateTextureArrayInformationMesh)}_{worldIndex}",
                worldMesh.MaterialCount);
            
            foreach (var material in worldMesh.Materials)
            {
                if (TextureArrayInformation.ContainsKey(material.Texture))
                {
                    continue;
                }

                AddTextureToCache(material.Group, material.Texture);

                _loadingService.Tick();
                await _frameSkipperService.TrySkipToNextFrame();
            }

            _loadingService.FinalizePhase();
        }

        public async Task CalculateTextureArrayInformation(List<IVirtualObject> vobs, int worldIndex)
        {
            var elementAmount = CalculateElementAmount(vobs);
            _loadingService.SetPhase($"{nameof(PreCachingLoadingBarHandler.ProgressTypesPerWorld.CalculateTextureArrayInformationVobs)}_{worldIndex}", elementAmount);
            
            await CalculateTextureArrayInformation(vobs);
            _loadingService.FinalizePhase();
        }
        
        private int CalculateElementAmount(List<IVirtualObject> vobs)
        {
            var count = 0;
            foreach (var vob in vobs)
            {
                count++; // We count each element as we update potentially with each FrameSkipper call, which is unaffected if it's a light or sth. else.
                count += CalculateElementAmount(vob.Children);
            }
            return count;
        }
        
        private async Task CalculateTextureArrayInformation(List<IVirtualObject> vobs)
        {
            foreach (var vob in vobs)
            {
                _loadingService.Tick();
                await _frameSkipperService.TrySkipToNextFrame();

                // We ignore oCItem for now as we will load them all in one afterward.
                // We also calculate bounds only for objects which are marked to be cached inside Constants.
                if (vob.Type == VirtualObjectType.oCItem || !Constants.StaticCacheVobTypes.Contains(vob.Type))
                {
                    // Check children
                    await CalculateTextureArrayInformation(vob.Children);
                    continue;
                }

                switch (vob.Visual!.Type)
                {
                    case VisualType.Mesh:
                    case VisualType.Model:
                    case VisualType.MorphMesh:
                    case VisualType.MultiResolutionMesh:
                        AddTexInfoForSingleVob(vob);
                        break;

                    // TODO - Should Decals and ParticleEffects also leverage the texture array?
                    case VisualType.Decal:
                    case VisualType.ParticleEffect:
                    default:
                        break;
                }

                // Recursive
                await CalculateTextureArrayInformation(vob.Children);
            }
        }

        /// <summary>
        /// As there might be VOBs which aren't in a new game, but when gamers load a save game,
        /// we need to calculate bounds for all! items.
        /// </summary>
        public async Task CalculateItemTextureArrayInformation()
        {
            var allItems = _gameStateService.GothicVm.GetInstanceSymbols("C_Item");

            _loadingService.SetPhase(nameof(PreCachingLoadingBarHandler.ProgressTypesGlobal.CalculateItemTextureArrayInformation), allItems.Count);
            
            foreach (var obj in allItems)
            {
                await _frameSkipperService.TrySkipToNextFrame();
                _loadingService.Tick();
                
                var item = _vmCacheService.TryGetItemData(obj.Name);

                if (item == null)
                {
                    continue;
                }

                AddTexInfoForItem(item);
            }
            
            _loadingService.FinalizePhase();
        }

        /// <summary>
        /// Get mesh information from various sources of Vob. Similar to logic used in GUZ.Core.Domain.Vobs.VobInitializerDomain.CreateDefaultMesh()
        /// </summary>
        private void AddTexInfoForSingleVob(IVirtualObject vob)
        {
            var meshName = vob.GetVisualName();
            
            // MDL
            var mdl = _resourceCacheService.TryGetModel(meshName, false);
            if (mdl != null)
            {
                mdl.Mesh.Meshes.ForEach(mesh => mesh.Mesh.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture)));
                mdl.Mesh.Attachments.ForEach(mesh => mesh.Value.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture)));
                return;
            }

            // MDH+MDM (without MDL as wrapper)
            var mdh = _resourceCacheService.TryGetModelHierarchy(meshName, false);
            var mdm = _resourceCacheService.TryGetModelMesh(meshName, false);
            if (mdh != null && mdm != null)
            {
                mdm.Meshes.ForEach(mesh => mesh.Mesh.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture)));
                mdm.Attachments.ForEach(mesh => mesh.Value.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture)));
                return;
            }

            // MMB
            var mmb = _resourceCacheService.TryGetMorphMesh(meshName, false);
            if (mmb != null)
            {
                mmb.Mesh.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture));
                return;
            }

            // MRM
            var mrm = _resourceCacheService.TryGetMultiResolutionMesh(meshName, false);
            if (mrm != null)
            {
                mrm.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture));
                return;
            }
            
            // MSH - Final check if at least a mesh exists with the provided name.
            var msh = _resourceCacheService.TryGetMesh(meshName, false);
            if (msh != null)
            {
                msh.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture));
                return;
            }

            Logger.LogWarning($">{meshName}<'s has no mdl/mdh+mdm/mmb/mrm.", LogCat.PreCaching);
        }

        private void AddTextureToCache(MaterialGroup group, string textureName)
        {
            // Already cached.
            if (TextureArrayInformation.ContainsKey(textureName))
            {
                return;
            }

            var texture = _resourceCacheService.TryGetTexture(textureName);

            if (texture == null)
            {
                return;
            }

            var unityTextureFormat = texture.Format.AsUnityTextureFormat();

            if (unityTextureFormat != TextureFormat.DXT1 && unityTextureFormat != TextureFormat.RGBA32)
            {
                Logger.LogError("Only DXT1 and RGBA32 textures are supported for texture arrays as of now!", LogCat.PreCaching);
            }

            var textureArrayType = TextureCacheService.TextureArrayTypes.Unknown;

            // Water is separate as we use a different shader.
            // TODO - Do we need to check for different TextureFormats as well?
            if (group == MaterialGroup.Water)
            {
                textureArrayType = TextureCacheService.TextureArrayTypes.Water;
            }
            // DXT1 can be opaque
            else if (unityTextureFormat == TextureFormat.DXT1)
            {
                textureArrayType = TextureCacheService.TextureArrayTypes.Opaque;
            }
            // RGBA32 is transparent
            else if (unityTextureFormat == TextureFormat.RGBA32)
            {
                textureArrayType = TextureCacheService.TextureArrayTypes.Transparent;
            }
            else
            {
                Logger.LogError($"TextureFormat={unityTextureFormat} + MaterialGroup={group} isn't handled for TextureArray so far.", LogCat.PreCaching);
            }

            var animationTextures = CalculateAnimationTextures(textureName);

            // TryAdd is used to ignore duplicates.
            TextureArrayInformation.TryAdd(textureName,
                new StaticCacheService.TextureInfo(textureArrayType, Math.Max(texture.Width, texture.Height), animationTextures.Count));

            // If the texture is an "animated one", we also need to add the animation textures. During runtime, water will iterate the z-index of TextureArray to loop through these elements.
            foreach (var animationTexture in animationTextures)
            {
                TextureArrayInformation.Add(animationTexture.Key,
                    new StaticCacheService.TextureInfo(textureArrayType, Math.Max(animationTexture.Value.Width, animationTexture.Value.Height), 0));
            }
        }

        /// <summary>
        /// If texture name contains _A0, then it is the start of an animated texture.
        /// We can fetch the corresponding animations and return them to be included as next elements inside Texture array.
        /// </summary>
        private Dictionary<string, ITexture> CalculateAnimationTextures(string textureName)
        {
            var textures = new Dictionary<string, ITexture>();
            if (!textureName.ContainsIgnoreCase("_A0"))
            {
                return textures;
            }

            for (var id = 1; ; id++)
            {
                // Replace the frame number in the key with the current id
                var frameKey = Regex.Replace(textureName, "_[Aa]0", $"_A{id}");
                var zkTex = _resourceCacheService.TryGetTexture(frameKey);

                if (zkTex == null)
                {
                    break;
                }

                textures.Add(frameKey.ToUpper(), zkTex);
            }

            return textures;
        }

        private void AddTexInfoForItem(ItemInstance item)
        {
            // MDL
            var mdl = _resourceCacheService.TryGetModel(item.Visual, false);
            if (mdl != null)
            {
                mdl.Mesh.Meshes.ForEach(mesh => mesh.Mesh.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture)));
                mdl.Mesh.Attachments.ForEach(mesh => mesh.Value.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture)));
                return;
            }

            // MDH+MDM (without MDL as wrapper)
            var mdh = _resourceCacheService.TryGetModelHierarchy(item.Visual, false);
            var mdm = _resourceCacheService.TryGetModelMesh(item.Visual, false);
            if (mdh != null && mdm != null)
            {
                mdm.Meshes.ForEach(mesh => mesh.Mesh.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture)));
                mdm.Attachments.ForEach(mesh => mesh.Value.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture)));
                return;
            }

            // MMB
            var mmb = _resourceCacheService.TryGetMorphMesh(item.Visual, false);
            if (mmb != null)
            {
                mmb.Mesh.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture));
                return;
            }

            // MRM
            var mrm = _resourceCacheService.TryGetMultiResolutionMesh(item.Visual, false);
            if (mrm != null)
            {
                mrm.Materials.ForEach(material => AddTextureToCache(material.Group, material.Texture));
                return;
            }

            Logger.LogWarning($">{item.Visual}<'s has no mdl/mdh+mdm/mmb/mrm.", LogCat.PreCaching);
        }
    }
}
