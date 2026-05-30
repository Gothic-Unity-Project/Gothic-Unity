using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Extensions;
using GUZ.Core.Const;
using UnityEngine;
using UnityEngine.Rendering;

namespace GUZ.Core.Services.Meshes
{
    /// <summary>
    /// Objects like items and interactables can alter their materials based on shader needs.
    /// e.g. hovering on an item (brightness change) and then grabbing it (transparency change).
    ///
    /// Unfortunately events in Unity can suffer race conditions (e.g. a hover is stopped after grab is started etc.)
    /// We therefore need to ensure, that two different shader changes will be reflected in the same material and not overwrite themselves.
    /// </summary>
    public class DynamicMaterialService
    {
        private class CacheEntry
        {
            public List<Renderer> Renderers;
            public bool IsCurrentlyDynamic;
            public List<int> AlteredShaderProperties = new ();
            public List<Material> DefaultMaterials;
            public List<Material> DynamicMaterials;
        }


        private Dictionary<string, (Shader dynamicShader, int shaderType)> _dynamicShaderMap = new ()
        {
            { Constants.ShaderSingleMeshLitName, new (Constants.ShaderSingleMeshLitDynamic, Constants.ShaderTypeTransparent) },
            // Basically: Leave the default shader (no special logic inside code needed with this handling.
            { Constants.ShaderWorldLitName, new (Constants.ShaderWorldLit, Constants.ShaderTypeDefault) },
            { Constants.ShaderLitAlphaToCoverageName, new (Constants.ShaderLitAlphaToCoverageDynamic, (int)RenderQueue.AlphaTest) }
        };

        // Some objects (like NPCs) have multiple meshes. We therefore add all self+children renderers/materials.
        private Dictionary<GameObject, CacheEntry> _cache = new();


        public void SetDynamicValue(GameObject go, int shaderProperty, float shaderValue)
        {
            if (!_cache.TryGetValue(go, out var entry))
            {
                CacheGameObject(go);
                entry = _cache[go];
            }

            // If it's the first time, alter GOs renderers to the dynamic ones.
            ActivateDynamicRenderers(entry);

            // Finally set the new values.
            entry.Renderers.ForEach(i => i.sharedMaterial.SetFloat(shaderProperty, shaderValue));

            // And we add the property to the list of "changed" properties.
            entry.AlteredShaderProperties.Add(shaderProperty);
        }

        /// <summary>
        /// Reset dynamic shader values.
        /// If all shader values are reverted, then the default materials will be re-applied.
        ///
        /// Hint: shaderProperties[].shaderValue need to be default ones to reset.
        /// </summary>
        public void ResetDynamicValue(GameObject go, int shaderProperty, float shaderValue)
        {
            if (!_cache.TryGetValue(go, out var entry))
            {
                return;
            }

            // Reset values
            entry.Renderers.ForEach(i => i.sharedMaterial.SetFloat(shaderProperty, shaderValue));
            entry.AlteredShaderProperties.Remove(shaderProperty);

            if (entry.AlteredShaderProperties.IsEmpty())
            {
                DeactivateDynamicRenderers(entry);
            }
        }

        /// <summary>
        /// e.g. called whenever a GameObject is culled out.
        /// </summary>
        public void ResetAllDynamicValues(GameObject go)
        {
            RemoveFromCache(go);
        }

        private void CacheGameObject(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>().ToList();
            var defaultMaterials = go.GetComponentsInChildren<Renderer>()
                .Select(i => i.sharedMaterial)
                .ToList();

            var dynamicMaterials = new List<Material>();
            foreach (var mat in defaultMaterials)
            {
                if (!_dynamicShaderMap.TryGetValue(mat.shader.name, out var shaderMapEntry))
                {
                    dynamicMaterials.Add(null); // We need to set an empty entry for the array to backfill when hover is over.
                    continue;
                }
                
                var newMaterial = new Material(shaderMapEntry.dynamicShader)
                {
                    mainTexture = mat.mainTexture,
                    renderQueue = shaderMapEntry.shaderType
                };

                dynamicMaterials.Add(newMaterial);
            }

            _cache.Add(go, new()
            {
                Renderers = renderers,
                DefaultMaterials = defaultMaterials,
                DynamicMaterials = dynamicMaterials
            });
        }

        private void RemoveFromCache(GameObject go)
        {
            if (!_cache.TryGetValue(go, out var entry))
            {
                return;
            }

            DeactivateDynamicRenderers(entry);

            _cache.Remove(go);
        }

        /// <summary>
        /// Change all materials and shaders of renderers.
        /// </summary>
        private void ActivateDynamicRenderers(CacheEntry entry)
        {
            if (entry.IsCurrentlyDynamic)
            {
                return;
            }

            for (var i = 0; i < entry.Renderers.Count; i++)
            {
                var dynamicMaterial = entry.DynamicMaterials[i];

                // It's a shader we didn't touch.
                if (dynamicMaterial == null)
                    continue;

                entry.Renderers[i].sharedMaterial = dynamicMaterial;
            }

            entry.IsCurrentlyDynamic = true;
        }

        private void DeactivateDynamicRenderers(CacheEntry entry)
        {
            if (!entry.IsCurrentlyDynamic)
            {
                return;
            }

            for (var i = 0; i < entry.Renderers.Count; i++)
            {
                var defaultMaterial = entry.DefaultMaterials[i];

                // It's a shader we didn't touch.
                if (defaultMaterial == null)
                    continue;
                
                entry.Renderers[i].sharedMaterial = defaultMaterial;
            }

            entry.IsCurrentlyDynamic = false;
        }
    }
}
