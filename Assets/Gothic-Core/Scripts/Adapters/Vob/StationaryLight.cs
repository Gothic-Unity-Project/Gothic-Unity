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

        public int Index { get; set; } = -1;

        /// <summary>
        /// A pooled world light (fires, static lights of caves/houses): StationaryLightsService hands it a shader slot
        /// only while it's loaded and among the lights closest to the camera - Index is -1 otherwise.
        /// </summary>
        public bool IsRuntimeSlot { get; set; }
        public Color RuntimeLinearColor { get; set; }

        public static readonly int StationaryLightIndicesShaderId = Shader.PropertyToID("_StationaryLightIndices");
        public static readonly int StationaryLightIndices2ShaderId = Shader.PropertyToID("_StationaryLightIndices2");
        public static readonly int StationaryLightCountShaderId = Shader.PropertyToID("_StationaryLightCount");

        private static Coroutine _updateDirtiedMeshesRoutine;

        private List<MeshRenderer> _affectedRenderers = new();
        private Light _unityLight;

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
            if (IsRuntimeSlot)
            {
                // Init() calls this on a light that may still be inactive (lazy loading) - Unity calls it again later.
                if (isActiveAndEnabled)
                    LightsService.RegisterPooledLight(this);
                return;
            }

            foreach (var rend in _affectedRenderers)
            {
                LightsService.AddLightOnRenderer(this, rend);
            }
        }

        private void OnDisable()
        {
            if (IsRuntimeSlot)
            {
                LightsService.UnregisterPooledLight(this);
                return;
            }

            foreach (var rend in _affectedRenderers)
            {
                LightsService.RemoveLightOnRenderer(this, rend);
            }
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
