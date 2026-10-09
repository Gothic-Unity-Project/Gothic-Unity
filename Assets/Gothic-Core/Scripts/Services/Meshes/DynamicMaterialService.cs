using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Meshes
{
    /// <summary>
    /// Objects like items and interactables can alter their materials based on shader needs.
    /// e.g. hovering on an item (brightness change) and then grabbing it (transparency change).
    ///
    /// Unfortunately events in Unity can suffer race conditions (e.g. a hover is stopped after grab is started etc.)
    /// We therefore need to ensure, that two different shader changes will be reflected in the same material and not overwrite themselves.
    ///
    /// Every material slot of a renderer is handled: NPC bodies with armor (G2 robes/armors) have several materials
    /// on one SkinnedMeshRenderer - only the first one got highlighted before.
    /// </summary>
    public class DynamicMaterialService
    {
        private class CacheEntry
        {
            public List<Renderer> Renderers;
            public bool IsCurrentlyDynamic;
            public List<int> AlteredShaderProperties = new ();
            public List<Material[]> DefaultMaterials;
            // Same layout as DefaultMaterials. null = a shader we don't touch.
            public List<Material[]> DynamicMaterials;
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
        private readonly HashSet<string> _loggedUnknownShaders = new();


        public void SetDynamicValue(GameObject go, int shaderProperty, float shaderValue)
        {
            // An NPC's body can be rebuilt at runtime (armor change) - its old renderers are gone or new ones exist.
            if (_cache.TryGetValue(go, out var cached) && !cached.IsCurrentlyDynamic && IsOutdated(go, cached))
                _cache.Remove(go);

            if (!_cache.TryGetValue(go, out var entry))
            {
                CacheGameObject(go);
                entry = _cache[go];
            }

            // If it's the first time, alter GOs renderers to the dynamic ones.
            ActivateDynamicRenderers(entry);

            // Finally set the new values.
            SetValue(entry, shaderProperty, shaderValue);

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
            SetValue(entry, shaderProperty, shaderValue);
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

        private static bool IsOutdated(GameObject go, CacheEntry entry)
        {
            return entry.Renderers.Any(i => i == null) ||
                   go.GetComponentsInChildren<Renderer>().Length != entry.Renderers.Count;
        }

        /// <summary>
        /// Only our own dynamic material copies are changed - the default materials are shared with other objects.
        /// </summary>
        private static void SetValue(CacheEntry entry, int shaderProperty, float shaderValue)
        {
            foreach (var materials in entry.DynamicMaterials)
            {
                foreach (var material in materials)
                {
                    if (material != null)
                        material.SetFloat(shaderProperty, shaderValue);
                }
            }
        }

        private void CacheGameObject(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>().ToList();
            var defaultMaterials = renderers.Select(i => i.sharedMaterials).ToList();

            var dynamicMaterials = new List<Material[]>();
            foreach (var materials in defaultMaterials)
            {
                var dynamicSlots = new Material[materials.Length];
                for (var i = 0; i < materials.Length; i++)
                {
                    var mat = materials[i];
                    // We need to keep an empty entry for the array to backfill when hover is over.
                    if (mat == null)
                        continue;
                    if (!_dynamicShaderMap.TryGetValue(mat.shader.name, out var shaderMapEntry))
                    {
                        // Can't be highlighted - name it once, so missing shaders are easy to add to the map.
                        if (_loggedUnknownShaders.Add(mat.shader.name))
                            Logger.Log($"[DynamicMaterial] Shader '{mat.shader.name}' ({go.name}/{mat.name}) has no dynamic variant - not highlighted.", LogCat.Mesh);
                        continue;
                    }

                    dynamicSlots[i] = new Material(shaderMapEntry.dynamicShader)
                    {
                        mainTexture = mat.mainTexture,
                        renderQueue = shaderMapEntry.shaderType
                    };
                }

                dynamicMaterials.Add(dynamicSlots);
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
                if (entry.Renderers[i] == null)
                    continue;

                var dynamicSlots = entry.DynamicMaterials[i];

                // Only shaders we know get replaced, the other slots keep their material.
                if (dynamicSlots.All(m => m == null))
                    continue;

                var materials = (Material[])entry.DefaultMaterials[i].Clone();
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    if (dynamicSlots[slot] != null)
                        materials[slot] = dynamicSlots[slot];
                }

                entry.Renderers[i].sharedMaterials = materials;
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
                if (entry.Renderers[i] == null)
                    continue;

                // It's a shader we didn't touch.
                if (entry.DynamicMaterials[i].All(m => m == null))
                    continue;

                entry.Renderers[i].sharedMaterials = entry.DefaultMaterials[i];
            }

            entry.IsCurrentlyDynamic = false;
        }
    }
}
