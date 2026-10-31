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
        /// closest to the camera - -1 otherwise.
        /// </summary>
        public int Index { get; private set; } = -1;

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
            _isInitialized = true;
            GatherRenderers();

            // Call Light on Renderer activation again.
            OnEnable();
        }

        /// <summary>
        /// The pool gave this light a shader slot: light the renderers around it.
        /// </summary>
        public void AttachSlot(int index)
        {
            Index = index;
            foreach (var rend in _affectedRenderers)
                _stationaryLightsService.AddLightOnRenderer(this, rend);
        }

        /// <summary>
        /// The pool took the slot back (farther away than others, or unloaded).
        /// </summary>
        public void DetachSlot()
        {
            foreach (var rend in _affectedRenderers)
                _stationaryLightsService.RemoveLightOnRenderer(this, rend);
            Index = -1;
        }

        /// <summary>
        /// A light that moves (the Light spell above the hero): it lights the renderers around its new position.
        /// </summary>
        public void Relocate(Vector3 position)
        {
            if (Index >= 0)
            {
                foreach (var rend in _affectedRenderers)
                    _stationaryLightsService.RemoveLightOnRenderer(this, rend);
            }

            _affectedRenderers.Clear();
            transform.position = position;
            CachedPosition = position;
            GatherRenderers();

            if (Index >= 0)
            {
                foreach (var rend in _affectedRenderers)
                    _stationaryLightsService.AddLightOnRenderer(this, rend);
            }
        }

        private void OnEnable()
        {
            // Init() calls this on a light that may still be inactive (lazy loading) - Unity calls it again later.
            if (_isInitialized && isActiveAndEnabled)
                _stationaryLightsService.RegisterPooledLight(this);
        }

        private void OnDisable()
        {
            if (_isInitialized)
                _stationaryLightsService.UnregisterPooledLight(this);
        }

        private void GatherRenderers()
        {
            var colliders = Physics.OverlapSphere(transform.position, Range);
            for (var i = 0; i < colliders.Length; i++)
            {
                var renderer = colliders[i].GetComponent<MeshRenderer>();
                if (renderer)
                {
                    _affectedRenderers.Add(renderer);
                }
            }
        }
    }
}
