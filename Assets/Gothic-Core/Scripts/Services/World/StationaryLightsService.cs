using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Domain.Meshes;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Extensions;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    public class StationaryLightsService
    {
        [Inject] private readonly ConfigService _configService;

        private readonly WorldLightmapDomain _lightmapDomain = new WorldLightmapDomain().Inject();

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

        /// <summary>
        /// Dynamic point light shadows (GD3D11-like): the lights closest to the camera (lamps, fires with a runtime slot)
        /// become shadow casting URP lights; the shaders light them through URP with their cube shadow and the pool skips
        /// them (w &lt; 0 in the slot). URP reaches every object in their range - the pool only the renderers a light
        /// gathered, Khorinis' market stayed dark under its lamps. Every one renders its surroundings 6 times, so only
        /// a few; baked lights (in the lightmaps) never - the walls would get them twice.
        /// </summary>
        // Option "Shadow range" (slider): from 4 lights within 20 m to 12 within 50 m. Every one draws its surroundings
        // 6 times per frame - with 40 the frame rate tanked.
        private const int _minShadowLights = 4;
        private const int _maxShadowLights = 12;
        private const float _minShadowLightDistance = 20f;
        private const float _maxShadowLightDistance = 50f;
        private float ShadowRange => Mathf.Clamp01(_configService.GetGothicOption(Constants.IniNames.ShadowRange,
            Constants.IniNames.ShadowRangeDefaultValue));
        private int ShadowLightCount => Mathf.RoundToInt(Mathf.Lerp(_minShadowLights, _maxShadowLights, ShadowRange));
        private float ShadowLightMaxDistance => Mathf.Lerp(_minShadowLightDistance, _maxShadowLightDistance, ShadowRange);
        // Geometry closer to the light than this casts no shadow: the lamp's cage casts its bars, not the flame's own
        // mesh around the light.
        private const float _shadowLightNearPlane = 0.05f;
        private readonly HashSet<StationaryLight> _shadowLights = new();
        // Their Unity lights - GothicShadowLightsFeature finds them among URP's visible lights every frame.
        private readonly HashSet<Light> _shadowUnityLights = new();

        public bool HasShadowLights => _shadowUnityLights.Count > 0;

        public bool IsShadowLight(Light light) => _shadowUnityLights.Contains(light);

        /// <summary>
        /// Only lights this close (m, minus their range) keep their Unity light enabled - URP takes 256 visible lights,
        /// the city has ~770: turning the head pushed a lamp out of them, its light and shadow vanished. The Unity
        /// light only lights objects/NPCs (and casts the shadow); the pool lights the world, also beyond this.
        /// </summary>
        private const float _unityLightMaxDistance = 40f;
        private readonly HashSet<StationaryLight> _wantedShadowLights = new();
        private readonly List<(float Distance, StationaryLight Light)> _shadowCandidates = new();

        private HashSet<long> _lightMappedCells = new();
        private readonly Stack<int> _freeRuntimeSlots = new();
        // Who drives the hero light now - the last ClaimHeroLight wins, only it may update or release the slot.
        private object _heroLightOwner;
        // The hero light's Unity light (torch, Light spell) - a dynamic shadow light while the option is on.
        private Light _heroUnityLight;
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

        // Lights a renderer's material gets: 16 in _StationaryLightIndices (plants), 16 more in _StationaryLightIndices2.
        private const int MaxAffectingLights = 16;

        private void UpdateRenderer(MeshRenderer renderer)
        {
            if (!renderer)
            {
                return;
            }

            var rendererLights = _lightsPerRenderer[renderer];
            // The shader takes 32 lights (16 on plants): the shadow casting ones first, then the closest. In slot order
            // a big world chunk (Khorinis' market) didn't get the lamps right above it.
            if (rendererLights.Count > MaxAffectingLights)
            {
                var center = renderer.bounds.center;
                rendererLights.Sort((a, b) =>
                {
                    var aShadow = _shadowLights.Contains(a);
                    if (aShadow != _shadowLights.Contains(b))
                        return aShadow ? -1 : 1;
                    return (a.CachedPosition - center).sqrMagnitude.CompareTo((b.CachedPosition - center).sqrMagnitude);
                });
            }

            var nonAllocMaterials = _rendererMaterials;
            var indicesMatrix = Matrix4x4.identity;
            GetOwnedMaterials(renderer, nonAllocMaterials);
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
        /// Index of the one reserved slot for the hero light - the light following the hero (the torch in their hand,
        /// the Light spell above them). Slot 0, the pool hands out the others. -1 until InitStationaryLights() has run.
        /// </summary>
        public int HeroLightIndex { get; private set; } = -1;

        /// <summary>
        /// The current world's light mapped polygons show Gothic's lightmaps (WorldMeshBuilder) - its static lights are in them already, they only light VOBs and NPCs.
        /// </summary>
        public bool HasWorldLightmaps { get; private set; }

        private static readonly int _worldLightmapsShaderId = Shader.PropertyToID("_WorldLightmaps");
        private static readonly int _worldLightmapBrightnessShaderId = Shader.PropertyToID("_WorldLightmapBrightness");
        // The shader's 1x1 white array without lightmaps - kept for the game's lifetime.
        private Texture2DArray _emptyLightmaps;
        // The current world's lightmap pages - destroyed with the next world or the main menu.
        private Texture2DArray _worldLightmaps;

        public StationaryLightsService()
        {
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(ClearWorldLightmaps);
            GlobalEventDispatcher.MainMenuSceneLoaded.AddListener(ClearWorldLightmaps);
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener((key, _) =>
            {
                if (key == Constants.IniNames.Shadows || key == Constants.IniNames.ShadowRange)
                    RefreshShadowLights();
            });
        }

        /// <summary>
        /// The world's lightmaps (DeveloperConfig.EnableWorldLightmaps) go into one texture array that Lit-World
        /// samples for the light mapped polygons. Returns their per-vertex uv data for WorldMeshBuilder, null without
        /// lightmaps - the shader keeps a 1x1 white array then.
        /// </summary>
        [CanBeNull]
        public WorldLightmapDomain.LightmapData PrepareWorldLightmaps(ZenKit.IMesh mesh)
        {
            ClearWorldLightmaps();
            Shader.SetGlobalFloat(_worldLightmapBrightnessShaderId, GetLightmapBrightness());

            if (!_configService.Dev.EnableWorldLightmaps)
                return null;
            var data = _lightmapDomain.PrepareLightmaps(mesh);
            if (data == null)
                return null;

            var pages = data.Pages;
            _worldLightmaps = new Texture2DArray(WorldLightmapDomain.PageSize, WorldLightmapDomain.PageSize,
                pages.Count, UnityEngine.TextureFormat.RGBA32, true, false)
            {
                name = "WorldLightmaps",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var slice = 0; slice < pages.Count; slice++)
                _worldLightmaps.SetPixelData(pages[slice], 0, slice);
            _worldLightmaps.Apply(true, true);

            Shader.SetGlobalTexture(_worldLightmapsShaderId, _worldLightmaps);
            HasWorldLightmaps = true;
            Logger.Log($"[Lightmaps] {data.Count} lightmaps on {pages.Count} pages", LogCat.Mesh);
            return data;
        }

        /// <summary>
        /// Frees the world's lightmap array (new world, main menu, a failed load) - the shader gets the white one.
        /// </summary>
        private void ClearWorldLightmaps()
        {
            if (_emptyLightmaps == null)
            {
                _emptyLightmaps = new Texture2DArray(1, 1, 1, UnityEngine.TextureFormat.RGBA32, false)
                {
                    name = "EmptyLightmaps"
                };
                _emptyLightmaps.SetPixelData(new byte[] { 255, 255, 255, 255 }, 0, 0);
                _emptyLightmaps.Apply(false, true);
            }
            if (_worldLightmaps != null)
            {
                UnityEngine.Object.Destroy(_worldLightmaps);
                _worldLightmaps = null;
            }
            Shader.SetGlobalTexture(_worldLightmapsShaderId, _emptyLightmaps);
            HasWorldLightmaps = false;
        }

        private readonly HashSet<MeshRenderer> _worldChunkRenderers = new();

        public void RegisterWorldChunk(MeshRenderer renderer)
        {
            _worldChunkRenderers.Add(renderer);
        }

        public bool IsWorldChunk(MeshRenderer renderer) => _worldChunkRenderers.Contains(renderer);

        public void ClearWorldChunks()
        {
            _worldChunkRenderers.Clear();
            _worldLitRenderers.Clear();
            _lightmapCells.Clear();
            _vobLightApplied = 0;
            _vobLightMissed = 0;
            _vobLightExamples = 0;
            _vobLightSkipLogs = 0;
        }

        private static readonly int _vobLightTintShaderId = Shader.PropertyToID("_VobLightTint");
        // VOB renderers lit by the lightmaps (ApplyWorldLight) - the static lights baked into them skip them too.
        private readonly HashSet<Renderer> _worldLitRenderers = new();

        public bool IsWorldLit(Renderer renderer) => _worldLitRenderers.Contains(renderer);
        // Lightmap light per cell (ToCell) of the light mapped world polygons, sampled at their centers by WorldMeshBuilder.
        private readonly Dictionary<long, Color> _lightmapCells = new();

        /// <summary>
        /// The lightmap light of the world's light mapped polygons per cell (rgb summed, w = polygon count).
        /// </summary>
        public void SetLightmapCells(Dictionary<long, Unity.Mathematics.float4> cells, bool isMostlyLightmapped)
        {
            _lightmapCells.Clear();
            var brightness = GetLightmapBrightness();
            _isMostlyLightmapped = isMostlyLightmapped;
            var total = new Vector3();
            var totalCount = 0f;
            foreach (var sum in cells.Values)
            {
                total += new Vector3(sum.x, sum.y, sum.z);
                totalCount += sum.w;
            }
            _averageLightmapLight = totalCount > 0
                ? new Color(total.x / totalCount * brightness, total.y / totalCount * brightness, total.z / totalCount * brightness)
                : Color.white;
            foreach (var (cell, sum) in cells)
            {
                if (sum.w > 0)
                    _lightmapCells[cell] = new Color(sum.x / sum.w * brightness, sum.y / sum.w * brightness,
                        sum.z / sum.w * brightness);
            }
            Logger.Log($"[VobWorldLight] {_lightmapCells.Count} light mapped cells", LogCat.Vob);
        }

        /// <summary>
        /// Gothic.ini's zVidBrightness (menu: Video > Brightness, 0..1, vanilla 0.5 = neutral) as a factor on the
        /// lightmap light, 0.25..4 (2^((v - 0.5) * 4)). The engine's tonemapping pass (OpenGothic Renderer::drawTonemapping)
        /// shifts the whole frame; this is the part of it we can reach without a post-process pass. Read when a world loads.
        /// </summary>
        public float GetLightmapBrightness()
        {
            var value = _configService.Gothic?.GetFloat(Constants.IniNames.VideoBrightness,
                Constants.IniNames.VideoBrightnessDefaultValue) ?? Constants.IniNames.VideoBrightnessDefaultValue;
            return Mathf.Pow(2f, (Mathf.Clamp01(value) - 0.5f) * 4f);
        }

        /// <summary>
        /// A VOB in a light mapped area (cave, mine, house) takes the lightmap light
        /// of the world around it - like the engine's VOB lighting from the static light. Stairs, stones and scaffolds in
        /// the Old Mine shone like the day next to the dark walls.
        /// </summary>
        public void ApplyWorldLight(GameObject vobGo)
        {
            if (vobGo == null)
                return;
            if (_lightmapCells.Count == 0)
            {
                LogVobWorldLightSkip(vobGo, "no light mapped cells in this world");
                return;
            }

            var renderers = vobGo.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                LogVobWorldLightSkip(vobGo, "no renderers yet");
                return;
            }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            var light = SampleWorldLight(bounds);
            LogVobWorldLight(vobGo, bounds, light);
            if (!light.HasValue)
                return;

            var tint = new Vector4(light.Value.r, light.Value.g, light.Value.b, 1f);
            foreach (var renderer in renderers)
            {
                if (renderer is MeshRenderer meshRenderer && IsWorldChunk(meshRenderer))
                    continue;
                // Into the renderer's own materials (UpdateRenderer writes its light indices there too), not a
                // MaterialPropertyBlock: the SRP batcher skips renderers with one, ~1900 VOBs in Khorinis were drawn
                // one by one in every pass (with the shadows: sun cascades, lamp cubes).
                GetOwnedMaterials(renderer, _rendererMaterials);
                foreach (var material in _rendererMaterials)
                    if (material.HasProperty(_vobLightTintShaderId))
                        material.SetVector(_vobLightTintShaderId, tint);
                _worldLitRenderers.Add(renderer);
            }
        }

        /// <summary>
        /// The renderer's materials this service writes per-renderer values into (light indices, world-light tint).
        /// Every VOB and chunk renderer has its own (mesh builders); the loading material is the one shared exception,
        /// left in place when a mesh failed to build - writing there would light every renderer still using it.
        /// </summary>
        private static void GetOwnedMaterials(Renderer renderer, List<Material> materials)
        {
            renderer.GetSharedMaterials(materials);
            // Backwards and without a lambda: called for hundreds of renderers at once (GC spikes on Quest).
            for (var i = materials.Count - 1; i >= 0; i--)
            {
                if (materials[i] == null || materials[i] == Constants.LoadingMaterial)
                    materials.RemoveAt(i);
            }
        }

        // Diagnostics: how many VOBs got the world light, with a few examples (VOBs in the Old Mine stayed bright).
        private int _vobLightApplied;
        private int _vobLightMissed;
        private int _vobLightExamples;
        private const int _vobLightMaxExamples = 8;
        private const int _vobLightSummaryEvery = 100;

        private int _vobLightSkipLogs;

        private void LogVobWorldLightSkip(GameObject vobGo, string reason)
        {
            if (_vobLightSkipLogs >= _vobLightMaxExamples)
                return;
            _vobLightSkipLogs++;
            Logger.Log($"[VobWorldLight] {vobGo.name} skipped: {reason}", LogCat.Vob);
        }

        private void LogVobWorldLight(GameObject vobGo, Bounds bounds, Color? light)
        {
            if (light.HasValue)
                _vobLightApplied++;
            else
                _vobLightMissed++;
            if (_vobLightExamples < _vobLightMaxExamples)
            {
                _vobLightExamples++;
                Logger.Log($"[VobWorldLight] {vobGo.name} at {bounds.center} size {bounds.size}: " +
                           $"{(light.HasValue ? light.Value.ToString() : "no light mapped cell")}", LogCat.Vob);
            }
            if ((_vobLightApplied + _vobLightMissed) % _vobLightSummaryEvery == 0)
                Logger.Log($"[VobWorldLight] {_vobLightApplied} VOBs tinted, {_vobLightMissed} without light mapped cells",
                    LogCat.Vob);
        }

        /// <summary>
        /// The average lightmap light of the light mapped cells around the bounds' center and bottom (3x3x3 cells each),
        /// null if there are none - outdoors the sun lights the VOB.
        /// </summary>
        public Color? SampleWorldLight(Bounds bounds)
        {
            // Ring by ring (2 m cells) up to _vobLightMaxRings - a bridge over a chasm has its walls a few meters away.
            var probes = new[] { bounds.center, new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) };
            var visited = new HashSet<long>();
            for (var ring = 1; ring <= _vobLightMaxRings; ring++)
            {
                var sum = new Vector3();
                var count = 0;
                foreach (var probe in probes)
                {
                    for (var x = -ring; x <= ring; x++)
                    for (var y = -ring; y <= ring; y++)
                    for (var z = -ring; z <= ring; z++)
                    {
                        var cell = ToCell(probe + new Vector3(x, y, z) * _cellSize);
                        if (!visited.Add(cell) || !_lightmapCells.TryGetValue(cell, out var light))
                            continue;
                        sum += new Vector3(light.r, light.g, light.b);
                        count++;
                    }
                }
                if (count > 0)
                    return new Color(sum.x / count, sum.y / count, sum.z / count);
            }

            // A world lit by lightmaps (mines): nothing near - its average light rather than the full day.
            return _isMostlyLightmapped ? _averageLightmapLight : null;
        }

        private const int _vobLightMaxRings = 4;
        private bool _isMostlyLightmapped;
        private Color _averageLightmapLight;

        /// <summary>
        /// How often a moving light (the torch in the hand, the Light spell) is moved in the world shader - Immersion
        /// setting "Light refresh": Low 0.3 s (a slide show when walking), Medium twice, High four times as often.
        /// </summary>
        public float MovingLightRefreshSeconds => _configService.Gothic.GetInt(Constants.IniNames.LightRefresh, 0) switch
        {
            1 => 0.15f,
            2 => 0.075f,
            _ => 0.3f
        };

        /// <summary>
        /// Set global Shader data when world is being loaded.
        /// </summary>
        public void InitStationaryLights()
        {
            var maxTotalLights = _maxTotalLights;
            _lightPositionsAndAttenuation = new Vector4[maxTotalLights];
            _lightColors = new Vector4[maxTotalLights];

            // Slot 0 is the hero light (torch in hand, Light spell), every other slot hosts pooled lights (fires and
            // static lights of light mapped areas), closest first.
            HeroLightIndex = 0;
            _heroLightOwner = null;
            _freeRuntimeSlots.Clear();
            for (var i = maxTotalLights - 1; i > HeroLightIndex; i--)
                _freeRuntimeSlots.Push(i);
            // Lights of the previous world are gone (OnDisable unregistered them) - start clean anyway.
            foreach (var light in _pooledLights)
                if (light != null && light.Index >= 0)
                    light.DetachSlot();
            _pooledLights.Clear();
            _shadowLights.Clear();
            _shadowUnityLights.Clear();
            _dynamicShadowLights.Clear();
            _dynamicShadowNearPlanes.Clear();
            _heroUnityLight = null;
            _poolPeak = 0;
            _isPoolDirty = true;

            Logger.Log($"[StationaryLights] slots={maxTotalLights} lightMappedCells={_lightMappedCells.Count}",
                LogCat.Vob);

            UploadGlobalLightArrays();
        }

        /// <summary>
        /// The owner (a burning torch, a Light spell) takes the hero light - there's one slot, so the previous owner's
        /// light goes dark (its updates are ignored from now on, IsHeroLightOwner tells it).
        /// </summary>
        public void ClaimHeroLight(object owner, Light unityLight = null)
        {
            if (_heroUnityLight != null && _heroUnityLight != unityLight)
                RemoveDynamicShadowLight(_heroUnityLight);
            _heroUnityLight = unityLight;
            // The torch's own stick and the hand would hide the flame - geometry closer than this casts nothing.
            if (unityLight != null)
                AddDynamicShadowLight(unityLight, _heroShadowNearPlane);
            if (_heroLightOwner != null && !ReferenceEquals(_heroLightOwner, owner))
                Logger.Log($"[StationaryLights] Hero light taken over by {owner} from {_heroLightOwner}", LogCat.Vob);
            _heroLightOwner = owner;
        }

        public bool IsHeroLightOwner(object owner) => owner != null && ReferenceEquals(_heroLightOwner, owner);

        /// <summary>
        /// Moves the hero light and re-uploads the global shader arrays - only for its owner, false otherwise.
        /// Call this sparingly (e.g. a few times a second while a torch burns), not every frame -
        /// Shader.SetGlobalVectorArray re-uploads the whole array every time.
        /// </summary>
        public bool UpdateHeroLight(object owner, Vector3 worldPosition, float rangeMeters, Color linearColor)
        {
            if (!IsHeroLightOwner(owner))
                return false;
            if (HeroLightIndex < 0 || _lightPositionsAndAttenuation == null)
                return true; // world not loaded yet

            // w < 0: a shadow casting light, the shaders light it through URP (GothicShadowedLights).
            var attenuation = 1f / (rangeMeters * rangeMeters);
            var isShadowed = _heroUnityLight != null && _shadowUnityLights.Contains(_heroUnityLight);
            _lightPositionsAndAttenuation[HeroLightIndex] = new Vector4(
                worldPosition.x, worldPosition.y, worldPosition.z, isShadowed ? -attenuation : attenuation);
            // Alpha 0: the hero's light reaches indoor and outdoor surfaces.
            linearColor.a = 0f;
            _lightColors[HeroLightIndex] = linearColor;
            UploadGlobalLightArrays();
            return true;
        }

        /// <summary>
        /// The owner's light is gone - the slot is zeroed out so no stale position/color lingers. A former owner
        /// can't switch off the light that took the slot over.
        /// </summary>
        public void ReleaseHeroLight(object owner)
        {
            if (!IsHeroLightOwner(owner))
                return;
            _heroLightOwner = null;
            if (_heroUnityLight != null)
                RemoveDynamicShadowLight(_heroUnityLight);
            _heroUnityLight = null;
            if (HeroLightIndex < 0 || _lightPositionsAndAttenuation == null)
                return;

            _lightPositionsAndAttenuation[HeroLightIndex] = Vector4.zero;
            _lightColors[HeroLightIndex] = Vector4.zero;
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
        /// <summary>
        /// Light mapped polygons in the 2 m column right above (2 to 8 m up): a roof or a ceiling - the position is
        /// indoors (house, cave, sewer). Walls next to it don't count (Khorinis' street lamps stand next to light mapped
        /// walls, the sewers' lamps below the open market).
        /// </summary>
        public bool HasLightMappedCeiling(Vector3 position)
        {
            for (var up = 1; up <= 4; up++)
            {
                if (_lightMappedCells.Contains(ToCell(position + Vector3.up * (up * _cellSize))))
                    return true;
            }
            return false;
        }

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
            if (light.IsOutdoor)
                light.Intensity = _outdoorLightFactor;
            if (_pooledLights.Add(light))
                _isPoolDirty = true;
        }

        /// <summary>
        /// A pooled light changed (StationaryLight.UpdateRuntime) - its slot gets
        /// the new values with the next upload; without a slot it competes with the others at the next rebalance.
        /// </summary>
        public void UpdatePooledLight(StationaryLight light)
        {
            if (light == null || _lightPositionsAndAttenuation == null)
                return;
            if (light.Index <= HeroLightIndex || light.Index >= _lightPositionsAndAttenuation.Length)
            {
                _isPoolDirty = true;
                return;
            }

            _lightPositionsAndAttenuation[light.Index] = GetSlotPositionAndAttenuation(light);
            _lightColors[light.Index] = GetSlotColor(light);
            _isUploadDirty = true;
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
                light.AttachSlot(slot);
                _lightPositionsAndAttenuation[slot] = GetSlotPositionAndAttenuation(light);
                _lightColors[slot] = GetSlotColor(light);
                _isUploadDirty = true;
            }

            UpdateShadowLights(camPos);
            UpdateUnityLights(camPos);

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
            if (_shadowLights.Remove(light))
                SetLightShadow(light, false);
            var slot = light.Index;
            light.DetachSlot();
            if (slot <= HeroLightIndex || slot >= _lightPositionsAndAttenuation.Length)
                return;

            _lightPositionsAndAttenuation[slot] = Vector4.zero;
            _lightColors[slot] = Vector4.zero;
            _freeRuntimeSlots.Push(slot);
            _isUploadDirty = true;
        }

        private void UpdateUnityLights(Vector3 cameraPosition)
        {
            foreach (var light in _pooledLights)
            {
                if (light == null)
                    continue;
                var isNear = Vector3.Distance(light.CachedPosition, cameraPosition) - light.CachedRange <=
                             _unityLightMaxDistance;
                // A shadow casting light farther away needs its Unity light for the shadow.
                light.SetUnityLightEnabled(isNear || _shadowLights.Contains(light));
            }
        }

        /// <summary>
        /// Outdoor lamps and fires by day: Gothic's lights burn all day, but its bright vertex light hid them. Ours are
        /// stronger (StationaryLightFalloff) and the sun's shadow leaves 40% - lamps glowed red on shaded walls at noon.
        /// Night (sky brightness up to _lampFullBrightness) full, noon _lampDayFactor.
        /// </summary>
        private const float _lampDayFactor = 0.2f;
        private const float _lampFullBrightness = 0.35f;
        private float _outdoorLightFactor = 1f;

        /// <summary>
        /// SkyService: the world's day light changed (its brightness 0..1, indoor worlds dark). Outdoor lights follow.
        /// </summary>
        public void SetDaylight(float brightness)
        {
            var factor = Mathf.Lerp(1f, _lampDayFactor, Mathf.InverseLerp(_lampFullBrightness, 1f, brightness));
            if (Mathf.Abs(factor - _outdoorLightFactor) < 0.02f)
                return;

            _outdoorLightFactor = factor;
            if (_lightColors == null)
                return;
            foreach (var light in _pooledLights)
            {
                if (light == null || !light.IsOutdoor)
                    continue;
                // The Unity light lights objects/NPCs and the shadow casting lamps' pools.
                light.Intensity = factor;
                if (light.Index > HeroLightIndex && light.Index < _lightColors.Length)
                {
                    _lightColors[light.Index] = GetSlotColor(light);
                    _isUploadDirty = true;
                }
            }
        }

        /// <summary>
        /// The slot's color; its alpha marks an indoor light (StationaryLighting.hlsl: no light on outdoor surfaces).
        /// </summary>
        private Color GetSlotColor(StationaryLight light)
        {
            var color = light.IsOutdoor ? light.RuntimeLinearColor * _outdoorLightFactor : light.RuntimeLinearColor;
            color.a = light.IsIndoor ? 1f : 0f;
            return color;
        }

        /// <summary>
        /// Position and 1/range² of a slot - negative for a shadow casting light (the shaders look up its shadow).
        /// </summary>
        private Vector4 GetSlotPositionAndAttenuation(StationaryLight light)
        {
            var position = light.CachedPosition;
            var attenuation = 1f / Mathf.Max(0.01f, light.CachedRange * light.CachedRange);
            return new Vector4(position.x, position.y, position.z,
                _shadowLights.Contains(light) ? -attenuation : attenuation);
        }

        /// <summary>
        /// The _maxShadowLights pooled lights closest to the camera cast shadows (option "Shadows" not off), the rest
        /// not. Runs with the pool's rebalance (camera moved 2 m or every 2 s).
        /// </summary>
        private void UpdateShadowLights(Vector3 cameraPosition)
        {
            _shadowUnityLights.RemoveWhere(light => light == null);
            _dynamicShadowLights.RemoveAll(light => light == null);
            _shadowCandidates.Clear();
            _wantedShadowLights.Clear();
            var maxDistance = ShadowLightMaxDistance;
            var maxCount = ShadowLightCount;
            if (_configService.Gothic.GetInt(Constants.IniNames.Shadows, Constants.IniNames.ShadowsDefaultValue) > 0)
            {
                foreach (var light in _pooledLights)
                {
                    if (light == null || light.Index <= HeroLightIndex || light.IsBakedIntoWorld ||
                        !light.isActiveAndEnabled)
                        continue;
                    var distance = Vector3.Distance(light.CachedPosition, cameraPosition);
                    if (distance - light.CachedRange > maxDistance)
                        continue;
                    // Spell effects (summoning, fire rain) before lamps - they're short and right next to the hero.
                    _shadowCandidates.Add((light.IsSpellLight ? distance - maxDistance : distance, light));
                }
                _shadowCandidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                for (var i = 0; i < _shadowCandidates.Count && i < maxCount; i++)
                    _wantedShadowLights.Add(_shadowCandidates[i].Light);
            }

            _shadowCandidates.Clear();
            foreach (var light in _shadowLights)
                if (light == null || !_wantedShadowLights.Contains(light))
                    _shadowCandidates.Add((0f, light));
            foreach (var (_, light) in _shadowCandidates)
            {
                _shadowLights.Remove(light);
                if (light != null)
                    SetLightShadow(light, false);
            }

            foreach (var light in _wantedShadowLights)
                if (_shadowLights.Add(light))
                    SetLightShadow(light, true);
        }

        /// <summary>
        /// Option "Shadows" changed: the shadow lights follow at once (off: none).
        /// </summary>
        public void RefreshShadowLights()
        {
            RefreshDynamicShadowLights();
            var cam = Camera.main;
            if (cam == null || _lightPositionsAndAttenuation == null)
                return;
            foreach (var light in _shadowLights)
                if (light != null)
                    SetLightShadow(light, true);
            UpdateShadowLights(cam.transform.position);
        }

        /// <summary>
        /// Moving lights that cast shadows: the hero's torch / Light spell (ClaimHeroLight) and spell projectiles
        /// (VRSpellThrow). URP renders their cube shadow every frame; the shaders light the world with them through
        /// URP (GothicShadowLightsFeature) - before, a projectile's light only reached NPCs and objects.
        /// Up to _maxDynamicShadowLights at once, more stay plain lights.
        /// </summary>
        private const int _maxDynamicShadowLights = 4;
        private const float _heroShadowNearPlane = 0.2f;
        private const float _dynamicShadowNearPlane = 0.15f;
        private readonly List<Light> _dynamicShadowLights = new();
        private readonly Dictionary<Light, float> _dynamicShadowNearPlanes = new();

        public void AddDynamicShadowLight(Light light, float nearPlane = _dynamicShadowNearPlane)
        {
            if (light == null || _dynamicShadowLights.Contains(light))
                return;
            _dynamicShadowLights.RemoveAll(other => other == null);
            if (_dynamicShadowLights.Count >= _maxDynamicShadowLights)
                return;

            _dynamicShadowLights.Add(light);
            _dynamicShadowNearPlanes[light] = nearPlane;
            ApplyDynamicShadow(light);
        }

        public void RemoveDynamicShadowLight(Light light)
        {
            if (light == null || !_dynamicShadowLights.Remove(light))
                return;
            _dynamicShadowNearPlanes.Remove(light);
            _shadowUnityLights.Remove(light);
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
        }

        private void RefreshDynamicShadowLights()
        {
            _dynamicShadowLights.RemoveAll(light => light == null);
            foreach (var light in _dynamicShadowLights)
                ApplyDynamicShadow(light);
        }

        private void ApplyDynamicShadow(Light light)
        {
            var option = _configService.Gothic.GetInt(Constants.IniNames.Shadows,
                Constants.IniNames.ShadowsDefaultValue);
            var isOn = option > 0;
            light.shadows = !isOn ? LightShadows.None : option == 1 ? LightShadows.Hard : LightShadows.Soft;
            light.renderMode = isOn ? LightRenderMode.ForcePixel : LightRenderMode.Auto;
            light.shadowNearPlane = _dynamicShadowNearPlanes.TryGetValue(light, out var nearPlane)
                ? nearPlane
                : _dynamicShadowNearPlane;
            if (isOn)
            {
                light.GetUniversalAdditionalLightData().softShadowQuality =
                    option == 2 ? SoftShadowQuality.Low : SoftShadowQuality.High;
                _shadowUnityLights.Add(light);
            }
            else
            {
                _shadowUnityLights.Remove(light);
            }
        }

        private void SetLightShadow(StationaryLight light, bool isOn)
        {
            var unityLight = light.GetComponent<Light>();
            if (unityLight == null)
                return;

            var option = _configService.Gothic.GetInt(Constants.IniNames.Shadows,
                Constants.IniNames.ShadowsDefaultValue);
            unityLight.shadows = !isOn ? LightShadows.None : option == 1 ? LightShadows.Hard : LightShadows.Soft;
            if (isOn)
                _shadowUnityLights.Add(unityLight);
            else
                _shadowUnityLights.Remove(unityLight);
            // URP's 8 lights per object: the shadow casting ones first.
            unityLight.renderMode = isOn ? LightRenderMode.ForcePixel : LightRenderMode.Auto;
            unityLight.shadowNearPlane = _shadowLightNearPlane;
            if (isOn)
            {
                var lightData = unityLight.GetUniversalAdditionalLightData();
                lightData.softShadowQuality = option == 2 ? SoftShadowQuality.Low : SoftShadowQuality.High;
            }

            if (light.Index > HeroLightIndex && light.Index < _lightPositionsAndAttenuation.Length)
            {
                _lightPositionsAndAttenuation[light.Index] = GetSlotPositionAndAttenuation(light);
                _isUploadDirty = true;
            }
            // Its renderers re-sort their lights (UpdateRenderer: shadow casting ones first).
            foreach (var renderer in light.AffectedRenderers)
                if (renderer != null)
                    _dirtiedMeshes.Add(renderer);
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
