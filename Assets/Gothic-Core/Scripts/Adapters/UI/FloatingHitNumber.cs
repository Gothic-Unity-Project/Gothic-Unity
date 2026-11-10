using Gothic.Core.Const;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;

namespace Gothic.Core.Adapters.UI
{
    /// <summary>
    /// One "-12 HP" / "+5 MANA" number: rises a bit, always faces the camera, fades out and goes back to its pool.
    /// Stays where it was spawned (doesn't follow the NPC), like TF2 damage numbers.
    /// Prefab PrefabType.HitNumber (text settings), pooled by its spawner (CreatePool) - no new GameObject per hit.
    /// </summary>
    public class FloatingHitNumber : MonoBehaviour
    {
        private const float _lifetime = 1.3f;
        private const float _riseDistance = 0.45f;
        private const float _scale = 0.03f;
        private const int _poolCapacity = 4;
        private const int _poolMaxSize = 16;

        private static readonly Color _damageColor = new(1f, 0.25f, 0.2f);
        private static readonly Color _healColor = new(0.35f, 1f, 0.35f);
        private static readonly Color _manaColor = new(0.35f, 0.6f, 1f);
        public const string ManaUnit = "MANA";

        [Tooltip("The number's text (font size, alignment). Gothic's font is applied at runtime.")] [SerializeField]
        private TextMeshPro _text;

        private IObjectPool<FloatingHitNumber> _pool;
        private Camera _camera;
        private Vector3 _startPosition;
        private float _elapsed;

        /// <summary>
        /// The numbers live below parent (destroyed with it). Created on demand, Gothic's font applied once.
        /// </summary>
        public static ObjectPool<FloatingHitNumber> CreatePool(ResourceCacheService resourceCacheService,
            GameObject parent)
        {
            ObjectPool<FloatingHitNumber> pool = null;
            pool = new ObjectPool<FloatingHitNumber>(
                () =>
                {
                    var number = resourceCacheService.TryGetPrefabObject(PrefabType.HitNumber, parent: parent)
                        .GetComponent<FloatingHitNumber>();
                    number._pool = pool;
                    GothicText.Apply(number._text, isTinted: true);
                    return number;
                },
                number => number.gameObject.SetActive(true),
                number => number.gameObject.SetActive(false),
                number =>
                {
                    if (number != null)
                        Destroy(number.gameObject);
                },
                false, _poolCapacity, _poolMaxSize);
            return pool;
        }

        /// <summary>
        /// Slider "hitNumbersSize" in our settings menu: 1 (smallest, default) ... HitNumbersMaxScale.
        /// </summary>
        public static float GetSizeScale(ConfigService configService)
        {
            var size = Mathf.Clamp01(configService.GetGothicOption(Constants.IniNames.HitNumbersSize, 0f));
            return Mathf.Lerp(1f, Constants.IniNames.HitNumbersMaxScale, size);
        }

        /// <summary>
        /// HP: green up, red down. Mana: always blue.
        /// </summary>
        public static Color GetColor(int amount, string unit)
        {
            if (unit == ManaUnit)
                return _manaColor;
            return amount > 0 ? _healColor : _damageColor;
        }

        public static string GetText(int amount, string unit) => amount > 0 ? $"+{amount} {unit}" : $"{amount} {unit}";

        /// <summary>
        /// Starts a number taken from the pool (CreatePool).
        /// </summary>
        /// <param name="sizeScale">GetSizeScale - read by the caller, which has the ConfigService.</param>
        public void Show(Vector3 position, int amount, string unit, float sizeScale)
        {
            transform.position = position;
            transform.localScale = Vector3.one * (_scale * sizeScale);
            _startPosition = position;
            _elapsed = 0f;

            _text.text = GetText(amount, unit);
            // Full alpha again - the color's alpha is 1.
            _text.color = GetColor(amount, unit);

            FaceCamera();
        }

        private void LateUpdate()
        {
            _elapsed += Time.deltaTime;
            var t = _elapsed / _lifetime;
            if (t >= 1f)
            {
                _pool.Release(this);
                return;
            }

            // Fast start, slow end - the number "pops" up and settles.
            transform.position = _startPosition + Vector3.up * (_riseDistance * (1f - (1f - t) * (1f - t)));
            _text.alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;

            FaceCamera();
        }

        private void FaceCamera()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            // TMP text is read along its +Z axis, so it looks away from the camera.
            transform.rotation = Quaternion.LookRotation(transform.position - _camera.transform.position);
        }
    }
}
