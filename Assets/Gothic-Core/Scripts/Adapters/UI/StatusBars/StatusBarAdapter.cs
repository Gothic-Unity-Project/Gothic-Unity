using System;
using System.Collections;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.UI.StatusBars
{
    public class StatusBarAdapter : MonoBehaviour
    {
        [SerializeField] private StatusType _statusType;
        public StatusType Type => _statusType;
        private Coroutine _fadeCoroutine;
        [SerializeField] private bool _isPlayer;
        [SerializeField] private Image _background;
        [SerializeField] private Image _statusValue;

        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly ConfigService _configService;

        private const string _nameTextName = "NameText";
        private const float _textInFrontOfBar = -0.5f;

        private NpcContainer _owner;
        // "HP/MAX" (health bars) or "MANA/MAX" (the hero's mana bar) - option "healthNumbers".
        private TMP_Text _numbers;
        private bool _isNumbersShown;
        private int _numbersValue;
        private int _numbersMax;
        private bool _isInWorld;
        private bool _isVideoPlaying;

        public enum StatusType
        {
            Health,
            Mana,
            Misc
        }

        public bool IsPlayer => _isPlayer;

        private bool HasNumbers => _statusType == StatusType.Health || (_statusType == StatusType.Mana && _isPlayer);

        private void OnDestroy()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);
        }

        private void Awake()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);

            // Player is spawned before ZenKit (with Gothic version) is bootstrapped.
            if (_isPlayer)
            {
                // Health is visible during gameplay only (as in G1), not in menus or while loading.
                // TODO - Potentially only show it when a weapon is drawn in the future (immersion update).
                DisableBar();

                // If we load it for the player, ZenKit is initialized later therefore, we need to delay the startup execution.
                GlobalEventDispatcher.ZenKitBootstrapped.AddListener(StartInternal);

                // A loaded save replaces the hero.
                if (_statusType == StatusType.Health)
                {
                    GlobalEventDispatcher.WorldSceneLoaded.AddListener(OnWorldSceneLoaded);
                    GlobalEventDispatcher.MainMenuSceneLoaded.AddListener(OnMenuOrLoadingSceneLoaded);
                    GlobalEventDispatcher.LoadingSceneLoaded.AddListener(OnMenuOrLoadingSceneLoaded);

                    // Videos (new game intro, chapter videos) are watched without the bar.
                    GlobalEventDispatcher.PlayVideo.AddListener(OnPlayVideo);
                    GlobalEventDispatcher.VideoPlaybackFinished.AddListener(OnVideoPlaybackFinished);
                }
            }
            else
            {
                // Same exception as the player above: Health stays visible. Nothing re-enables a disabled NPC
                // health bar later on.
                if (_statusType != StatusType.Health)
                {
                    DisableBar();
                }

                StartInternal();
            }
        }

        private void StartInternal()
        {
            _background.material = _textureService.StatusBarBackgroundMaterial;

            // FIXME - Set fill amount based on current value when NPC/Monster is spawned.
            _statusValue.fillAmount = 1f;
            _statusValue.material = _statusType switch
            {
                StatusType.Health => _textureService.StatusBarHealthMaterial,
                StatusType.Mana => _textureService.StatusBarManaMaterial,
                StatusType.Misc => _textureService.StatusBarMiscMaterial,
                _ => throw new ArgumentOutOfRangeException()
            };

            switch (_statusType)
            {
                case StatusType.Health:
                    break;
                case StatusType.Mana:
                    break;
                case StatusType.Misc:
                    StartCoroutine(HandleDiveValue());
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Health (and the hero's mana numbers) follow NpcAttributeChanged while enabled. Culled NPCs are disabled -
        /// they refresh when re-enabled.
        /// </summary>
        private void OnEnable()
        {
            if (!HasNumbers)
                return;

            GlobalEventDispatcher.NpcAttributeChanged.AddListener(OnNpcAttributeChanged);
            if (_isPlayer && _statusType == StatusType.Health)
                GlobalEventDispatcher.NpcAttributeAmountChanged.AddListener(OnNpcAttributeAmountChanged);
            Refresh();
        }

        private void OnDisable()
        {
            if (!HasNumbers)
                return;

            GlobalEventDispatcher.NpcAttributeChanged.RemoveListener(OnNpcAttributeChanged);
            GlobalEventDispatcher.NpcAttributeAmountChanged.RemoveListener(OnNpcAttributeAmountChanged);
        }

        private void OnNpcAttributeChanged(NpcContainer npc, NpcAttribute attribute)
        {
            var isMine = _statusType == StatusType.Health
                ? attribute is NpcAttribute.HitPoints or NpcAttribute.HitPointsMax
                : attribute is NpcAttribute.Mana or NpcAttribute.ManaMax;
            if (!isMine || npc != GetOwner())
                return;

            Refresh();
        }

        /// <summary>
        /// "-12 HP" / "+5 MANA" next to the hero's health bar (option "hitNumbers", like NpcHitNumbers over NPCs).
        /// </summary>
        private void OnNpcAttributeAmountChanged(NpcContainer npc, NpcAttribute attribute, int amount)
        {
            if (attribute is not (NpcAttribute.HitPoints or NpcAttribute.Mana) || npc != GetOwner())
                return;

            ShowHeroHitNumber(amount, attribute == NpcAttribute.Mana ? FloatingHitNumber.ManaUnit : "HP");
        }

        private void OnWorldSceneLoaded()
        {
            _isInWorld = true;
            if (!_isVideoPlaying)
                EnableBar();
            Refresh();
        }

        private void OnMenuOrLoadingSceneLoaded()
        {
            _isInWorld = false;
            DisableBar();
        }

        private void OnPlayVideo(string fileName)
        {
            _isVideoPlaying = true;
            DisableBar();
        }

        private void OnVideoPlaybackFinished()
        {
            _isVideoPlaying = false;
            if (_isInWorld)
                EnableBar();
        }

        /// <summary>
        /// Health: the fill and the numbers. The hero's mana bar: only the numbers - whoever shows it sets the fill.
        /// </summary>
        private void Refresh()
        {
            var owner = GetOwner();
            if (owner == null)
                return;

            if (_statusType == StatusType.Health)
            {
                _numbersValue = owner.Vob.GetAttribute((int)NpcAttribute.HitPoints);
                _numbersMax = owner.Vob.GetAttribute((int)NpcAttribute.HitPointsMax);
                SetFillAmount(_numbersValue, _numbersMax);
            }
            else
            {
                _numbersValue = owner.Vob.GetAttribute((int)NpcAttribute.Mana);
                _numbersMax = owner.Vob.GetAttribute((int)NpcAttribute.ManaMax);
            }
            UpdateNumbers();
        }

        /// <summary>
        /// "HP/MAX" on the bar - option "healthNumbers" in our settings menu (Gothic.ini, off by default).
        /// The text is created on first use: Gothic's font only exists after the game booted.
        /// </summary>
        private void UpdateNumbers()
        {
            _isNumbersShown = _configService.Gothic != null &&
                              _configService.Gothic.GetInt(Constants.IniNames.HealthNumbers, 0) == 1;
            if (_isNumbersShown && _numbers == null)
                _numbers = CreateNumbersText();
            if (_numbers == null)
                return;

            _numbers.text = $"{_numbersValue}/{_numbersMax}";
            _numbers.enabled = _isNumbersShown && _background.enabled;
        }

        /// <summary>
        /// NPC bars sit in the focus NameCanvas next to the NPC's >NameText<: the numbers copy its layout and go right
        /// above the name. (Not under the bar itself - its background is rotated 180 degrees, the text came out
        /// mirrored.) The player's bars have no name - there the numbers fill the bar.
        /// </summary>
        private TMP_Text CreateNumbersText()
        {
            var textGo = new GameObject("HealthNumbers", typeof(RectTransform));
            textGo.layer = gameObject.layer;
            var rect = (RectTransform)textGo.transform;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            GothicText.Apply(text);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            var nameText = FindNameText();
            if (nameText != null)
            {
                var nameRect = (RectTransform)nameText.transform;
                rect.SetParent(nameRect.parent, false);
                rect.anchorMin = nameRect.anchorMin;
                rect.anchorMax = nameRect.anchorMax;
                rect.pivot = nameRect.pivot;
                rect.sizeDelta = nameRect.sizeDelta;
                rect.localRotation = nameRect.localRotation;
                rect.localScale = nameRect.localScale;
                rect.anchoredPosition = nameRect.anchoredPosition + Vector2.up * nameRect.rect.height;
                text.fontSize = nameText.fontSize;
                return text;
            }

            rect.SetParent(_background.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            // In front of the bar - on it, the text z-fought with the bar's own image.
            rect.anchoredPosition3D = new Vector3(0f, 0f, _textInFrontOfBar);
            text.enableAutoSizing = true;
            text.fontSizeMin = 1f;
            text.fontSizeMax = 200f;
            return text;
        }

        private TMP_Text FindNameText()
        {
            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                var nameTransform = parent.Find(_nameTextName);
                if (nameTransform != null)
                    return nameTransform.GetComponent<TMP_Text>();
            }
            return null;
        }

        /// <summary>
        /// Starts at the bar's right end - found via its corners in canvas space, the bar's background can be rotated.
        /// </summary>
        private void ShowHeroHitNumber(int amount, string unit)
        {
            if (_configService.Gothic.GetInt(Constants.IniNames.HitNumbers, 0) != 1 || !_background.enabled)
                return;

            var parentCanvas = GetComponentInParent<Canvas>();
            var canvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
            if (canvas == null)
            {
                Logger.LogWarning("[StatusBarAdapter] Hero bar has no canvas - no hit numbers.", LogCat.Ui);
                return;
            }

            var canvasRect = (RectTransform)canvas.transform;
            var corners = new Vector3[4];
            _background.rectTransform.GetWorldCorners(corners);
            var right = float.MinValue;
            var bottom = float.MaxValue;
            var top = float.MinValue;
            foreach (var corner in corners)
            {
                var local = canvasRect.InverseTransformPoint(corner);
                right = Mathf.Max(right, local.x);
                bottom = Mathf.Min(bottom, local.y);
                top = Mathf.Max(top, local.y);
            }

            var barHeight = top - bottom;
            var anchor = canvasRect.TransformPoint(new Vector3(right + barHeight * 0.5f, (top + bottom) / 2f, 0f));
            FloatingUiHitNumber.Spawn(canvasRect, anchor, barHeight, amount, unit);
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == Constants.IniNames.HealthNumbers && HasNumbers && GetOwner() != null)
                UpdateNumbers();
        }

        /// <summary>
        /// The player's bar follows the current hero (none in menus, a loaded save replaces it). NPC bars are placed
        /// below their NPC's prefab.
        /// </summary>
        private NpcContainer GetOwner()
        {
            if (_isPlayer)
                return _npcService?.GetHeroContainer();

            if (_owner == null)
                _owner = GetComponentInParent<NpcLoader>()?.Container;
            return _owner;
        }

        private IEnumerator HandleDiveValue()
        {
            while (true)
            {
                if (!_playerService.IsDiving && _statusValue.enabled)
                {
                    DisableBar();
                }
                else if (_playerService.IsDiving)
                {
                    SetFillAmount(_playerService.CurrentAir, _playerService.MaxAir);

                    if (!_statusValue.enabled)
                        EnableBar();
                }

                yield return null;
            }
            // ReSharper disable once IteratorNeverReturns
        }

        public void SetFillAmount(float current, float max)
        {
            _statusValue.fillAmount = current / max;
        }

        public void DisableBar()
        {
            _background.enabled = false;
            _statusValue.enabled = false;
            if (_numbers != null)
                _numbers.enabled = false;
        }

        public void EnableBar()
        {
            _background.enabled = true;
            _statusValue.enabled = true;
            if (_numbers != null)
                _numbers.enabled = _isNumbersShown;
        }

        /// <summary>
        /// VRRuneCaster: the hero's mana bar shows while a spell is charged.
        /// </summary>
        public void FadeIn(float duration = 0.25f)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine(0f, 1f, duration, enable: true));
        }

        public void FadeOut(float duration = 0.25f)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine(1f, 0f, duration, enable: false));
        }

        private IEnumerator FadeRoutine(float from, float to, float duration, bool enable)
        {
            if (enable)
                EnableBar();

            SetAlpha(from);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                SetAlpha(Mathf.Lerp(from, to, elapsed / duration));
                elapsed += Time.deltaTime;
                yield return null;
            }
            SetAlpha(to);

            if (!enable)
                DisableBar();
        }

        private void SetAlpha(float alpha)
        {
            var background = _background.color;
            background.a = alpha;
            _background.color = background;

            var value = _statusValue.color;
            value.a = alpha;
            _statusValue.color = value;
        }
    }
}
