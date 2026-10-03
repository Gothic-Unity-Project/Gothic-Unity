using UnityEngine;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// shpScaleKeys_S of a Gothic particle effect: the emitter shape grows/shrinks over time (MFX_Icewave_WAVE: a
    /// circle from 20 cm to 8 m in one second). Unity has no shape scale curve - the radius/box is set every frame.
    /// </summary>
    public class PfxShapeScaler : MonoBehaviour
    {
        private ParticleSystem _particleSystem;
        private float[] _keys;
        private float _fps;
        private bool _isLooping;
        private bool _isSmooth;
        private float _baseRadius;
        private Vector3 _baseScale;
        private float _time;


        public void Init(ParticleSystem particleSystem, float[] keys, float fps, bool isLooping, bool isSmooth)
        {
            _particleSystem = particleSystem;
            _keys = keys;
            _fps = fps;
            _isLooping = isLooping;
            _isSmooth = isSmooth;
            _baseRadius = particleSystem.shape.radius;
            _baseScale = particleSystem.shape.scale;
            Apply(_keys[0]);
        }

        private void Update()
        {
            if (_particleSystem == null || _keys == null || _keys.Length == 0)
                return;

            _time += Time.deltaTime;
            var position = _time * _fps;
            if (_isLooping)
                position %= _keys.Length;
            var index = Mathf.Min(Mathf.FloorToInt(position), _keys.Length - 1);
            var next = _isLooping ? (index + 1) % _keys.Length : Mathf.Min(index + 1, _keys.Length - 1);
            var scale = _isSmooth ? Mathf.Lerp(_keys[index], _keys[next], position - Mathf.Floor(position)) : _keys[index];
            Apply(scale);

            if (!_isLooping && index >= _keys.Length - 1)
                enabled = false;
        }

        private void Apply(float scale)
        {
            // Box: its size, others (circle, sphere): the radius - never both (they would multiply).
            var shape = _particleSystem.shape;
            if (shape.shapeType == ParticleSystemShapeType.Box)
                shape.scale = _baseScale * scale;
            else
                shape.radius = _baseRadius * scale;
        }
    }
}
