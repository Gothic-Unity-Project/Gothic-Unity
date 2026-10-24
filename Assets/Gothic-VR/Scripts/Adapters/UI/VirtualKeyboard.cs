using System;
using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Adapters.UI;
using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.VR.Models.UI;
#if GOTHIC_HVR_INSTALLED
using HurricaneVR.Framework.Core.UI;
#endif
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// An on-screen keyboard in Gothic's font (like SteamVR's) - a panel with the typed text, letters, digits, shift,
    /// space, backspace, OK and cancel, clicked with the UI laser. It shows in front of the camera (or at a follow
    /// target).
    /// Optional: a symbol row (_ - . , : +), forced upper case, a max length, tappable suggestions (e.g. for
    /// cheat commands like "insert ITPO_MANA_03 5" or "goto pos -1200.5 300 40"), a key filter greying out keys
    /// and a submit condition.
    /// Layout lives in the prefab (PrefabType.UiVirtualKeyboard, VR/Prefabs/UI), the optional rows collapse via
    /// layout groups.
    /// </summary>
    public class VirtualKeyboard : MonoBehaviour
    {
        public const int DefaultMaxLength = 30;
        private const float _distance = 1.2f;
        private const float _heightBelowEyes = 0.25f;
        private const float _disabledLabelAlpha = 0.25f;

        [SerializeField] private TMP_Text _display;
        [SerializeField] private GameObject _suggestionRow;
        [SerializeField] private Button[] _suggestionButtons;
        [SerializeField] private GameObject _symbolRow;
        [SerializeField] private Button _shiftButton;
        [SerializeField] private Button _spaceButton;
        [SerializeField] private Button _backspaceButton;
        [SerializeField] private Button _submitButton;
        [SerializeField] private Button _cancelButton;

        private static VirtualKeyboard _current;

        private string _text = string.Empty;
        private bool _isUpperCase;
        private int _maxLength = DefaultMaxLength;
        private bool _isForcedUpperCase;
        private bool _isSymbolRowShown;
        private bool _isClosed;
        private bool _isClosedOnSubmit = true;
        private Transform _followTarget;
        private bool _hasFollowTarget;
        private Action<string> _onSubmit;
        private Action _onCancel;
        private Action<string> _onTextChanged;
        private Func<string, IReadOnlyList<string>> _suggestionProvider;
        private Func<string, char, bool> _keyFilter;
        private Func<string, bool> _canSubmit;
        private VirtualKeyboardKey[] _keys;
        private TMP_Text _spaceLabel;
        private readonly List<string> _suggestions = new();

        public string Text => _text;

        /// <summary>
        /// Short form of Show(VirtualKeyboardOptions) - see VirtualKeyboardOptions for the parameters.
        /// </summary>
        public static void Show(string initialText, Action<string> onSubmit, Action onCancel,
            int maxLength = DefaultMaxLength, bool forceUpperCase = false, bool showSymbolRow = false,
            Func<string, IReadOnlyList<string>> suggestionProvider = null)
        {
            Show(new VirtualKeyboardOptions
            {
                InitialText = initialText,
                OnSubmit = onSubmit,
                OnCancel = onCancel,
                MaxLength = maxLength,
                IsForcedUpperCase = forceUpperCase,
                IsSymbolRowShown = showSymbolRow,
                SuggestionProvider = suggestionProvider
            });
        }

        public static VirtualKeyboard Show(VirtualKeyboardOptions options)
        {
            if (_current != null)
                _current.Close();

            var resourceCacheService = ReflexProjectInstaller.DIContainer.Resolve<ResourceCacheService>();
            var go = resourceCacheService.TryGetPrefabObject(PrefabType.UiVirtualKeyboard);
            _current = go.GetComponent<VirtualKeyboard>();
            _current.Init(options);
            return _current;
        }

        private void Init(VirtualKeyboardOptions options)
        {
            _onSubmit = options.OnSubmit;
            _onCancel = options.OnCancel;
            _onTextChanged = options.OnTextChanged;
            _maxLength = Mathf.Max(1, options.MaxLength);
            _isForcedUpperCase = options.IsForcedUpperCase;
            _isUpperCase = options.IsForcedUpperCase;
            _isSymbolRowShown = options.IsSymbolRowShown;
            _suggestionProvider = options.SuggestionProvider;
            _keyFilter = options.KeyFilter;
            _canSubmit = options.CanSubmit;
            _isClosedOnSubmit = options.IsClosedOnSubmit;
            _followTarget = options.FollowTarget;
            _hasFollowTarget = _followTarget != null;
            _text = Clamp(ApplyForcedCase(options.InitialText ?? string.Empty));

            if (_hasFollowTarget)
                FollowTarget();
            else
                PlaceInFrontOfCamera();

            foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                GothicText.Apply(text);

            _symbolRow.SetActive(_isSymbolRowShown);
            _suggestionRow.SetActive(_suggestionProvider != null);

            _keys = GetComponentsInChildren<VirtualKeyboardKey>(true);
            _spaceLabel = _spaceButton.GetComponentInChildren<TMP_Text>(true);
            foreach (var key in _keys)
            {
                var character = key.Character;
                key.Button.onClick.AddListener(() => Type(character));
            }
            RefreshKeyLabels();

            for (var i = 0; i < _suggestionButtons.Length; i++)
            {
                var index = i;
                _suggestionButtons[i].onClick.AddListener(() => ApplySuggestion(index));
            }

            _shiftButton.onClick.AddListener(ToggleCase);
            _spaceButton.onClick.AddListener(() => Type(' '));
            _backspaceButton.onClick.AddListener(Backspace);
            _submitButton.onClick.AddListener(Submit);
            _cancelButton.onClick.AddListener(Cancel);

            // The optional rows changed - recalculate the panel size before the laser sees it.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);

            // DeveloperConfig.EnableUiGrabBars: following an anchor (Marvin), it's moved with the anchor's bar.
            if (!_hasFollowTarget && ReflexProjectInstaller.DIContainer.Resolve<ConfigService>().Dev.EnableUiGrabBars)
                VRGrabBar.Create((RectTransform)transform, transform);

            RefreshDisplay();
#if GOTHIC_HVR_INSTALLED
            // A canvas created at runtime: the UI laser only hits canvases HVRInputModule knows.
            if (HVRInputModule.Instance != null)
                HVRInputModule.Instance.AddCanvas(GetComponent<Canvas>());
#endif
            Logger.Log("[VirtualKeyboard] Shown", LogCat.Ui);
        }

        private void PlaceInFrontOfCamera()
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = cam.transform.up;
            transform.position = cam.transform.position + forward * _distance + Vector3.down * _heightBelowEyes;
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        /// <summary>
        /// Replaces the text from outside (forced case and max length apply). Doesn't call OnTextChanged.
        /// </summary>
        public void SetText(string text)
        {
            _text = Clamp(ApplyForcedCase(text ?? string.Empty));
            RefreshDisplay();
        }

        private void Type(char character)
        {
            if (_text.Length >= _maxLength)
                return;
            var typed = GetTypedCharacter(character);
            if (_keyFilter != null && !_keyFilter(_text, typed))
                return;

            _text += typed;
            RefreshDisplay();
            _onTextChanged?.Invoke(_text);
        }

        private char GetTypedCharacter(char character)
        {
            return char.IsLetter(character) && _isUpperCase ? char.ToUpperInvariant(character) : character;
        }

        private void ToggleCase()
        {
            if (_isForcedUpperCase)
                return;
            _isUpperCase = !_isUpperCase;
            RefreshKeyLabels();
            RefreshKeyStates();
        }

        private void RefreshKeyLabels()
        {
            foreach (var key in _keys)
            {
                var character = key.Character;
                if (char.IsLetter(character) && key.Label != null)
                    key.Label.text = GetTypedCharacter(character).ToString();
            }
        }

        private void Backspace()
        {
            if (_text.Length == 0)
                return;
            _text = _text.Substring(0, _text.Length - 1);
            RefreshDisplay();
            _onTextChanged?.Invoke(_text);
        }

        private void ApplySuggestion(int index)
        {
            if (index < 0 || index >= _suggestions.Count)
                return;
            _text = Clamp(ApplyForcedCase(_suggestions[index]));
            RefreshDisplay();
            _onTextChanged?.Invoke(_text);
        }

        private void RefreshDisplay()
        {
            _display.text = _text + "_";
            RefreshKeyStates();
            RefreshSuggestions();
        }

        private void RefreshKeyStates()
        {
            foreach (var key in _keys)
                SetKeyInteractable(key.Button, key.Label, IsKeyAllowed(key.Character));

            SetKeyInteractable(_spaceButton, _spaceLabel, IsKeyAllowed(' '));
            _backspaceButton.interactable = _text.Length > 0;
            _submitButton.interactable = IsSubmitAllowed();
        }

        private bool IsKeyAllowed(char character)
        {
            return _keyFilter == null || _keyFilter(_text, GetTypedCharacter(character));
        }

        private bool IsSubmitAllowed()
        {
            return _canSubmit == null || _canSubmit(_text);
        }

        private static void SetKeyInteractable(Button button, TMP_Text label, bool isInteractable)
        {
            button.interactable = isInteractable;
            // A TMP label doesn't follow the Button tint.
            if (label == null)
                return;

            var color = label.color;
            color.a = isInteractable ? 1f : _disabledLabelAlpha;
            label.color = color;
        }

        private void RefreshSuggestions()
        {
            if (_suggestionProvider == null)
                return;

            _suggestions.Clear();
            try
            {
                var suggestions = _suggestionProvider(_text);
                if (suggestions != null)
                {
                    foreach (var suggestion in suggestions)
                    {
                        if (_suggestions.Count >= _suggestionButtons.Length)
                            break;
                        if (!string.IsNullOrEmpty(suggestion))
                            _suggestions.Add(suggestion);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[VirtualKeyboard] Suggestion provider failed: {e.Message}", LogCat.Ui);
            }

            for (var i = 0; i < _suggestionButtons.Length; i++)
            {
                var isUsed = i < _suggestions.Count;
                // Unused buttons are hidden, the row keeps its height (LayoutElement on the row).
                _suggestionButtons[i].gameObject.SetActive(isUsed);
                if (isUsed)
                    _suggestionButtons[i].GetComponentInChildren<TMP_Text>(true).text = _suggestions[i];
            }
        }

        private void Submit()
        {
            if (!IsSubmitAllowed())
                return;

            var text = _text.Trim();
            var onSubmit = _onSubmit;
            Logger.Log($"[VirtualKeyboard] Submitted '{text}'", LogCat.Ui);
            if (_isClosedOnSubmit)
                Close();
            onSubmit?.Invoke(text);
        }

        private void Cancel()
        {
            var onCancel = _onCancel;
            Logger.Log("[VirtualKeyboard] Cancelled", LogCat.Ui);
            Close();
            onCancel?.Invoke();
        }

        private void LateUpdate()
        {
            if (!_hasFollowTarget || _isClosed)
                return;

            // Unity null check: the anchor is destroyed (e.g. the hand/canvas it belonged to).
            if (_followTarget == null)
                Close();
            else
                FollowTarget();
        }

        /// <summary>
        /// The anchor's world scale too: a hand menu canvas is much smaller than the keyboard's own scale.
        /// </summary>
        private void FollowTarget()
        {
            transform.SetPositionAndRotation(_followTarget.position, _followTarget.rotation);
            transform.localScale = _followTarget.lossyScale;
        }

        private string ApplyForcedCase(string text)
        {
            return _isForcedUpperCase ? text.ToUpperInvariant() : text;
        }

        private string Clamp(string text)
        {
            return text.Length > _maxLength ? text.Substring(0, _maxLength) : text;
        }

        public void Close()
        {
            if (_isClosed)
                return;
            _isClosed = true;

#if GOTHIC_HVR_INSTALLED
            if (HVRInputModule.Instance != null)
                HVRInputModule.Instance.RemoveCanvas(GetComponent<Canvas>());
#endif
            if (_current == this)
                _current = null;
            _onSubmit = null;
            _onCancel = null;
            _onTextChanged = null;
            _suggestionProvider = null;
            _keyFilter = null;
            _canSubmit = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_current == this)
                _current = null;
        }
    }
}
