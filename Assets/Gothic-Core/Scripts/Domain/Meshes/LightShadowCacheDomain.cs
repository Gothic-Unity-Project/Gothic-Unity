using System.Collections.Generic;
using System.Diagnostics;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Meshes
{
    /// <summary>
    /// Cached cube shadows of the pooled lamps and fires (PC, option "Shadows" on) - like GD3D11's point light shadows:
    /// a light's surroundings (world mesh, VOBs) are drawn once into its 6 faces, the distance to the nearest wall per
    /// texel. URP renders its shadow lights every frame (~40 at most) - farther lamps had none, the candles in a house
    /// lit its outside walls and the street. StationaryLightsService owns this, picks the lights and writes their
    /// slot into the shader arrays (StationaryLighting.hlsl samples _GothicLightShadowCache).
    /// Only static things cast: NPCs, items and the hero move - the nearest lights keep URP's shadows for them.
    /// A light is drawn again when the colliders around it changed (lazy loaded VOBs, a door's collider).
    /// </summary>
    public class LightShadowCacheDomain
    {
        /// <summary>
        /// Texels per face side. Same as GOTHIC_LIGHT_CACHE_FACE_SIZE in StationaryLighting.hlsl. 128: ~8 cm at 5 m -
        /// the nearest lamps have URP's sharper ones.
        /// </summary>
        public const int FaceSize = 128;

        /// <summary>
        /// Lights with a cached shadow (6 slices each, R16 float: 192 KB per light, 48 MB in all). The pool lights ~200
        /// in Khorinis within its 80 m.
        /// </summary>
        public const int Capacity = 256;

        // CPU time per frame for drawing queued lights (ms): walking through a town keeps refilling the queue - a fixed
        // 3 lights per frame cost ~1.2 ms of an 11 ms PCVR frame. At least one light per frame, at most
        // _maxRendersPerFrame (the GPU side isn't timed).
        private const double _renderBudgetMs = 0.75;
        private const int _maxRendersPerFrame = 4;
        private const int _checksPerFrame = 4;
        // Same as StationaryLightsService's shadow near plane: the lamp's cage casts its bars, not the flame's own mesh.
        private const float _nearPlane = 0.05f;
        private const string _passName = "GothicLightCache";
        private const int _maxColliders = 1024;

        private static readonly int _atlasShaderId = Shader.PropertyToID("_GothicLightShadowCache");
        private static readonly int _cacheLightShaderId = Shader.PropertyToID("_GothicLightCacheLight");

        // Face order and orientation as in StationaryLighting.hlsl (_GothicCacheFaceForward/_GothicCacheFaceUp).
        private static readonly Vector3[] _faceForward =
            { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private static readonly Vector3[] _faceUp =
            { Vector3.up, Vector3.up, Vector3.forward, Vector3.forward, Vector3.up, Vector3.up };

        private struct Draw
        {
            public MeshRenderer Renderer;
            public Material Material;
            public int Submesh;
            public int Pass;
            public Bounds Bounds;
        }

        private sealed class Entry
        {
            public int Key;
            public int Slot;
            public Vector3 Position;
            public float Range;
            public float Distance;
            public bool IsRendered;
            public bool IsQueued;
            public int ColliderCount;
        }

        /// <summary>
        /// A light the cache may take: Key identifies it (StationaryLightsService: the light's instance id), Distance
        /// sorts (closest first).
        /// </summary>
        public struct Candidate
        {
            public int Key;
            public Vector3 Position;
            public float Range;
            public float Distance;
        }

        private readonly Dictionary<int, Entry> _entries = new();
        private readonly Stack<int> _freeSlots = new();
        private readonly List<Entry> _renderQueue = new();
        private readonly List<Entry> _checkList = new();
        private int _checkCursor;
        private readonly HashSet<int> _wantedKeys = new();
        private readonly List<int> _removedKeys = new();
        private readonly Collider[] _colliders = new Collider[_maxColliders];
        private readonly HashSet<MeshRenderer> _casters = new();
        private readonly List<Material> _materials = new();
        // The light's draw calls, gathered once and tested per face (bounds and materials are native calls).
        private readonly List<Draw> _draws = new();
        // The world chunks' bounds - static, read once per world instead of once per light.
        private readonly List<(MeshRenderer Renderer, Bounds Bounds)> _chunkBounds = new();
        private readonly HashSet<int> _overflowLoggedKeys = new();
        private readonly Dictionary<Material, int> _passIndices = new();
        private readonly Plane[] _planes = new Plane[6];
        private readonly int _casterLayerMask =
            (1 << Constants.DefaultLayer) | (1 << Constants.IgnoreRaycastLayer) | (1 << Constants.VobMovableLayer);

        private RenderTexture _atlas;
        private RenderTexture _faceTarget;
        private Texture2DArray _emptyAtlas;
        private CommandBuffer _cmd;
        private int _renderedTotal;

        public bool IsEnabled => _atlas != null;

        /// <summary>
        /// The light's slot in the atlas once its shadow is drawn, -1 otherwise.
        /// </summary>
        public int GetSlot(int key) => _atlas != null && _entries.TryGetValue(key, out var entry) && entry.IsRendered
            ? entry.Slot
            : -1;

        /// <summary>
        /// Allocates (48 MB) or frees the atlas. Off: the shader gets a 1 slice dummy, every light loses its slot
        /// (the caller rewrites them).
        /// </summary>
        public void SetEnabled(bool isEnabled)
        {
            if (isEnabled == IsEnabled)
                return;

            Clear();
            if (isEnabled)
            {
                _atlas = new RenderTexture(FaceSize, FaceSize, GraphicsFormat.R16_SFloat, GraphicsFormat.None)
                {
                    name = "GothicLightShadowCache",
                    dimension = TextureDimension.Tex2DArray,
                    volumeDepth = Capacity * 6,
                    useMipMap = false,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _atlas.Create();
                _faceTarget = new RenderTexture(FaceSize, FaceSize, GraphicsFormat.R16_SFloat,
                    GraphicsFormat.D24_UNorm_S8_UInt)
                {
                    name = "GothicLightShadowCacheFace",
                    useMipMap = false,
                    filterMode = FilterMode.Point
                };
                _faceTarget.Create();
                _cmd ??= new CommandBuffer { name = "GothicLightShadowCache" };
                Shader.SetGlobalTexture(_atlasShaderId, _atlas);
                Logger.Log($"[LightShadowCache] on: {Capacity} lights, {FaceSize}px faces", LogCat.Mesh);
            }
            else
            {
                _atlas.Release();
                Object.Destroy(_atlas);
                _atlas = null;
                _faceTarget.Release();
                Object.Destroy(_faceTarget);
                _faceTarget = null;
                BindEmptyAtlas();
                Logger.Log("[LightShadowCache] off", LogCat.Mesh);
            }
        }

        /// <summary>
        /// Forgets every light (new world) - the atlas stays.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();
            _renderQueue.Clear();
            _checkList.Clear();
            _checkCursor = 0;
            _freeSlots.Clear();
            for (var i = Capacity - 1; i >= 0; i--)
                _freeSlots.Push(i);
            _passIndices.Clear();
            _chunkBounds.Clear();
            _overflowLoggedKeys.Clear();
            if (_atlas == null)
                BindEmptyAtlas();
        }

        /// <summary>
        /// The lights in reach changed (pool rebalance): the closest Capacity keep or get a slot, the others lose theirs.
        /// Keys whose slot is gone land in removedKeys.
        /// </summary>
        public void UpdateWanted(List<Candidate> candidates, List<int> removedKeys)
        {
            if (_atlas == null)
                return;

            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            _wantedKeys.Clear();
            for (var i = 0; i < candidates.Count && i < Capacity; i++)
                _wantedKeys.Add(candidates[i].Key);

            _removedKeys.Clear();
            foreach (var key in _entries.Keys)
                if (!_wantedKeys.Contains(key))
                    _removedKeys.Add(key);
            foreach (var key in _removedKeys)
            {
                var entry = _entries[key];
                _entries.Remove(key);
                _freeSlots.Push(entry.Slot);
                entry.IsQueued = false;
                if (entry.IsRendered)
                    removedKeys.Add(key);
            }
            _renderQueue.RemoveAll(entry => !_entries.ContainsKey(entry.Key));

            for (var i = 0; i < candidates.Count && i < Capacity; i++)
            {
                var candidate = candidates[i];
                if (!_entries.TryGetValue(candidate.Key, out var entry))
                {
                    if (_freeSlots.Count == 0)
                        break;
                    entry = new Entry { Key = candidate.Key, Slot = _freeSlots.Pop() };
                    _entries.Add(candidate.Key, entry);
                }
                var hasMoved = entry.Position != candidate.Position || !Mathf.Approximately(entry.Range, candidate.Range);
                entry.Position = candidate.Position;
                entry.Range = candidate.Range;
                entry.Distance = candidate.Distance;
                if ((!entry.IsRendered || hasMoved) && !entry.IsQueued)
                {
                    entry.IsQueued = true;
                    _renderQueue.Add(entry);
                }
            }
            _renderQueue.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            _checkList.Clear();
            _checkList.AddRange(_entries.Values);
            if (_checkCursor >= _checkList.Count)
                _checkCursor = 0;
        }

        /// <summary>
        /// Per frame: draws queued lights (closest first) within _renderBudgetMs; with nothing queued it checks a few
        /// drawn ones for new or gone colliders around them. Keys whose shadow appeared land in renderedKeys.
        /// </summary>
        public void Render(IReadOnlyCollection<MeshRenderer> worldChunks, List<int> renderedKeys)
        {
            if (_atlas == null)
                return;

            if (_renderQueue.Count == 0)
            {
                for (var i = 0; i < _checksPerFrame && _checkList.Count > 0; i++)
                {
                    _checkCursor = (_checkCursor + 1) % _checkList.Count;
                    var entry = _checkList[_checkCursor];
                    if (!entry.IsRendered || entry.IsQueued || !_entries.ContainsKey(entry.Key))
                        continue;
                    if (CountColliders(entry) == entry.ColliderCount)
                        continue;
                    entry.IsQueued = true;
                    _renderQueue.Add(entry);
                }
            }

            var rendered = 0;
            var start = Stopwatch.GetTimestamp();
            while (rendered < _maxRendersPerFrame && _renderQueue.Count > 0 &&
                   (rendered == 0 || ElapsedMs(start) < _renderBudgetMs))
            {
                var entry = _renderQueue[0];
                _renderQueue.RemoveAt(0);
                entry.IsQueued = false;
                if (!_entries.ContainsKey(entry.Key))
                    continue;

                RenderEntry(entry, worldChunks);
                rendered++;
                if (!entry.IsRendered)
                {
                    entry.IsRendered = true;
                    renderedKeys.Add(entry.Key);
                }
            }

            if (rendered > 0 && _renderQueue.Count == 0)
                Logger.Log($"[LightShadowCache] {_entries.Count} lights cached ({_renderedTotal} drawn so far)",
                    LogCat.Mesh);
        }

        private static double ElapsedMs(long start)
        {
            return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        private int CountColliders(Entry entry)
        {
            var count = Physics.OverlapSphereNonAlloc(entry.Position, entry.Range, _colliders, _casterLayerMask,
                QueryTriggerInteraction.Ignore);
            // A full buffer: some VOBs cast no cached shadow, and a change around the light goes unnoticed.
            if (count == _maxColliders && _overflowLoggedKeys.Add(entry.Key))
                Logger.LogWarning($"[LightShadowCache] {_maxColliders}+ colliders within {entry.Range:0.#} m of the " +
                                  $"light at {entry.Position} - some VOBs cast no cached shadow.", LogCat.Mesh);
            return count;
        }

        private void GatherCasters(Entry entry, IReadOnlyCollection<MeshRenderer> worldChunks)
        {
            _casters.Clear();
            var rangeSqr = entry.Range * entry.Range;
            // World chunks: by their bounds - a chunk of noCollDet polygons (bushes) has no collider.
            if (_chunkBounds.Count != worldChunks.Count)
            {
                _chunkBounds.Clear();
                foreach (var chunk in worldChunks)
                    if (chunk != null)
                        _chunkBounds.Add((chunk, chunk.bounds));
            }
            foreach (var (chunk, bounds) in _chunkBounds)
            {
                if (chunk != null && chunk.enabled && bounds.SqrDistance(entry.Position) <= rangeSqr)
                    _casters.Add(chunk);
            }

            var count = CountColliders(entry);
            entry.ColliderCount = count;
            for (var i = 0; i < count; i++)
            {
                if (_colliders[i].TryGetComponent<MeshRenderer>(out var renderer) &&
                    renderer.shadowCastingMode != ShadowCastingMode.Off && renderer.gameObject.activeInHierarchy)
                    _casters.Add(renderer);
            }

            _draws.Clear();
            foreach (var renderer in _casters)
            {
                var bounds = renderer.bounds;
                renderer.GetSharedMaterials(_materials);
                for (var submesh = 0; submesh < _materials.Count; submesh++)
                {
                    var material = _materials[submesh];
                    if (material == null)
                        continue;
                    var pass = GetPassIndex(material);
                    if (pass >= 0)
                        _draws.Add(new Draw
                        {
                            Renderer = renderer, Material = material, Submesh = submesh, Pass = pass, Bounds = bounds
                        });
                }
            }
        }

        private int GetPassIndex(Material material)
        {
            if (!_passIndices.TryGetValue(material, out var pass))
            {
                pass = material.FindPass(_passName);
                _passIndices.Add(material, pass);
            }
            return pass;
        }

        private void RenderEntry(Entry entry, IReadOnlyCollection<MeshRenderer> worldChunks)
        {
            GatherCasters(entry, worldChunks);

            var position = entry.Position;
            var range = Mathf.Max(entry.Range, _nearPlane * 2f);
            // Not run through GL.GetGPUProjectionMatrix on purpose: CommandBuffer.SetViewProjectionMatrices converts the
            // projection for the graphics API itself (clip z range, reversed z, the y flip when drawing into a render
            // texture) - URP draws its own shadow maps the same way (ShadowUtils.RenderShadowSlice). Converting it here
            // too would flip the faces on D3D/Vulkan. The texture rows then follow OpenGL's convention on every API,
            // which StationaryLightCachedShadow expects (checked: GPU readback vs. raycasts, 89-99% of directions).
            var projection = Matrix4x4.Perspective(90f, 1f, _nearPlane, range);
            _cmd.Clear();
            _cmd.SetGlobalVector(_cacheLightShaderId, new Vector4(position.x, position.y, position.z, 1f / range));

            for (var face = 0; face < 6; face++)
            {
                // Unity's camera convention: looks down -z in view space.
                var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) *
                           Matrix4x4.TRS(position, Quaternion.LookRotation(_faceForward[face], _faceUp[face]),
                               Vector3.one).inverse;
                GeometryUtility.CalculateFrustumPlanes(projection * view, _planes);

                _cmd.SetRenderTarget(_faceTarget);
                // 1 = nothing in the light's range.
                _cmd.ClearRenderTarget(true, true, Color.white);
                _cmd.SetViewProjectionMatrices(view, projection);
                foreach (var draw in _draws)
                {
                    if (draw.Renderer != null && GeometryUtility.TestPlanesAABB(_planes, draw.Bounds))
                        _cmd.DrawRenderer(draw.Renderer, draw.Material, draw.Submesh, draw.Pass);
                }
                _cmd.CopyTexture(_faceTarget, 0, 0, _atlas, entry.Slot * 6 + face, 0);
            }

            Graphics.ExecuteCommandBuffer(_cmd);
            _draws.Clear();
            _renderedTotal++;
        }

        private void BindEmptyAtlas()
        {
            if (_emptyAtlas == null)
            {
                _emptyAtlas = new Texture2DArray(1, 1, 1, TextureFormat.RHalf, false)
                {
                    name = "GothicLightShadowCacheEmpty"
                };
                _emptyAtlas.SetPixelData(new ushort[] { 0x3C00 }, 0, 0); // 1.0 as half
                _emptyAtlas.Apply(false, true);
            }
            Shader.SetGlobalTexture(_atlasShaderId, _emptyAtlas);
        }
    }
}
