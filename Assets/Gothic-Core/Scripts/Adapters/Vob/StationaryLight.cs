using System.Collections.Generic;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Adapters.Vob
{
    [RequireComponent(typeof(Light))]
    public class StationaryLight : MonoBehaviour
    {
        [Inject] private readonly StationaryLightsService _stationaryLightsService;
        private StationaryLightsService _resolvedLightsService;

        // OnEnable can run before GameObjectSelfInjector injected this component (component order on a new GO) - the
        // NullReferenceExceptions of lights in nested VOBs (The Chronicles Of Myrtana's ship lanterns didn't light).
        private StationaryLightsService LightsService => _stationaryLightsService ??
            (_resolvedLightsService ??= ReflexProjectInstaller.DIContainer.Resolve<StationaryLightsService>());
        
        
        public Color Color
        {
            get
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                return _unityLight.color;
            }
            set
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                _unityLight.color = value;
            }
        }

        public LightType Type
        {
            get
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                return _unityLight.type;
            }
            set
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                _unityLight.type = value;
            }
        }

        public float Intensity
        {
            get
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                return _unityLight.intensity;
            }
            set
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                _unityLight.intensity = value;
            }
        }

        public float Range
        {
            get
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                return _unityLight.range;
            }
            set
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                _unityLight.range = value;
            }
        }

        public float SpotAngle
        {
            get
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                return _unityLight.spotAngle;
            }
            set
            {
                if (!_unityLight)
                {
                    _unityLight = GetComponent<Light>();
                }

                _unityLight.spotAngle = value;
            }
        }

        /// <summary>
        /// Shader slot: StationaryLightsService hands one out only while the light is loaded and among the lights
        /// closest to the camera - -1 otherwise. A light moving at runtime (torch in the hand, Light spell) sets
        /// StationaryLightsService.DynamicLightIndex before Init() and keeps it (no pool).
        /// </summary>
        public int Index { get; set; } = -1;

        /// <summary>
        /// A static light whose light is baked into the world's lightmaps (StationaryLightsService.HasWorldLightmaps):
        /// it lights NPCs and VOBs outside the lightmaps only - the light mapped walls and the VOBs tinted with their
        /// light (ApplyWorldLight) would get it twice: the Old Mine's scaffolds got brighter with every lamp the pool lit.
        /// </summary>
        public bool IsBakedIntoWorld { get; set; }

        public int AffectedRendererCount => _affectedRenderers.Count;

        /// <summary>
        /// Color the pool writes into the shader slot it hands out (linear space).
        /// </summary>
        public Color RuntimeLinearColor { get; set; }

        /// <summary>
        /// World position and range, set in Init(). The light never moves - the pool reads them on every refresh
        /// without asking the engine.
        /// </summary>
        public Vector3 CachedPosition { get; private set; }

        public float CachedRange { get; private set; }

        public static readonly int StationaryLightIndicesShaderId = Shader.PropertyToID("_StationaryLightIndices");
        public static readonly int StationaryLightIndices2ShaderId = Shader.PropertyToID("_StationaryLightIndices2");
        public static readonly int StationaryLightCountShaderId = Shader.PropertyToID("_StationaryLightCount");

        private static Coroutine _updateDirtiedMeshesRoutine;

        private List<MeshRenderer> _affectedRenderers = new();
        private Light _unityLight;
        // OnEnable already runs when the prefab is spawned - the pool may only know the light after Init().
        private bool _isInitialized;
        // The dynamic slot (torch, Light spell): it lights its renderers itself, the pool never sees it.
        private bool _isFixedSlot;

        private void OnDrawGizmosSelected()
        {
            Gizmos.DrawWireSphere(transform.position, Range);
        }

        /// <summary>
        /// Set light's surrounding Meshes to add light information onto it later.
        /// As OnEnable is called when this Prefab is spawned, we need to call Init() separately now.
        ///
        /// HINT: The affected meshes won't be recalculated when another object gets visible (e.g. lazy loaded).
        ///       If we want to optimize it in the future, we would need to create a class which holds light bounds and
        ///       whenever something gets visible, the affected lights update their renderers.
        /// </summary>
        public void Init()
        {
            CachedPosition = transform.position;
            CachedRange = Range;
            _isFixedSlot = Index >= 0;
            _isInitialized = true;
            GatherRenderers();

            // Call Light on Renderer activation again.
            OnEnable();
        }

        /// <summary>
        /// Re-gathers nearby renderers and re-registers with them. Unlike Init() (meant to run once
        /// for a static world light), this is safe to call repeatedly for a light that moves at
        /// runtime (e.g. a spell effect following the player) so it keeps affecting whatever's nearby.
        /// </summary>
        public void Refresh()
        {
            OnDisable();
            _affectedRenderers.Clear();
            CachedPosition = transform.position;
            GatherRenderers();
            OnEnable();
        }

        /// <summary>
        /// The pool gave this light a shader slot: light the renderers around it.
        /// </summary>
        public void AttachSlot(int index)
        {
            Index = index;
            foreach (var rend in _affectedRenderers)
                LightsService.AddLightOnRenderer(this, rend);
        }

        /// <summary>
        /// The pool took the slot back (farther away than others, or unloaded).
        /// </summary>
        public void DetachSlot()
        {
            foreach (var rend in _affectedRenderers)
                LightsService.RemoveLightOnRenderer(this, rend);
            Index = -1;
        }

        private void OnEnable()
        {
            if (_isFixedSlot)
            {
                foreach (var rend in _affectedRenderers)
                    LightsService.AddLightOnRenderer(this, rend);
                return;
            }

            // Init() calls this on a light that may still be inactive (lazy loading) - Unity calls it again later.
            if (_isInitialized && isActiveAndEnabled)
                LightsService.RegisterPooledLight(this);
        }

        private void OnDisable()
        {
            if (_isFixedSlot)
            {
                foreach (var rend in _affectedRenderers)
                    LightsService.RemoveLightOnRenderer(this, rend);
                return;
            }

            if (_isInitialized)
                LightsService.UnregisterPooledLight(this);
        }

        private void GatherRenderers()
        {
            var colliders = Physics.OverlapSphere(transform.position, Range);
            for (var i = 0; i < colliders.Length; i++)
            {
                var renderer = colliders[i].GetComponent<MeshRenderer>();
                if (renderer && !(IsBakedIntoWorld && (LightsService.IsWorldChunk(renderer) ||
                                                       LightsService.IsWorldLit(renderer))))
                {
                    _affectedRenderers.Add(renderer);
                }
            }
        }
    }
}
