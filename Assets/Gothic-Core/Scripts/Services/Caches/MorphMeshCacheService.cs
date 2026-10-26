using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Caches
{
    /// <summary>
    /// We need to store various mappings and vertex data for MorphMeshes to work properly.
    /// We therefore use this separate cache class to ease reading.
    /// </summary>
    public class MorphMeshCacheService
    {
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        
        /// <summary>
        /// Vertex mapping is used for Mesh Morphing as we need to map Unity data back to ZenKit data for updates.
        ///
        /// In Unity we don't reuse vertices and always create a new one for another polygon (triangle).
        /// But when we re-map IMorphAnimation samples/vertices later on, we need to know the original vertexId from ZenKit.
        ///
        /// It goes like this:
        /// ["hum_head_bald] => {[0] => [i1,i2,i3,...]}
        ///  | Dict key is name of IMorphMesh
        ///                       | Array index is former vertexId
        ///                             | Data is new vertexIds from Unity mesh
        /// </summary>
        private readonly Dictionary<string, List<List<int>>> _vertexMapping = new();

        /// <summary>
        /// We also store the vertices migrated from ZenKit to Unity. They basically differentiate by:
        /// No vertex is reused for different triangles. Every time a vertex is needed, it got duplicated for Unity usage.
        /// </summary>
        private readonly Dictionary<string, Vector3[]> _unityVertices = new();

        // Morph meshes already warned about in TryGetMorphData(), so the warning is logged once per mesh.
        private readonly HashSet<string> _missingMorphMeshes = new();

        /// <summary>
        /// Save CPU cycles during runtime by caching the Morph values for every frame.
        ///
        /// e.g. ["Human-Head-Bald-Viseme"] = {[0] => {{x,y,z}, {x,y,z}}, [1] => {{x,y,z}, {x,y,z}}}
        /// [0] - is frame ID of the Morph Animation
        /// {{x,y,z}, {x,y,z}} - are the morph values of every vertex
        /// </summary>
        private readonly Dictionary<string, List<Vector3[]>> _animationMorphs = new();


        public bool IsMappingAlreadyCached(string morphMeshName)
        {
            var preparedKey = GetPreparedKey(morphMeshName);

            return _vertexMapping.ContainsKey(preparedKey);
        }

        public void AddVertexMapping(string morphMeshName, int arraySize)
        {
            var preparedKey = GetPreparedKey(morphMeshName);

            _vertexMapping.Add(preparedKey, new List<List<int>>(arraySize));

            // Initialize
            Enumerable.Range(0, arraySize)
                .ToList()
                .ForEach(_ => _vertexMapping[preparedKey].Add(new List<int>()));
        }

        public void AddVertexMappingEntry(string preparedMorphMeshName, int originalVertexIndex,
            int additionalUnityVertexIndex)
        {
            var preparedKey = GetPreparedKey(preparedMorphMeshName);

            _vertexMapping[preparedKey][originalVertexIndex].Add(additionalUnityVertexIndex);
        }

        public void SetUnityVerticesForVertexMapping(string preparedMorphMeshName, Vector3[] unityVertices)
        {
            var preparedKey = GetPreparedKey(preparedMorphMeshName);

            _unityVertices.Add(preparedKey, unityVertices);
        }

        [CanBeNull]
        public Vector3[] GetOriginalUnityVertices(string morphMeshName)
        {
            var preparedKey = GetPreparedKey(morphMeshName);

            return _unityVertices.TryGetValue(preparedKey, out var vertices) ? vertices : null;
        }

        /// <summary>
        /// Cache and return MorphAnimation samples.
        ///
        /// [0] => [Vector1, V2, V3, ...]
        /// | Key is frameId
        ///        | Data is the already processed morph data (morph addition to original triangle data)
        /// </summary>
        [CanBeNull]
        public List<Vector3[]> TryGetMorphData(string mmbName, string animationName)
        {
            var preparedMmbKey = GetPreparedKey(mmbName);
            var preparedAnimKey = GetPreparedKey(animationName);
            var preparedKey = $"{preparedMmbKey}-{preparedAnimKey}";

            if (_animationMorphs.TryGetValue(preparedKey, out var data))
            {
                return data;
            }

            // Create logic
            var mmb = _resourceCacheService.TryGetMorphMesh(mmbName);
            var anim = mmb.Animations.First(anim => anim.Name.EqualsIgnoreCase(animationName));

            // The head mesh wasn't registered (e.g. 'hum_head_fatbald.' in G2). Random face animations retried it
            // every few seconds and threw thousands of KeyNotFoundExceptions - no face animation instead, warn once.
            if (!_vertexMapping.TryGetValue(preparedMmbKey, out var originalVertexMapping) ||
                !_unityVertices.TryGetValue(preparedMmbKey, out var originalUnityVertexData))
            {
                if (_missingMorphMeshes.Add(preparedMmbKey))
                {
                    Logger.LogWarning($"MorphMesh '{mmbName}' has no cached vertex data - its animations are skipped.",
                        LogCat.Mesh);
                }
                return null;
            }

            // Original vertex count from ZenKit data.
            var vertexCount = anim.Vertices.Count;

            var newData = new List<Vector3[]>(anim.FrameCount);
            // Initialize - We set the VertexData for every frame to the original mesh value.
            Enumerable.Range(0, anim.FrameCount)
                .ToList()
                .ForEach(_ => newData.Add((Vector3[])originalUnityVertexData.Clone()));


            for (var i = 0; i < anim.SampleCount; i++)
            {
                var currentFrame = i / vertexCount;
                var currentZkVertexId = anim.Vertices[i % vertexCount];
                var morpMeshVectorAddition = anim.Samples[i].ToUnityVector();

                // For each unityVector at current frame, we add MorphMesh data.
                // Hint: If a vertex isn't named in the MorphMesh vertexIds, then we just leave it as original.
                foreach (var unityVertexId in originalVertexMapping[currentZkVertexId])
                {
                    newData[currentFrame][unityVertexId] =
                        originalUnityVertexData[unityVertexId] + morpMeshVectorAddition;
                }
            }

            _animationMorphs[preparedKey] = newData;

            return newData;
        }

        // TryGetMorphDataForVertices: frames per "mmb-animation-vertexCount".
        private readonly Dictionary<string, List<Vector3[]>> _animationMorphsByVertices = new();

        // Position rounding for matching Unity vertices to ZenKit positions (0.1 mm).
        private const float _positionMatchScale = 10000f;

        /// <summary>
        /// DeveloperConfig.EnableMorphByPosition: the frames of a morph animation for the given vertices of a Unity
        /// mesh (rest pose + the animation's offsets). The vertices are matched to the morph mesh by position - the
        /// mapping of TryGetMorphData() only exists if the mesh was built from the morph mesh first: a bow (or
        /// crossbow) built once without it (welded, cached) never had one and never bent.
        /// </summary>
        [CanBeNull]
        public List<Vector3[]> TryGetMorphDataForVertices(string mmbName, string animationName, Vector3[] unityVertices)
        {
            if (unityVertices == null || unityVertices.Length == 0)
                return null;

            var preparedKey = $"{GetPreparedKey(mmbName)}-{GetPreparedKey(animationName)}-{unityVertices.Length}";
            if (_animationMorphsByVertices.TryGetValue(preparedKey, out var cached))
                return cached;

            var mmb = _resourceCacheService.TryGetMorphMesh(mmbName);
            var anim = mmb?.Animations.FirstOrDefault(i => i.Name.EqualsIgnoreCase(animationName));
            if (mmb?.Mesh == null || anim == null)
            {
                _animationMorphsByVertices[preparedKey] = null;
                return null;
            }

            var positions = mmb.Mesh.Positions;
            var zkByPosition = new Dictionary<Vector3Int, List<int>>();
            for (var i = 0; i < positions.Count; i++)
            {
                var key = Vector3Int.RoundToInt(new Vector3(positions[i].X, positions[i].Y, positions[i].Z) / 100f *
                                                _positionMatchScale);
                if (!zkByPosition.TryGetValue(key, out var list))
                    zkByPosition[key] = list = new List<int>();
                list.Add(i);
            }

            var unityByZk = new List<int>[positions.Count];
            var matched = 0;
            for (var u = 0; u < unityVertices.Length; u++)
            {
                if (!zkByPosition.TryGetValue(Vector3Int.RoundToInt(unityVertices[u] * _positionMatchScale),
                        out var zkIds))
                    continue;
                matched++;
                foreach (var zkId in zkIds)
                    (unityByZk[zkId] ??= new List<int>()).Add(u);
            }

            if (matched == 0)
            {
                Logger.LogWarning($"[MorphMesh] {mmbName}: no vertex matches the morph mesh - {animationName} skipped.",
                    LogCat.Mesh);
                _animationMorphsByVertices[preparedKey] = null;
                return null;
            }

            var vertexCount = anim.Vertices.Count;
            var frames = new List<Vector3[]>(anim.FrameCount);
            for (var f = 0; f < anim.FrameCount; f++)
                frames.Add((Vector3[])unityVertices.Clone());

            for (var i = 0; i < anim.SampleCount; i++)
            {
                var frame = i / vertexCount;
                var zkId = anim.Vertices[i % vertexCount];
                if (frame >= frames.Count || zkId < 0 || zkId >= unityByZk.Length || unityByZk[zkId] == null)
                    continue;

                var offset = anim.Samples[i].ToUnityVector();
                foreach (var u in unityByZk[zkId])
                    frames[frame][u] = unityVertices[u] + offset;
            }

            Logger.Log($"[MorphMesh] {mmbName} {animationName}: {anim.FrameCount} frames, " +
                       $"{matched}/{unityVertices.Length} vertices matched by position", LogCat.Mesh);
            _animationMorphsByVertices[preparedKey] = frames;
            return frames;
        }

        public string GetPreparedKey(string key)
        {
            var lowerKey = key.ToLower();
            var extension = Path.GetExtension(lowerKey);

            if (extension == string.Empty)
            {
                return lowerKey;
            }

            return lowerKey.Replace(extension, "");
        }

        public void Dispose()
        {
            _vertexMapping.Clear();
            _unityVertices.Clear();
            _animationMorphs.Clear();
            _animationMorphsByVertices.Clear();
        }
    }
}
