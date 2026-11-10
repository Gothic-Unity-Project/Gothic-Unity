using Gothic.Core.Models.Caches;
using Gothic.Core.Services.Caches;
using TMPro;
using UnityEngine;
using UnityEngine.Pool;

namespace Gothic.Core.Adapters.UI
{
    /// <summary>
    /// "-12 HP" / "+5 MANA" next to the hero's own health bar (our UI) - same look as FloatingHitNumber over NPCs:
    /// red/green Gothic font, rises a bit and fades out, then goes back to its pool.
    /// Prefab PrefabType.UiHitNumber (pivot, text settings), pooled by its spawner (CreatePool).
    /// </summary>
    public class FloatingUiHitNumber : MonoBehaviour
    {
        private const float _lifetime = 1.3f;
        // In bar heights.
        private const float _riseHeights = 2f;
        private const float _fontSizeInBarHeights = 1.2f;
        private const float _inFrontOfBar = -0.5f;
        private const int _poolCapacity = 4;
        private const int _poolMaxSize = 8;

        [Tooltip("The number's text (alignment, no raycasts). Gothic's font and the size are set at runtime.")]
        [SerializeField]
        private TextMeshProUGUI _text;

        private IObjectPool<FloatingUiHitNumber> _pool;
        private RectTransform _rect;
        private Vector2 _startPosition;
        private float _riseDistance;
        private float _elapsed;

        /// <param name="canvas">The bar's canvas - the numbers live there unrotated, reading the right way.</param>
        public static ObjectPool<FloatingUiHitNumber> CreatePool(ResourceCacheService resourceCacheService,
            RectTransform canvas)
        {
            ObjectPool<FloatingUiHitNumber> pool = null;
            pool = new ObjectPool<FloatingUiHitNumber>(
                () =>
                {
                    var number = resourceCacheService
                        .TryGetPrefabObject(PrefabType.UiHitNumber, parent: canvas.gameObject)
                        .GetComponent<FloatingUiHitNumber>();
                    number._pool = pool;
                    number._rect = (RectTransform)number.transform;
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
        /// Starts a number taken from the pool (CreatePool).
        /// </summary>
        /// <param name="anchorWorldPosition">Where the number starts (e.g. right of the bar), in world space.</param>
        /// <param name="barHeight">The bar's height in canvas units - sizes the text.</param>
        /// <param name="sizeScale">FloatingHitNumber.GetSizeScale.</param>
        public void Show(Vector3 anchorWorldPosition, float barHeight, int amount, string unit, float sizeScale)
        {
            // Drawn above the other numbers still rising, like a newly created one.
            _rect.SetAsLastSibling();
            _rect.sizeDelta = new Vector2(barHeight * 10f, barHeight * 2f * sizeScale);
            _rect.localRotation = Quaternion.identity;
            _rect.localScale = Vector3.one;
            var local = ((RectTransform)_rect.parent).InverseTransformPoint(anchorWorldPosition);
            // In front of the bar (z-fighting otherwise).
            _rect.localPosition = new Vector3(local.x, local.y, _inFrontOfBar);
            _startPosition = _rect.anchoredPosition;
            _riseDistance = barHeight * _riseHeights * sizeScale;
            _elapsed = 0f;

            _text.text = FloatingHitNumber.GetText(amount, unit);
            // Full alpha again - the color's alpha is 1.
            _text.color = FloatingHitNumber.GetColor(amount, unit);
            _text.fontSize = barHeight * _fontSizeInBarHeights * sizeScale;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var t = _elapsed / _lifetime;
            if (t >= 1f)
            {
                _pool.Release(this);
                return;
            }

            _rect.anchoredPosition = _startPosition + Vector2.up * (_riseDistance * (1f - (1f - t) * (1f - t)));
            _text.alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
        }
    }
}
