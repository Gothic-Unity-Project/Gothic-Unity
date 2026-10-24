using TMPro;
using UnityEngine;

namespace Gothic.Core.Adapters.UI
{
    /// <summary>
    /// "-12 HP" / "+5 MANA" next to the hero's own health bar (our UI) - same look as FloatingHitNumber over NPCs:
    /// red/green Gothic font, rises a bit and fades out.
    /// </summary>
    public class FloatingUiHitNumber : MonoBehaviour
    {
        private const float _lifetime = 1.3f;
        // In bar heights.
        private const float _riseHeights = 2f;
        private const float _fontSizeInBarHeights = 1.2f;
        private const float _inFrontOfBar = -0.5f;

        private TextMeshProUGUI _text;
        private RectTransform _rect;
        private Vector2 _startPosition;
        private float _riseDistance;
        private float _elapsed;

        /// <param name="canvas">The bar's canvas - the text lives there unrotated, so it reads the right way round.</param>
        /// <param name="anchorWorldPosition">Where the number starts (e.g. right of the bar), in world space.</param>
        /// <param name="barHeight">The bar's height in canvas units - sizes the text.</param>
        public static void Spawn(RectTransform canvas, Vector3 anchorWorldPosition, float barHeight, int amount,
            string unit)
        {
            var go = new GameObject($"UiHitNumber {amount} {unit}", typeof(RectTransform));
            go.layer = canvas.gameObject.layer;
            go.transform.SetParent(canvas, false);
            go.AddComponent<FloatingUiHitNumber>().Init(canvas, anchorWorldPosition, barHeight, amount, unit);
        }

        private void Init(RectTransform canvas, Vector3 anchorWorldPosition, float barHeight, int amount, string unit)
        {
            var sizeScale = FloatingHitNumber.GetSizeScale();

            _rect = (RectTransform)transform;
            _rect.pivot = new Vector2(0f, 0.5f);
            _rect.sizeDelta = new Vector2(barHeight * 10f, barHeight * 2f * sizeScale);
            _rect.localRotation = Quaternion.identity;
            _rect.localScale = Vector3.one;
            var local = canvas.InverseTransformPoint(anchorWorldPosition);
            // In front of the bar (z-fighting otherwise).
            _rect.localPosition = new Vector3(local.x, local.y, _inFrontOfBar);
            _startPosition = _rect.anchoredPosition;
            _riseDistance = barHeight * _riseHeights * sizeScale;

            _text = gameObject.AddComponent<TextMeshProUGUI>();
            GothicText.Apply(_text, isTinted: true);
            _text.text = FloatingHitNumber.GetText(amount, unit);
            _text.color = FloatingHitNumber.GetColor(amount, unit);
            _text.fontSize = barHeight * _fontSizeInBarHeights * sizeScale;
            _text.alignment = TextAlignmentOptions.Left;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.raycastTarget = false;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var t = _elapsed / _lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            _rect.anchoredPosition = _startPosition + Vector2.up * (_riseDistance * (1f - (1f - t) * (1f - t)));
            _text.alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
        }
    }
}
