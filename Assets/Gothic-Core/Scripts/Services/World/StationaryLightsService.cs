using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Services.StaticCache;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Services.World
{
    public class StationaryLightsService
    {
        [Inject] private readonly StaticCacheService _staticCacheService;
        
        private static readonly int _globalStationaryLightPositionsAndAttenuationShaderId =
            Shader.PropertyToID("_GlobalStationaryLightPositionsAndAttenuation");

        private static readonly int _globalStationaryLightColorsShaderId =
            Shader.PropertyToID("_GlobalStationaryLightColors");


        private readonly HashSet<MeshRenderer> _dirtiedMeshes = new();
        private readonly Dictionary<MeshRenderer, List<StationaryLight>> _lightsPerRenderer = new();

        public void LateUpdate()
        {
            // Update the renderer once for all updated lights.
            if (_dirtiedMeshes.Count <= 0)
            {
                return;
            }

            foreach (var renderer in _dirtiedMeshes)
            {
                UpdateRenderer(renderer);
            }

            _dirtiedMeshes.Clear();
        }

        public void AddLightOnRenderer(StationaryLight light, MeshRenderer renderer)
        {
            if (!_lightsPerRenderer.ContainsKey(renderer))
            {
                _lightsPerRenderer.Add(renderer, new List<StationaryLight>());
            }

            _lightsPerRenderer[renderer].Add(light);
            _dirtiedMeshes.Add(renderer);
        }

        public void RemoveLightOnRenderer(StationaryLight light, MeshRenderer renderer)
        {
            if (!_lightsPerRenderer.ContainsKey(renderer))
            {
                return;
            }

            try
            {
                _lightsPerRenderer[renderer].Remove(light);
                _dirtiedMeshes.Add(renderer);
            }
            catch
            {
                //Logger.LogError($"[{nameof(StationaryLight)}] Light {name} wasn't part of {_affectedRenderers[i].name}'s lights on disable. This is unexpected.");
            }
        }

        private void UpdateRenderer(MeshRenderer renderer)
        {
            if (!renderer)
            {
                return;
            }

            var rendererLights = _lightsPerRenderer[renderer];

            var nonAllocMaterials = new List<Material>();
            var indicesMatrix = Matrix4x4.identity;
            renderer.GetSharedMaterials(nonAllocMaterials);
            for (var i = 0; i < Mathf.Min(16, rendererLights.Count); i++)
            {
                indicesMatrix[i / 4, i % 4] = rendererLights[i].Index;
            }

            for (var i = 0; i < nonAllocMaterials.Count; i++)
            {
                if (nonAllocMaterials[i])
                {
                    nonAllocMaterials[i].SetMatrix(StationaryLight.StationaryLightIndicesShaderId, indicesMatrix);
                    nonAllocMaterials[i].SetInt(StationaryLight.StationaryLightCountShaderId,
                        rendererLights.Count);
                }
            }

            // TODO - The current pre-caching logic is stopping at exactly 16 lights. Therefore this logic would normally never been called.
            if (rendererLights.Count >= 16)
            {
                for (var i = 0; i < Mathf.Min(16, rendererLights.Count - 16); i++)
                {
                    indicesMatrix[i / 4, i % 4] = rendererLights[i + 16].Index;
                }

                for (var i = 0; i < nonAllocMaterials.Count; i++)
                {
                    if (nonAllocMaterials[i])
                    {
                        nonAllocMaterials[i].SetMatrix(StationaryLight.StationaryLightIndices2ShaderId, indicesMatrix);
                    }
                }
            }
        }

        private Vector4[] _lightPositionsAndAttenuation;
        private Vector4[] _lightColors;

        /// <summary>
        /// Index of the one reserved slot for a light that moves at runtime (e.g. the player's Light
        /// spell) — everything else in these arrays is baked once from the world file and never moves.
        /// -1 until InitStationaryLights() has run.
        /// </summary>
        public int DynamicLightIndex { get; private set; } = -1;

        /// <summary>
        /// Set global Shader data when world is being loaded.
        /// </summary>
        public void InitStationaryLights()
        {
            var lights = _staticCacheService.LoadedStationaryLights.StationaryLights;

            DynamicLightIndex = lights.Count;
            _lightPositionsAndAttenuation = new Vector4[lights.Count + 1];
            _lightColors = new Vector4[lights.Count + 1];

            for (var i = 0; i < lights.Count; i++)
            {
                _lightPositionsAndAttenuation[i] = new Vector4(
                    lights[i].P.x, lights[i].P.y, lights[i].P.z,
                    1f / (lights[i].R * lights[i].R));
                _lightColors[i] = lights[i].Col;
            }
            // Dynamic slot starts inert (zero attenuation/color) until UpdateDynamicLight() moves it.
            _lightPositionsAndAttenuation[DynamicLightIndex] = Vector4.zero;
            _lightColors[DynamicLightIndex] = Vector4.zero;

            UploadGlobalLightArrays();
        }

        /// <summary>
        /// Moves the single reserved dynamic light slot and re-uploads the global shader arrays.
        /// Call this sparingly (e.g. a few times a second while a light-spell-like effect is active),
        /// not every frame — Shader.SetGlobalVectorArray re-uploads the whole array every time, so the
        /// cost scales with total static light count, not just the dynamic one.
        /// </summary>
        public void UpdateDynamicLight(Vector3 worldPosition, float rangeMeters, Color linearColor)
        {
            if (DynamicLightIndex < 0) return; // world not loaded yet

            _lightPositionsAndAttenuation[DynamicLightIndex] = new Vector4(
                worldPosition.x, worldPosition.y, worldPosition.z, 1f / (rangeMeters * rangeMeters));
            _lightColors[DynamicLightIndex] = linearColor;
            UploadGlobalLightArrays();
        }

        /// <summary>
        /// Zeroes the dynamic slot out so no stale position/color lingers once the effect ends.
        /// </summary>
        public void ClearDynamicLight()
        {
            if (DynamicLightIndex < 0) return;

            _lightPositionsAndAttenuation[DynamicLightIndex] = Vector4.zero;
            _lightColors[DynamicLightIndex] = Vector4.zero;
            UploadGlobalLightArrays();
        }

        private void UploadGlobalLightArrays()
        {
            // Unity exception: Zero sized arrays aren't allowed for Shader values.
            if (_lightPositionsAndAttenuation.IsEmpty())
            {
                return;
            }

            Shader.SetGlobalVectorArray(_globalStationaryLightPositionsAndAttenuationShaderId,
                _lightPositionsAndAttenuation);
            Shader.SetGlobalVectorArray(_globalStationaryLightColorsShaderId, _lightColors);
        }
    }
}
