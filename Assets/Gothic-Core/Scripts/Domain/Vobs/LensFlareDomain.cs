using System.Collections.Generic;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Vobs
{
    /// <summary>
    /// A soft glow around a light - one additive billboard that faces the camera, keeps a capped size on screen and
    /// fades out close to the camera (all set on the zCVobLensFlare prefab). The texture comes from Gothic's files and
    /// is set here. Used by the torch in the hero's hand. Gothic's lens flare VOBs could use it too (not yet).
    /// </summary>
    public class LensFlareDomain
    {
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly TextureService _textureService;

        // One material per texture, shared by all glows with it.
        private readonly Dictionary<string, Material> _materials = new();
        private bool _isPrefabMissing;

        /// <summary>
        /// A glow at the parent's origin, in the color of its light.
        /// </summary>
        /// <returns>Null if the prefab is missing (warned once).</returns>
        [CanBeNull]
        public GameObject Create(Transform parent, string textureName, Color color)
        {
            if (_isPrefabMissing || parent == null)
                return null;

            var glow = _resourceCacheService.TryGetPrefabObject(PrefabType.VobLensFlare, "Glow", parent.gameObject);
            var particles = glow != null ? glow.GetComponent<ParticleSystem>() : null;
            var particleRenderer = glow != null ? glow.GetComponent<ParticleSystemRenderer>() : null;
            if (particles == null || particleRenderer == null)
            {
                _isPrefabMissing = true;
                if (glow != null)
                    Object.Destroy(glow);
                Logger.LogWarning($"[LensFlare] No prefab with a particle system at {PrefabType.VobLensFlare.Path()} " +
                                  "- lights have no glow.", LogCat.Vob);
                return null;
            }

            particleRenderer.sharedMaterial = GetMaterial(textureName, particleRenderer.sharedMaterial);

            // The prefab's one particle is born with this color: start it anew.
            var main = particles.main;
            main.startColor = color;
            particles.Clear(true);
            particles.Play(true);
            return glow;
        }

        /// <param name="template">The prefab's material (additive, camera fading) - its shader variant is in the
        /// build.</param>
        private Material GetMaterial(string textureName, [CanBeNull] Material template)
        {
            if (_materials.TryGetValue(textureName, out var material) && material != null)
                return material;

            if (template != null)
                material = new Material(template);
            else
            {
                // Like the particle effects' materials (VobPfxMeshBuilder).
                material = new Material(Constants.ShaderUnlitParticles);
                material.ToAdditiveMode();
                material.SetInt("_Cull", (int)CullMode.Off);
            }

            material.name = $"Glow {textureName}";
            _textureService.SetTexture(textureName, material);
            if (material.mainTexture == null)
                Logger.LogWarning($"[LensFlare] Texture {textureName} not found - the glow is a plain square.",
                    LogCat.Vob);

            _materials[textureName] = material;
            return material;
        }
    }
}
