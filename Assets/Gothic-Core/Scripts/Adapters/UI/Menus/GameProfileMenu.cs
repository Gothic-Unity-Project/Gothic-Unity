using System;
using System.Collections.Generic;
using Gothic.Core.Models.Config;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Gothic.Core.Adapters.UI.Menus
{
    /// <summary>
    /// World-space list of GameSettings.json game profiles shown at start (EnableGameProfiles). Built in code so the
    /// GameVersion scene stays untouched; one button per profile, the VR laser / flat mouse clicks it. Also the second
    /// list of a mod's game inis (DeveloperConfig.EnableGameProfileIniChoice) with a back button.
    /// </summary>
    public class GameProfileMenu : MonoBehaviour
    {
        private const float _pixelsPerMeter = 1000f;
        private const float _distanceToCamera = 1.2f;
        private const float _width = 640f;
        private const float _buttonHeight = 70f;
        private const float _buttonSpacing = 15f;
        private const string _fontPath = "Fonts & Materials/LiberationSans SDF";

        private const string _backLabel = "< Back";
        // DeveloperConfig.EnableProfileMenuTextFit: the smallest font a long title shrinks to, its side margin.
        private const float _minFontSize = 16f;
        private const float _textMargin = 10f;

        // DeveloperConfig.EnableGameVersionScenePlacement: the scene's teleport lands a frame or more later (XRI) - the
        // menu was placed in front of the falling camera, below the floor. It follows the camera this long.
        private const float _followCameraSeconds = 0.5f;
        private float _followCameraUntil;

        private Action<JsonGameProfile> _onSelected;
        private Action _onBack;
        private bool _isSelected;

        public void Build(IReadOnlyList<JsonGameProfile> profiles, Action<JsonGameProfile> onSelected,
            Action onBack = null)
        {
            _onSelected = onSelected;
            _onBack = onBack;

            var buttonCount = profiles.Count + (onBack != null ? 1 : 0);
            var height = buttonCount * (_buttonHeight + _buttonSpacing) + _buttonSpacing;
            var rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(_width, height);
            transform.localScale = Vector3.one / _pixelsPerMeter;
            gameObject.layer = LayerMask.NameToLayer("UI");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            gameObject.AddComponent<GraphicRaycaster>();

            var background = gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.8f);

            for (var i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                var y = height / 2f - _buttonSpacing - i * (_buttonHeight + _buttonSpacing) - _buttonHeight / 2f;
                CreateButton(profile.Name, new Vector2(0f, y), () => Select(profile));
            }
            if (onBack != null)
            {
                var backY = height / 2f - _buttonSpacing - profiles.Count * (_buttonHeight + _buttonSpacing) -
                            _buttonHeight / 2f;
                CreateButton(_backLabel, new Vector2(0f, backY), Back);
            }

            PlaceInFrontOfCamera();
            var config = ReflexProjectInstaller.DIContainer?.Resolve<Services.Config.ConfigService>();
            if (config != null && config.Dev.EnableGameVersionScenePlacement)
                _followCameraUntil = Time.unscaledTime + _followCameraSeconds;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _followCameraUntil)
                PlaceInFrontOfCamera();
        }

        private void Select(JsonGameProfile profile)
        {
            // A second click while the game boots would init everything twice.
            if (_isSelected)
                return;

            _isSelected = true;
            gameObject.SetActive(false);
            _onSelected?.Invoke(profile);
        }

        private void Back()
        {
            if (_isSelected)
                return;

            _isSelected = true;
            gameObject.SetActive(false);
            _onBack?.Invoke();
        }

        private void PlaceInFrontOfCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                transform.SetPositionAndRotation(new Vector3(0f, 1.5f, _distanceToCamera), Quaternion.identity);
                return;
            }

            var forward = cam.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.001f ? Vector3.forward : forward.normalized;

            // A world-space canvas is read along its +Z axis, so it looks away from the camera.
            transform.SetPositionAndRotation(cam.transform.position + forward * _distanceToCamera,
                Quaternion.LookRotation(forward));
        }

        private void CreateButton(string label, Vector2 position, UnityAction onClick)
        {
            var buttonGo = new GameObject($"Profile_{label}", typeof(RectTransform));
            buttonGo.layer = gameObject.layer;
            buttonGo.transform.SetParent(transform, false);
            var rect = (RectTransform)buttonGo.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(_width - 2 * _buttonSpacing, _buttonHeight);

            var image = buttonGo.AddComponent<Image>();
            image.color = Color.white;

            // The button tint multiplies the image color, so the image stays white and the tint carries the color.
            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(0.25f, 0.2f, 0.15f, 0.95f);
            colors.highlightedColor = new Color(0.55f, 0.42f, 0.25f, 1f);
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = new Color(0.7f, 0.55f, 0.3f, 1f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            CreateText(buttonGo.transform, label, Vector2.zero, rect.sizeDelta, 34, FontStyles.Normal);
        }

        private void CreateText(Transform parent, string content, Vector2 position, Vector2 size, float fontSize,
            FontStyles style)
        {
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.layer = gameObject.layer;
            textGo.transform.SetParent(parent, false);
            var rect = (RectTransform)textGo.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = textGo.AddComponent<TextMeshProUGUI>();
            // TMP's default font isn't found in this project; Gothic fonts only exist after the game booted.
            text.font = Resources.Load<TMP_FontAsset>(_fontPath);
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;

            // DeveloperConfig.EnableProfileMenuTextFit: a long title (a mod's ini: "Gothic: Mroczne Tajemnice (Poziom
            // łatwy, Rockefeller) (DM_E_Rockefeller.INI)") wrapped into three lines and ran over the next button -
            // it shrinks to fit the button instead.
            if (ReflexProjectInstaller.DIContainer?.Resolve<Services.Config.ConfigService>()?.Dev?
                    .EnableProfileMenuTextFit == true)
            {
                text.enableAutoSizing = true;
                text.fontSizeMax = fontSize;
                text.fontSizeMin = _minFontSize;
                text.margin = new Vector4(_textMargin, 0f, _textMargin, 0f);
            }
        }
    }
}
