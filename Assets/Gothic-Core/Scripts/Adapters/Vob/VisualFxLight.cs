using Gothic.Core.Domain.Vobs;
using UnityEngine;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// DeveloperConfig.EnableVisualFxLights: the light of a VISUALFX (lightPresetName: summoning's red REDAMBIENCE,
    /// fire rain's CATACLYSM) like OpenGothic's Effect::setupLight - the preset's color and range animations, a key's
    /// lightRange instead of the preset's range (summoning: 0 while held, 4 m while charging). A pooled
    /// StationaryLight, so the world shader lights the ground around it too. It follows its parent.
    /// </summary>
    [RequireComponent(typeof(StationaryLight))]
    public class VisualFxLight : MonoBehaviour
    {
        // CATACLYSM is 200 m - the world shader's lights are meant for a room or a camp, not the whole valley.
        private const float _maxRangeMeters = 20f;
        private const float _updateSeconds = 0.1f;
        // A following light gathers its renderers again after moving this far.
        private const float _regatherDistance = 1f;

        private StationaryLight _light;
        private LightPresetLoader.LightPreset _preset;
        private float _baseRange;
        private float _startTime;
        private float _fadeStart = -1f;
        private float _fadeSeconds;
        private float _nextUpdate;
        private Vector3 _gatheredAt;

        public static VisualFxLight Create(Transform parent, Vector3 position, LightPresetLoader.LightPreset preset,
            float rangeMeters)
        {
            var go = new GameObject($"VisualFxLight_{preset.Name}");
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            var light = go.AddComponent<VisualFxLight>();
            light.Init(preset, rangeMeters);
            return light;
        }

        private void Init(LightPresetLoader.LightPreset preset, float rangeMeters)
        {
            _preset = preset;
            _baseRange = Mathf.Min(rangeMeters > 0f ? rangeMeters : preset.RangeMeters, _maxRangeMeters);
            _startTime = Time.time;

            _light = GetComponent<StationaryLight>();
            _light.Type = LightType.Point;
            _light.Intensity = 1f;
            _light.Color = preset.Color;
            _light.Range = Mathf.Max(0.01f, _baseRange);
            _light.RuntimeLinearColor = preset.Color.linear;
            _light.Init();
            _gatheredAt = transform.position;
            Apply();
        }

        /// <summary>
        /// A key's lightRange (charging level).
        /// </summary>
        public void SetRange(float rangeMeters)
        {
            _baseRange = Mathf.Min(rangeMeters, _maxRangeMeters);
            Apply();
        }

        /// <summary>
        /// Dims to nothing over the seconds (starting after the delay), then is gone.
        /// </summary>
        public void FadeOut(float seconds, float delay = 0f)
        {
            if (_fadeStart >= 0f)
                return;
            _fadeStart = Time.time + Mathf.Max(0f, delay);
            _fadeSeconds = Mathf.Max(0.01f, seconds);
            Destroy(gameObject, delay + _fadeSeconds + 0.1f);
        }

        private void Update()
        {
            if (Time.time < _nextUpdate)
                return;
            _nextUpdate = Time.time + _updateSeconds;

            if ((transform.position - _gatheredAt).sqrMagnitude > _regatherDistance * _regatherDistance)
            {
                _gatheredAt = transform.position;
                _light.Refresh();
            }
            Apply();
        }

        private void Apply()
        {
            var age = Time.time - _startTime;
            var color = Sample(_preset.Colors, _preset.ColorFps, _preset.IsColorSmooth, age, _preset.Color);
            var range = _baseRange * Sample(_preset.RangeScales, _preset.RangeFps, _preset.IsRangeSmooth, age, 1f);
            if (_fadeStart >= 0f && Time.time >= _fadeStart)
            {
                var fade = 1f - Mathf.Clamp01((Time.time - _fadeStart) / _fadeSeconds);
                color *= fade;
                range *= fade;
            }
            _light.UpdateRuntime(color, Mathf.Max(0.01f, range));
        }

        private static Color Sample(Color[] keys, float fps, bool isSmooth, float age, Color fallback)
        {
            if (keys == null || keys.Length == 0)
                return fallback;
            if (keys.Length == 1 || fps <= 0f)
                return keys[0];
            var frame = age * fps;
            var index = Mathf.FloorToInt(frame) % keys.Length;
            if (!isSmooth)
                return keys[index];
            return Color.Lerp(keys[index], keys[(index + 1) % keys.Length], frame - Mathf.Floor(frame));
        }

        private static float Sample(float[] keys, float fps, bool isSmooth, float age, float fallback)
        {
            if (keys == null || keys.Length == 0)
                return fallback;
            if (keys.Length == 1 || fps <= 0f)
                return keys[0];
            var frame = age * fps;
            var index = Mathf.FloorToInt(frame) % keys.Length;
            if (!isSmooth)
                return keys[index];
            return Mathf.Lerp(keys[index], keys[(index + 1) % keys.Length], frame - Mathf.Floor(frame));
        }
    }
}
