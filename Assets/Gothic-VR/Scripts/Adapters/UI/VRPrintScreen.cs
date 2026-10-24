using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Adapters.UI;
using Gothic.Core.Models.UI;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.UI;
using Reflex.Attributes;
using TMPro;
using UnityEngine;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Shows PrintScreen() messages in front of the VR camera. Gothic's screen positions (percent) are mapped onto a
    /// virtual screen inside the comfortable field of view. The screen follows the head (VRFollowCamera) and is only
    /// active while messages are visible. Message texts are pooled copies of the inactive template.
    /// The same text again only extends its line - like the engine, where a repeated PrintScreen draws over itself.
    /// Drawn on top of the world (VRUiOnTop), so walls and NPCs don't cut through it.
    /// </summary>
    public class VRPrintScreen : MonoBehaviour
    {
        [SerializeField] private RectTransform _screen;
        [SerializeField] private TMP_Text _messageTemplate;
        [SerializeField] private float _minSeconds = 1f;
        [SerializeField] private VRUiOnTop _onTop;
        [SerializeField] private VRFollowCamera _followCamera;

        [Inject] private readonly FontService _fontService;

        private readonly List<(TMP_Text Text, float HideAt)> _activeMessages = new();
        private readonly Stack<TMP_Text> _pool = new();

        public void Init()
        {
            _screen.gameObject.SetActive(false);
            _followCamera.enabled = false;
            // New lines are clones of the template - VRUiOnTop wraps their fonts when they're generated.
            _onTop.Apply();

            GlobalEventDispatcher.PrintScreen.AddListener(OnPrintScreen);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.PrintScreen.RemoveListener(OnPrintScreen);
        }

        private void Update()
        {
            if (_activeMessages.Count == 0)
                return;

            HideExpiredMessages();
        }

        private void OnPrintScreen(PrintScreenMessage message)
        {
            var hideAt = Time.time + Mathf.Max(message.Seconds, _minSeconds);
            for (var i = 0; i < _activeMessages.Count; i++)
            {
                var (activeText, activeHideAt) = _activeMessages[i];
                if (activeText.text != message.Text)
                    continue;
                _activeMessages[i] = (activeText, Mathf.Max(activeHideAt, hideAt));
                return;
            }

            if (_activeMessages.Count == 0)
            {
                _screen.gameObject.SetActive(true);
                _followCamera.enabled = true;
            }

            var text = _pool.Count > 0 ? _pool.Pop() : Instantiate(_messageTemplate, _screen, false);
            SetupText(text, message);
            text.gameObject.SetActive(true);

            _activeMessages.Add((text, hideAt));
        }

        private void SetupText(TMP_Text text, PrintScreenMessage message)
        {
            var isCenteredX = message.PosX == PrintScreenMessage.Centered;
            var isCenteredY = message.PosY == PrintScreenMessage.Centered;

            GothicText.Apply(text);
            text.spriteAsset = _fontService.TryGetFont(message.Font) ?? _fontService.DefaultSpriteAsset;
            text.fontSize = message.Font.ToLowerInvariant().Contains("font_old_10_white") ? 16 : 36;
            text.horizontalAlignment = isCenteredX ? HorizontalAlignmentOptions.Center : HorizontalAlignmentOptions.Left;
            text.verticalAlignment = isCenteredY ? VerticalAlignmentOptions.Middle : VerticalAlignmentOptions.Top;
            text.text = message.Text;

            // Gothic places the top left corner of the text at the position. Centered axes use the text's center.
            var rect = text.rectTransform;
            var anchor = new Vector2(isCenteredX ? 0.5f : message.PosX / 100f,
                isCenteredY ? 0.5f : 1f - message.PosY / 100f);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(isCenteredX ? 0.5f : 0f, isCenteredY ? 0.5f : 1f);
            // The text is large (readable in VR). Long lines wrap inside the remaining width instead of leaving view.
            text.textWrappingMode = TextWrappingModes.Normal;
            var width = isCenteredX ? _screen.rect.width : _screen.rect.width * (1f - message.PosX / 100f);
            rect.sizeDelta = new Vector2(Mathf.Max(width, text.fontSize), text.fontSize * 2);
            rect.anchoredPosition = Vector2.zero;
        }

        private void HideExpiredMessages()
        {
            for (var i = _activeMessages.Count - 1; i >= 0; i--)
            {
                var (text, hideAt) = _activeMessages[i];
                if (Time.time < hideAt)
                    continue;

                text.gameObject.SetActive(false);
                _pool.Push(text);
                _activeMessages.RemoveAt(i);
            }

            if (_activeMessages.Count == 0)
            {
                _screen.gameObject.SetActive(false);
                _followCamera.enabled = false;
            }
        }
    }
}
