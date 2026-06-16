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

        private Vector4[] _lightPositionsAndAttenuation;
        private Vector4[] _lightColors;
        private bool _lightColorsDirty;
        private bool _lightPositionsDirty;

        private readonly HashSet<MeshRenderer> _dirtiedMeshes = new();
        private readonly Dictionary<MeshRenderer, List<StationaryLight>> _lightsPerRenderer = new();

        public void LateUpdate()
        {
            // Upload animated light data to shader arrays.
            if (_lightColorsDirty)
            {
                Shader.SetGlobalVectorArray(_globalStationaryLightColorsShaderId, _lightColors);
                _lightColorsDirty = false;
            }

            if (_lightPositionsDirty)
            {
                Shader.SetGlobalVectorArray(
                    _globalStationaryLightPositionsAndAttenuationShaderId, _lightPositionsAndAttenuation);
                _lightPositionsDirty = false;
            }

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

        /// <summary>
        /// Set global Shader data when world is being loaded.
        /// </summary>
        public void InitStationaryLights()
        {
            var lights = _staticCacheService.LoadedStationaryLights.StationaryLights;

            _lightPositionsAndAttenuation = new Vector4[lights.Count];
            _lightColors = new Vector4[lights.Count];

            for (var i = 0; i < lights.Count; i++)
            {
                _lightPositionsAndAttenuation[i] = new Vector4(
                    lights[i].P.x, lights[i].P.y, lights[i].P.z,
                    1f / (lights[i].R * lights[i].R));
                _lightColors[i] = lights[i].Col;
            }

            _lightColorsDirty = false;
            _lightPositionsDirty = false;

            // Unity exception: Zero sized arrays aren't allowed for Shader values.
            if (_lightPositionsAndAttenuation.IsEmpty())
            {
                return;
            }

            Shader.SetGlobalVectorArray(_globalStationaryLightPositionsAndAttenuationShaderId,
                _lightPositionsAndAttenuation);
            Shader.SetGlobalVectorArray(_globalStationaryLightColorsShaderId, _lightColors);
        }

        /// <summary>
        /// Called each frame by animated StationaryLights to push their current color and range
        /// into the shader global arrays. The arrays are uploaded to the GPU once per frame in
        /// LateUpdate if dirty.
        /// </summary>
        public void NotifyLightChanged(StationaryLight light)
        {
            var idx = light.Index;
            if (idx < 0 || _lightColors == null || idx >= _lightColors.Length)
                return;

            var color = light.Color;
            _lightColors[idx] = new Vector4(color.r, color.g, color.b, 0);
            _lightColorsDirty = true;

            if (light.Range > 0f)
            {
                _lightPositionsAndAttenuation[idx].w = 1f / (light.Range * light.Range);
                _lightPositionsDirty = true;
            }
        }
    }
}
