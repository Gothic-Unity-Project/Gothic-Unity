using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Adapters.Vob
{
    [RequireComponent(typeof(Light))]
    public class StationaryLight : MonoBehaviour
    {
        [Inject] private readonly StationaryLightsService _stationaryLightsService;
        [Inject] private GameTimeService _gameTimeService;
        [Inject] private VobService _vobService;
        [Inject] private NpcService _npcService;


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

        public static readonly int StationaryLightIndicesShaderId = Shader.PropertyToID("_StationaryLightIndices");
        public static readonly int StationaryLightIndices2ShaderId = Shader.PropertyToID("_StationaryLightIndices2");
        public static readonly int StationaryLightCountShaderId = Shader.PropertyToID("_StationaryLightCount");

        private static Coroutine _updateDirtiedMeshesRoutine;

        private List<MeshRenderer> _affectedRenderers = new();
        private Light _unityLight;

        private Color[] _colorAnimationList;
        private float _colorAnimationFps;
        private bool _colorAnimationSmooth;
        private float _colorAnimTime;

        private float[] _rangeAnimationScale;
        private float _rangeAnimationFps;
        private bool _rangeAnimationSmooth;
        private float _rangeAnimTime;
        private float _baseRange;

        private bool _isAnimated;
        
        private void Awake()
        {
            _unityLight = GetComponent<Light>();
        }

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

        private void OnEnable()
        {
            foreach (var rend in _affectedRenderers)
            {
                _stationaryLightsService.AddLightOnRenderer(this, rend);
            }
        }

        private void OnDisable()
        {
            foreach (var rend in _affectedRenderers)
            {
                _stationaryLightsService.RemoveLightOnRenderer(this, rend);
            }
        }

        public void SetColorAnimation(Color[] colors, float fps, bool smooth)
        {
            _colorAnimationList = colors;
            _colorAnimationFps = fps;
            _colorAnimationSmooth = smooth;
            _colorAnimTime = 0;
            _isAnimated = true;
        }

        public void SetRangeAnimation(float[] scales, float fps, bool smooth)
        {
            _rangeAnimationScale = scales;
            _rangeAnimationFps = fps;
            _rangeAnimationSmooth = smooth;
            _rangeAnimTime = 0;
            _baseRange = Range;
            _isAnimated = true;
        }

        private void Update()
        {
            if (!_isAnimated) return;
                AdvanceAnimationTime();
                AnimateColor();
                AnimateRange();

            _stationaryLightsService?.NotifyLightChanged(this);
        }

        private void AdvanceAnimationTime()
        {
            if (_colorAnimationList is { Length: > 0 } && _colorAnimationFps > 0)
                _colorAnimTime += Time.deltaTime;
            if (_rangeAnimationScale is { Length: > 0 } && _rangeAnimationFps > 0)
                _rangeAnimTime += Time.deltaTime;
        }

        private void AnimateColor()
        {
            if (_colorAnimationList == null || _colorAnimationList.Length == 0 || _colorAnimationFps <= 0)
                return;

            var totalDuration = _colorAnimationList.Length / _colorAnimationFps;
            _colorAnimTime %= totalDuration;
            var frameIndex = Mathf.FloorToInt(_colorAnimTime * _colorAnimationFps);
            frameIndex = Mathf.Clamp(frameIndex, 0, _colorAnimationList.Length - 1);

            if (_colorAnimationSmooth)
            {
                var nextIndex = (frameIndex + 1) % _colorAnimationList.Length;
                var t = (_colorAnimTime * _colorAnimationFps) - frameIndex;
                _unityLight.color = Color.Lerp(_colorAnimationList[frameIndex], _colorAnimationList[nextIndex], t);
            }
            else
            {
                _unityLight.color = _colorAnimationList[frameIndex];
            }
        }

        private void AnimateRange()
        {
            if (_rangeAnimationScale == null || _rangeAnimationScale.Length == 0 || _rangeAnimationFps <= 0)
                return;

            var totalDuration = _rangeAnimationScale.Length / _rangeAnimationFps;
            _rangeAnimTime %= totalDuration;
            var frameIndex = Mathf.FloorToInt(_rangeAnimTime * _rangeAnimationFps);
            frameIndex = Mathf.Clamp(frameIndex, 0, _rangeAnimationScale.Length - 1);

            float scale;
            if (_rangeAnimationSmooth)
            {
                var nextIndex = (frameIndex + 1) % _rangeAnimationScale.Length;
                var t = (_rangeAnimTime * _rangeAnimationFps) - frameIndex;
                scale = Mathf.Lerp(_rangeAnimationScale[frameIndex], _rangeAnimationScale[nextIndex], t);
            }
            else
            {
                scale = _rangeAnimationScale[frameIndex];
            }

            _unityLight.range = _baseRange * scale;
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
