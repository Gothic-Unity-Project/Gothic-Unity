using Gothic.Core.Const;
using Gothic.Core.Services.Config;
using TMPro;
using UnityEngine;

namespace Gothic.Core.Adapters.UI
{
    /// <summary>
    /// One "-12 HP" / "+5 MANA" number: rises a bit, always faces the camera, fades out and destroys itself.
    /// Stays where it was spawned (doesn't follow the NPC), like TF2 damage numbers.
    /// </summary>
    public class FloatingHitNumber : MonoBehaviour
    {
        private const float _lifetime = 1.3f;
        private const float _riseDistance = 0.45f;
        private const float _scale = 0.03f;
        private const float _fontSize = 20f;

        private static readonly Color _damageColor = new(1f, 0.25f, 0.2f);
        private static readonly Color _healColor = new(0.35f, 1f, 0.35f);
        private static readonly Color _manaColor = new(0.35f, 0.6f, 1f);
        public const string ManaUnit = "MANA";

        private TextMeshPro _text;
        private Vector3 _startPosition;
        private float _elapsed;

        public static void Spawn(Vector3 position, int amount, string unit)
        {
            var go = new GameObject($"HitNumber {amount} {unit}");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * (_scale * GetSizeScale());
            go.AddComponent<FloatingHitNumber>().Init(amount, unit);
        }

        /// <summary>
        /// Slider "hitNumbersSize" in our settings menu: 1 (smallest, default) ... HitNumbersMaxScale.
        /// </summary>
        public static float GetSizeScale()
        {
            var configService = ReflexProjectInstaller.DIContainer.Resolve<ConfigService>();
            var size = Mathf.Clamp01(configService.Gothic.GetFloat(Constants.IniNames.HitNumbersSize, 0f));
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

        private void Init(int amount, string unit)
        {
            _startPosition = transform.position;

            _text = gameObject.AddComponent<TextMeshPro>();
            GothicText.Apply(_text, isTinted: true);
            _text.text = GetText(amount, unit);
            _text.color = GetColor(amount, unit);
            _text.fontSize = _fontSize;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;

            FaceCamera();
        }

        private void LateUpdate()
        {
            _elapsed += Time.deltaTime;
            var t = _elapsed / _lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // Fast start, slow end - the number "pops" up and settles.
            transform.position = _startPosition + Vector3.up * (_riseDistance * (1f - (1f - t) * (1f - t)));
            _text.alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;

            FaceCamera();
        }

        private void FaceCamera()
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            // TMP text is read along its +Z axis, so it looks away from the camera.
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
