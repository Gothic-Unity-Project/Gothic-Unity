using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Logging;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    public class StationaryLightsService
    {
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

        /// <summary>
        /// The pooled lights are re-sorted by distance this often (s). Unloaded lights free their slot at once.
        /// </summary>
        private const float _poolRefreshInterval = 2f;

        /// <summary>
        /// The periodic re-sort is skipped while the camera moved less than this since the last one (m) - the order
        /// can't have changed (e.g. only VR head movement).
        /// </summary>
        private const float _poolMinCameraMove = 2f;

        /// <summary>
        /// A light with a slot counts this much closer (m): it keeps its slot until it's clearly farther than the
        /// others or the max distance, instead of flipping on/off at the cutoff with every refresh.
        /// </summary>
        private const float _poolHysteresis = 5f;

        /// <summary>
        /// Pooled lights farther than this (m, minus their range) don't get a slot. Not from Gothic/OpenGothic -
        /// VOB culling keeps lights loaded up to ~200 m (Large group, as a light's bounds are 2x its range), and the
        /// closest-first sort already gives the nearest lights priority on Quest's 512 slots.
        /// Kept as insurance until profiled on Quest: UpdateRenderer() writes only the first 16 lights of a renderer
        /// (in slot order, not by distance), so far lights could take entries from near ones on big world chunks.
        /// Tradeoff: at night, fires 80-200 m away are visible but don't light the ground around them.
        /// </summary>
        private const float _poolMaxDistance = 80f;

        private HashSet<long> _lightMappedCells = new();
        private readonly Stack<int> _freeRuntimeSlots = new();
        private readonly HashSet<StationaryLight> _pooledLights = new();
        // Pooled lights in reach with their reach (distance to the camera minus range), sorted closest first.
        private readonly List<(float Reach, StationaryLight Light)> _pooledSorted = new();
        private readonly HashSet<StationaryLight> _wantedLights = new();
        private bool _isPoolDirty;
        private float _nextPoolRebalance;
        private float _lastPoolRebalance;
        private Vector3 _lastPoolRebalanceCameraPosition;
        private int _poolPeak;
        private bool _isUploadDirty;
        
        private static readonly int _globalStationaryLightPositionsAndAttenuationShaderId =
            Shader.PropertyToID("_GlobalStationaryLightPositionsAndAttenuation");

        private static readonly int _globalStationaryLightColorsShaderId =
            Shader.PropertyToID("_GlobalStationaryLightColors");


        private readonly HashSet<MeshRenderer> _dirtiedMeshes = new();
        private readonly Dictionary<MeshRenderer, List<StationaryLight>> _lightsPerRenderer = new();
        // Reused by UpdateRenderer() - slot changes update hundreds of renderers at once (GC spikes on Quest).
        private readonly List<Material> _rendererMaterials = new();

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

            var nonAllocMaterials = _rendererMaterials;
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
        /// Set global Shader data when world is being loaded.
        /// </summary>
        public void InitStationaryLights()
        {
            var maxTotalLights = _maxTotalLights;
            _lightPositionsAndAttenuation = new Vector4[maxTotalLights];
            _lightColors = new Vector4[maxTotalLights];

            // Every slot hosts pooled lights (fires and static lights of light mapped areas), closest first.
            _freeRuntimeSlots.Clear();
            for (var i = maxTotalLights - 1; i >= 0; i--)
                _freeRuntimeSlots.Push(i);
            // Lights of the previous world are gone (OnDisable unregistered them) - start clean anyway.
            foreach (var light in _pooledLights)
                if (light != null && light.Index >= 0)
                    light.DetachSlot();
            _pooledLights.Clear();
            _poolPeak = 0;
            _isPoolDirty = true;

            Logger.Log($"[StationaryLights] slots={maxTotalLights} lightMappedCells={_lightMappedCells.Count}",
                LogCat.Vob);

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
        /// A point light that moves (the Light spell): it lights the world like the other lights, and NPCs and items
        /// through its Unity light. Move it with MoveLight, destroy its object to remove it.
        /// </summary>
        public GameObject CreateMovingLight(string name, Vector3 position, float range, Color color)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var light = go.AddComponent<StationaryLight>();
            light.Inject();
            light.Type = LightType.Point;
            light.Color = color;
            light.Range = range;
            light.Intensity = 1f;
            light.RuntimeLinearColor = color.linear;
            light.Init();
            return go;
        }

        /// <summary>
        /// A moving light keeps its slot, its position in the shader arrays follows and it lights the renderers around
        /// its new position.
        /// </summary>
        public void MoveLight(GameObject lightGo, Vector3 position)
        {
            if (lightGo == null || !lightGo.TryGetComponent<StationaryLight>(out var light))
                return;

            light.Relocate(position);
            _isPoolDirty = true;
            var slot = light.Index;
            if (slot < 0 || _lightPositionsAndAttenuation == null || slot >= _lightPositionsAndAttenuation.Length)
                return;

            _lightPositionsAndAttenuation[slot] = new Vector4(position.x, position.y, position.z,
                1f / (light.CachedRange * light.CachedRange));
            _isUploadDirty = true;
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
        /// waits until the hero comes closer. Re-sorted every _poolRefreshInterval.
        /// </summary>
        private void RebalancePool()
        {
            var isDirty = _isPoolDirty;
            _isPoolDirty = false;
            _lastPoolRebalance = Time.time;
            _nextPoolRebalance = Time.time + _poolRefreshInterval;
            if (_lightPositionsAndAttenuation == null || _pooledLights.Count == 0)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;
            var camPos = cam.transform.position;
            if (!isDirty && (camPos - _lastPoolRebalanceCameraPosition).sqrMagnitude <
                _poolMinCameraMove * _poolMinCameraMove)
                return;
            _lastPoolRebalanceCameraPosition = camPos;
            var capacity = _freeRuntimeSlots.Count;

            _pooledSorted.Clear();
            foreach (var light in _pooledLights)
            {
                if (light == null)
                    continue;
                var hasSlot = light.Index >= 0;
                if (hasSlot)
                    capacity++;
                // Reach is computed once per refresh, not per comparison. It subtracts the range, so plain
                // distances are needed (squared ones wouldn't sort the same).
                var reach = Vector3.Distance(light.CachedPosition, camPos) - light.CachedRange;
                if (hasSlot)
                    reach -= _poolHysteresis;
                if (reach <= _poolMaxDistance)
                    _pooledSorted.Add((reach, light));
            }
            _pooledSorted.Sort((a, b) => a.Reach.CompareTo(b.Reach));

            _wantedLights.Clear();
            for (var i = 0; i < _pooledSorted.Count && i < capacity; i++)
                _wantedLights.Add(_pooledSorted[i].Light);

            // Free first (lights no longer among the closest), then hand the slots out.
            foreach (var light in _pooledLights)
                if (light != null && light.Index >= 0 && !_wantedLights.Contains(light))
                    FreeSlot(light);

            foreach (var light in _wantedLights)
            {
                if (light.Index >= 0 || _freeRuntimeSlots.Count == 0)
                    continue;

                var slot = _freeRuntimeSlots.Pop();
                var position = light.CachedPosition;
                _lightPositionsAndAttenuation[slot] = new Vector4(position.x, position.y, position.z,
                    1f / (light.CachedRange * light.CachedRange));
                _lightColors[slot] = light.RuntimeLinearColor;
                light.AttachSlot(slot);
                _isUploadDirty = true;
            }

            if (_wantedLights.Count > _poolPeak)
            {
                _poolPeak = _wantedLights.Count;
                if (_poolPeak % 50 == 0 || _pooledSorted.Count > capacity)
                    Logger.Log($"[StationaryLights] pool: {_wantedLights.Count} lit of {_pooledSorted.Count} " +
                               $"in reach ({_pooledLights.Count} loaded, {capacity} slots)", LogCat.Vob);
            }
        }

        private void FreeSlot(StationaryLight light)
        {
            var slot = light.Index;
            light.DetachSlot();
            if (slot < 0 || slot >= _lightPositionsAndAttenuation.Length)
                return;

            _lightPositionsAndAttenuation[slot] = Vector4.zero;
            _lightColors[slot] = Vector4.zero;
            _freeRuntimeSlots.Push(slot);
            _isUploadDirty = true;
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
