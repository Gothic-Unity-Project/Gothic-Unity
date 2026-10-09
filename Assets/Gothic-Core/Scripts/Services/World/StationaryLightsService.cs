using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.StaticCache;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    public class StationaryLightsService
    {
        [Inject] private readonly StaticCacheService _staticCacheService;
        [Inject] private readonly ConfigService _configService;

        /// <summary>
        /// Same as MAX_TOTAL_STATIONARY_LIGHTS in StationaryLighting.hlsl (1023 = Unity's cap, mobile 512). The global
        /// arrays always get this size: Unity fixes a global array's size the first time it's set.
        /// G2 alone caches ~700 fire lights - with 512, the ones above it never lit the world.
        /// </summary>
        private static int _maxTotalLights => Application.isMobilePlatform ? 512 : 1023;

        /// <summary>
        /// Grid size (m) for the light mapped world polygons, see SetLightMappedCells().
        /// </summary>
        private const float _cellSize = 2f;

        /// <summary>
        /// Lights farther than this from the light mapped polygons are ignored (m).
        /// </summary>
        private const float _maxLightMapSearch = 4f;

        /// <summary>
        /// A newly loaded light waits at least this long for its slot (s) - culling loads lights in bursts.
        /// </summary>
        private const float _poolMinRebalanceInterval = 0.5f;

        private HashSet<long> _lightMappedCells = new();
        private readonly Stack<int> _freeRuntimeSlots = new();
        private readonly HashSet<StationaryLight> _pooledLights = new();
        private readonly List<StationaryLight> _pooledSorted = new();
        private readonly HashSet<StationaryLight> _wantedLights = new();
        private bool _isPoolDirty;
        private float _nextPoolRebalance;
        private float _lastPoolRebalance;
        private int _poolPeak;
        private bool _isUploadDirty;
        
        private static readonly int _globalStationaryLightPositionsAndAttenuationShaderId =
            Shader.PropertyToID("_GlobalStationaryLightPositionsAndAttenuation");

        private static readonly int _globalStationaryLightColorsShaderId =
            Shader.PropertyToID("_GlobalStationaryLightColors");


        private readonly HashSet<MeshRenderer> _dirtiedMeshes = new();
        private readonly Dictionary<MeshRenderer, List<StationaryLight>> _lightsPerRenderer = new();

        public void LateUpdate()
        {
            if (Time.time >= _nextPoolRebalance ||
                (_isPoolDirty && Time.time >= _lastPoolRebalance + _poolMinRebalanceInterval))
                RebalancePool();

            // Lights loaded/unloaded this frame are uploaded at once (the whole arrays go to the GPU each time).
            if (_isUploadDirty)
            {
                _isUploadDirty = false;
                UploadGlobalLightArrays();
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
            var maxTotalLights = _maxTotalLights;
            // Pooled: the cached fire lights get slots like every other world light (closest first) instead of
            // keeping one for good - slot 0 stays the torch's/Light spell's.
            var cachedCount = _configService.Dev.EnablePooledStationaryLights
                ? 0
                : Mathf.Min(lights.Count, maxTotalLights - 1);
            if (lights.Count > cachedCount)
                Logger.LogWarning($"[StationaryLights] {lights.Count} cached lights - only {cachedCount} fit into the shader.",
                    LogCat.Vob);

            DynamicLightIndex = cachedCount;
            _lightPositionsAndAttenuation = new Vector4[maxTotalLights];
            _lightColors = new Vector4[maxTotalLights];

            // The slots after the cached lights and the dynamic slot host the static lights of light mapped areas.
            _freeRuntimeSlots.Clear();
            var poolEnd = Mathf.Min(maxTotalLights, DynamicLightIndex + 1 + _configService.Dev.StaticLightPoolSize);
            for (var i = poolEnd - 1; i > DynamicLightIndex; i--)
                _freeRuntimeSlots.Push(i);
            // Lights of the previous world are gone (OnDisable unregistered them) - start clean anyway.
            foreach (var light in _pooledLights)
                if (light != null && light.Index >= 0)
                    light.DetachSlot();
            _pooledLights.Clear();
            _poolPeak = 0;
            _isPoolDirty = true;

            Logger.Log($"[StationaryLights] cached={lights.Count} runtimeSlots={_freeRuntimeSlots.Count} " +
                       $"lightMappedCells={_lightMappedCells.Count}", LogCat.Vob);

            for (var i = 0; i < cachedCount; i++)
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

        public static long ToCell(Vector3 position)
        {
            var x = (long)Mathf.FloorToInt(position.x / _cellSize) & 0x1FFFFF;
            var y = (long)Mathf.FloorToInt(position.y / _cellSize) & 0x1FFFFF;
            var z = (long)Mathf.FloorToInt(position.z / _cellSize) & 0x1FFFFF;
            return (x << 42) | (y << 21) | z;
        }

        /// <summary>
        /// Cells (ToCell) holding light mapped world polygons. Set by the world mesh builder on every world load.
        /// </summary>
        public void SetLightMappedCells(HashSet<long> cells)
        {
            _lightMappedCells = cells ?? new HashSet<long>();
        }

        /// <summary>
        /// Gothic bakes a static light into the world's vertex light outdoors (we render that already) and into
        /// lightmaps indoors (caves, houses - we don't). Only lights next to light mapped polygons are worth a slot.
        /// </summary>
        public bool IsNearLightMappedPolygons(Vector3 position, float rangeMeters)
        {
            if (_lightMappedCells.Count == 0)
                return false;

            var reach = Mathf.Min(rangeMeters, _maxLightMapSearch);
            var steps = Mathf.CeilToInt(reach / _cellSize);
            for (var x = -steps; x <= steps; x++)
                for (var y = -steps; y <= steps; y++)
                    for (var z = -steps; z <= steps; z++)
                        if (_lightMappedCells.Contains(ToCell(position + new Vector3(x, y, z) * _cellSize)))
                            return true;
            return false;
        }

        /// <summary>
        /// A pooled light was loaded (StationaryLight.OnEnable). It gets a slot with the next rebalance.
        /// </summary>
        public void RegisterPooledLight(StationaryLight light)
        {
            if (_pooledLights.Add(light))
                _isPoolDirty = true;
        }

        /// <summary>
        /// A pooled light was unloaded (culling, world change): its slot is free right away.
        /// </summary>
        public void UnregisterPooledLight(StationaryLight light)
        {
            if (!_pooledLights.Remove(light))
                return;
            if (light.Index >= 0)
                FreeSlot(light);
            _isPoolDirty = true;
        }

        /// <summary>
        /// Like OpenGothic, every loaded world light could shine - but the shader has a fixed number of slots
        /// (1023, 512 on Quest). The lights closest to the camera (reach = distance - range) get them, the rest
        /// waits until the hero comes closer. Re-sorted every StationaryLightPoolRefreshSeconds.
        /// </summary>
        private void RebalancePool()
        {
            _isPoolDirty = false;
            _lastPoolRebalance = Time.time;
            _nextPoolRebalance = Time.time + _configService.Dev.StationaryLightPoolRefreshSeconds;
            if (DynamicLightIndex < 0 || _pooledLights.Count == 0)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;
            var camPos = cam.transform.position;
            var maxDistance = _configService.Dev.StationaryLightMaxDistance;
            var capacity = _freeRuntimeSlots.Count;

            _pooledSorted.Clear();
            foreach (var light in _pooledLights)
            {
                if (light == null)
                    continue;
                if (light.Index >= 0)
                    capacity++;
                if (Vector3.Distance(light.transform.position, camPos) - light.Range <= maxDistance)
                    _pooledSorted.Add(light);
            }
            _pooledSorted.Sort((a, b) =>
                (Vector3.Distance(a.transform.position, camPos) - a.Range).CompareTo(
                    Vector3.Distance(b.transform.position, camPos) - b.Range));

            _wantedLights.Clear();
            for (var i = 0; i < _pooledSorted.Count && i < capacity; i++)
                _wantedLights.Add(_pooledSorted[i]);

            // Free first (lights no longer among the closest), then hand the slots out.
            foreach (var light in _pooledLights)
                if (light != null && light.Index >= 0 && !_wantedLights.Contains(light))
                    FreeSlot(light);

            foreach (var light in _wantedLights)
            {
                if (light.Index >= 0 || _freeRuntimeSlots.Count == 0)
                    continue;

                var slot = _freeRuntimeSlots.Pop();
                var position = light.transform.position;
                _lightPositionsAndAttenuation[slot] = new Vector4(position.x, position.y, position.z,
                    1f / (light.Range * light.Range));
                _lightColors[slot] = light.RuntimeLinearColor;
                light.AttachSlot(slot);
                _isUploadDirty = true;
            }

            if (_wantedLights.Count > _poolPeak)
            {
                _poolPeak = _wantedLights.Count;
                if (_poolPeak % 50 == 0 || _pooledSorted.Count > capacity)
                    Logger.Log($"[StationaryLights] pool: {_wantedLights.Count} lit of {_pooledSorted.Count} in reach " +
                               $"({_pooledLights.Count} loaded, {capacity} slots)", LogCat.Vob);
            }
        }

        private void FreeSlot(StationaryLight light)
        {
            var slot = light.Index;
            light.DetachSlot();
            if (slot <= DynamicLightIndex || slot >= _lightPositionsAndAttenuation.Length)
                return;

            _lightPositionsAndAttenuation[slot] = Vector4.zero;
            _lightColors[slot] = Vector4.zero;
            _freeRuntimeSlots.Push(slot);
            _isUploadDirty = true;
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
