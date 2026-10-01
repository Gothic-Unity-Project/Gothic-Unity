#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core.Manager;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableScreenMessages): Gothic's PrintScreen texts ("New log entry", "1 item received", ...)
    /// shown as a small HUD in front of the VR head, plus Snd_Play 2D sounds ("LogEntry").
    /// Lines stack downwards, each disappears after its own time. Drawn on top of the world (ignores depth).
    /// The same text again only extends its line - like the engine, where a repeated PrintScreen draws over itself.
    /// Scripts can print every AI tick (G2 with PC_Rockefeller) - a new line + materials per call made the game lag.
    /// </summary>
    public class VRScreenMessages : MonoBehaviour
    {
        private const float _distance = 1.6f;
        private const float _verticalOffset = -0.25f; // Slightly below the view center - out of the way of NPCs' faces.
        private const float _smoothTime = 0.25f;
        private const float _canvasScale = 0.0015f;
        private const float _lineHeight = 34f;
        private const int _maxLines = 5;
        private const float _minSeconds = 2f;

        private static readonly int _guiZTestModeId = Shader.PropertyToID("unity_GUIZTestMode");

        private static VRScreenMessages _instance;
        private static Material _backgroundMaterial;
        private static Material _textMaterial;

        private readonly List<(TMP_Text text, float hideAt)> _lines = new();
        private AudioService _audioService;
        private AudioSource _audioSource;
        private RectTransform _content;
        private Vector3 _velocity;

        public static void ShowMessage(AudioService audioService, string message, int seconds)
        {
            GetInstance(audioService).AddLine(message, seconds);
        }

        public static void PlaySound(AudioService audioService, string soundName)
        {
            var instance = GetInstance(audioService);
            var clip = audioService.GetRandomSoundClip(soundName);
            if (clip != null)
                instance._audioSource.PlayOneShot(clip);
        }

        private static VRScreenMessages GetInstance(AudioService audioService)
        {
            if (_instance != null)
                return _instance;

            var go = new GameObject("_ScreenMessages");
            _instance = go.AddComponent<VRScreenMessages>();
            _instance._audioService = audioService;
            _instance.Build();
            return _instance;
        }

        private void Build()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.spatialBlend = 0f;
            _audioSource.playOnAwake = false;

            var canvasGo = new GameObject("_Canvas");
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localScale = Vector3.one * _canvasScale;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _content = canvas.GetComponent<RectTransform>();
            _content.sizeDelta = new Vector2(700, _lineHeight * _maxLines);
            _content.pivot = new Vector2(0.5f, 1f);

            SnapInFrontOfCamera();
        }

        private void AddLine(string message, int seconds)
        {
            var hideAt = Time.time + Mathf.Max(_minSeconds, seconds);
            for (var i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].text.text != message)
                    continue;
                _lines[i] = (_lines[i].text, Mathf.Max(_lines[i].hideAt, hideAt));
                return;
            }

            if (_lines.Count >= _maxLines)
                RemoveLine(0);

            var textGo = new GameObject("_Line");
            textGo.transform.SetParent(_content, false);

            var background = textGo.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.55f);
            if (_backgroundMaterial == null)
            {
                _backgroundMaterial = new Material(background.material);
                _backgroundMaterial.SetInt(_guiZTestModeId, (int)CompareFunction.Always);
            }
            background.material = _backgroundMaterial;

            var labelGo = new GameObject("_Text");
            labelGo.transform.SetParent(textGo.transform, false);
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            var font = Resources.Load<TMP_FontAsset>("FontAsset/LiberationSans SDF empty");
            if (font != null)
                text.font = font;
            text.fontSize = 22;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.text = message;
            // fontMaterial would create a material copy per line - share one.
            if (_textMaterial == null)
            {
                _textMaterial = new Material(text.fontSharedMaterial);
                _textMaterial.SetInt(_guiZTestModeId, (int)CompareFunction.Always);
            }
            text.fontSharedMaterial = _textMaterial;

            var labelRt = text.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            // Background as wide as the text.
            var rt = background.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(text.GetPreferredValues(message).x + 24f, _lineHeight - 4f);

            _lines.Add((text, hideAt));
            LayoutLines();
        }

        private void RemoveLine(int index)
        {
            Destroy(_lines[index].text.transform.parent.gameObject);
            _lines.RemoveAt(index);
        }

        private void LayoutLines()
        {
            for (var i = 0; i < _lines.Count; i++)
            {
                var rt = (RectTransform)_lines[i].text.transform.parent;
                rt.anchoredPosition = new Vector2(0f, -i * _lineHeight);
            }
        }

        private void Update()
        {
            var removed = false;
            for (var i = _lines.Count - 1; i >= 0; i--)
            {
                if (Time.time < _lines[i].hideAt)
                    continue;
                RemoveLine(i);
                removed = true;
            }
            if (removed)
                LayoutLines();

            var cam = Camera.main;
            if (cam == null)
                return;

            var target = GetTargetPosition(cam.transform);
            transform.position = Vector3.SmoothDamp(transform.position, target, ref _velocity, _smoothTime);
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        private void SnapInFrontOfCamera()
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            transform.position = GetTargetPosition(cam.transform);
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        private static Vector3 GetTargetPosition(Transform cam)
        {
            var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            if (forward == Vector3.zero)
                forward = cam.forward;
            return cam.position + forward * _distance + Vector3.up * _verticalOffset;
        }
    }
}
#endif
