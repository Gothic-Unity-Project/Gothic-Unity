#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Logging;
using Gothic.Core.Services.Caches;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// DeveloperConfig.EnableVrCrossbowManualReload: the cocked state and the shape of a crossbow. It stays on the item
    /// (a fired crossbow put on the ground stays fired); VRCrossbow drives it while the crossbow is held.
    /// The shapes are Gothic's own (ITRWCROSSBOW*.MMB): cocked = the rest mesh (S_READY has no offsets), fired = the
    /// frame of S_RELAX (the engine's reload animation) with the string farthest forward at the prod. In between the
    /// vertices are blended - the string follows the pulling hand.
    /// The string's middle (nock) = the vertex moving farthest between the two shapes.
    /// </summary>
    public class VRCrossbowString : MonoBehaviour
    {
        [Inject] private readonly MorphMeshCacheService _morphMeshCacheService;

        private const string _reloadAnimation = "S_RELAX";

        private MeshFilter _meshFilter;
        private Mesh _originalMesh;
        private Mesh _mesh;
        private Vector3[] _cockedVertices;
        private Vector3[] _firedVertices;
        private Vector3[] _currentVertices;
        private int _nockIndex = -1;
        private bool _isInitialized;

        /// <summary>
        /// 0 = fired (string at the prod), 1 = cocked. A new crossbow (from the backpack, a holster, the world) is
        /// cocked.
        /// </summary>
        public float Progress { get; private set; } = 1f;

        public bool IsLoaded => Progress >= 1f;

        /// <summary>
        /// The shapes were found - without them the crossbow reloads automatically.
        /// </summary>
        public bool IsAnimated => _nockIndex >= 0;

        public Vector3 CockedNockLocal => _cockedVertices[_nockIndex];
        public Vector3 FiredNockLocal => _firedVertices[_nockIndex];
        public Vector3 CurrentNockLocal => Vector3.Lerp(FiredNockLocal, CockedNockLocal, Progress);

        private void Awake()
        {
            this.Inject();
        }

        public void Init(string visual)
        {
            if (_isInitialized)
                return;
            _isInitialized = true;

            _meshFilter = GetComponent<MeshFilter>();
            if (_meshFilter == null || _meshFilter.sharedMesh == null || !_meshFilter.sharedMesh.isReadable)
            {
                Logger.LogWarning($"[VRCrossbowString] {visual}: no readable mesh - automatic reload.", LogCat.VR);
                return;
            }

            _originalMesh = _meshFilter.sharedMesh;
            _cockedVertices = _originalMesh.vertices;
            var frames = _morphMeshCacheService.TryGetMorphDataForVertices(visual, _reloadAnimation, _cockedVertices);
            if (frames == null || frames.Count == 0)
            {
                Logger.LogWarning($"[VRCrossbowString] {visual}: no {_reloadAnimation} morph - automatic reload.",
                    LogCat.VR);
                return;
            }

            // Fired = the frame whose vertex moves farthest away from the cocked shape (the string at the prod).
            var bestDistance = 0f;
            for (var f = 0; f < frames.Count; f++)
            {
                for (var i = 0; i < _cockedVertices.Length; i++)
                {
                    var distance = (frames[f][i] - _cockedVertices[i]).sqrMagnitude;
                    if (distance <= bestDistance)
                        continue;
                    bestDistance = distance;
                    _firedVertices = frames[f];
                    _nockIndex = i;
                }
            }

            if (_nockIndex < 0 || Mathf.Sqrt(bestDistance) < 0.05f)
            {
                _nockIndex = -1;
                Logger.LogWarning($"[VRCrossbowString] {visual}: the string doesn't move in {_reloadAnimation} - " +
                                  "automatic reload.", LogCat.VR);
                return;
            }

            _mesh = Instantiate(_originalMesh);
            _mesh.name = $"{_originalMesh.name} (VR crossbow)";
            _mesh.MarkDynamic();
            _meshFilter.sharedMesh = _mesh;
            _currentVertices = new Vector3[_cockedVertices.Length];
            ApplyShape();

            Logger.Log($"[VRCrossbowString] {visual}: string travel {Mathf.Sqrt(bestDistance):F2} m (vertex " +
                       $"{_nockIndex})", LogCat.VR);
        }

        public void SetProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (Mathf.Approximately(progress, Progress))
                return;
            Progress = progress;
            ApplyShape();
        }

        private void ApplyShape()
        {
            if (!IsAnimated || _mesh == null)
                return;

            for (var i = 0; i < _currentVertices.Length; i++)
                _currentVertices[i] = Vector3.LerpUnclamped(_firedVertices[i], _cockedVertices[i], Progress);
            _mesh.vertices = _currentVertices;
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_meshFilter != null && _originalMesh != null)
                _meshFilter.sharedMesh = _originalMesh;
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
#endif
