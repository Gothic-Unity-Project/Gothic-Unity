using System;
using System.Collections;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Gothic.Core.Services.Config;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.UI.StatusBars
{
    public class StatusBarAdapter : MonoBehaviour
    {
        [SerializeField] private StatusType _statusType;
        [SerializeField] private bool _isPlayer;
        [SerializeField] private Image _background;
        [SerializeField] private Image _statusValue;

        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly NpcService _npcService;

        private Coroutine _fadeCoroutine;
        private NpcContainer _owner;
        private int _lastHitPoints = int.MinValue;
        private int _lastHitPointsMax = int.MinValue;
        private int _lastMana = int.MinValue;
        private int _healthOwnerWaitFrames;
        private const string _nameTextName = "NameText";
        private const float _textInFrontOfBar = -0.5f;
        private TMP_Text _healthNumbers;
        private bool _isHealthNumbersShown;
        private RectTransform _healthNumbersNameRect;
        private float _healthNumbersBaseFontSize;

        public enum StatusType
        {
            Health,
            Mana,
            Misc
        }

        public StatusType Type => _statusType;
        public bool IsPlayer => _isPlayer;

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
                // TODO - Keep health bar always active as in G1, potentially only enabling it when a weapon is drawn in the future (immersion update).
                if (_statusType != StatusType .Health)
                {
                    DisableBar();
                }

                // If we load it for the player, ZenKit is initialized later therefore, we need to delay the startup execution.
                GlobalEventDispatcher.ZenKitBootstrapped.AddListener(StartInternal);

            }
            else
            {
                // Same exception as the player above: Health stays visible (git history shows it was
                // never actually wired to re-enable on hit — SetFillAmount alone never touches
                // .enabled — so with DisableBar() unconditional here it could never have shown).
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
                    if (_isPlayer && _configService.Dev.HideHealthBar)
                        DisableBar();
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
        /// Health uses Update() instead of a coroutine (unlike HandleDiveValue) because NPCs get
        /// disabled/re-enabled by culling — a coroutine's iterator dies on disable and never resumes
        /// on its own, silently freezing the bar forever. Update() just stops and restarts for free.
        /// </summary>
        private void Update()
        {
            if (_statusType == StatusType.Mana && _isPlayer)
            {
                UpdateHeroManaNumbers();
                return;
            }
            if (_statusType != StatusType.Health) return;

            // Loading a save can replace the hero's container. A cached one would keep showing the old HP forever
            // (e.g. 1 HP after loading a save with full health) - so the player's bar always asks for the current hero.
            if (_isPlayer && _owner != null)
            {
                var currentHero = _npcService.GetHeroContainer();
                if (currentHero != null && currentHero != _owner)
                {
                    Logger.Log($"[StatusBarAdapter] '{name}' hero container changed (save loaded?) - rebinding.", LogCat.Ui);
                    _owner = currentHero;
                    _lastHitPoints = int.MinValue;
                    _lastMana = int.MinValue;
                }
            }

            if (_owner == null)
            {
                // The player's bar is a distinct prefab instance (_isPlayer), not nested under the NPC
                // prefab hierarchy at all — go straight to the hero container instead of hunting for an
                // NpcLoader ancestor that doesn't exist on the VR rig. Regular NPC bars ARE nested under
                // their NpcLoader-owning prefab (see BasePlayerBehaviours), so that lookup stays for them.
                _owner = _isPlayer ? _npcService.GetHeroContainer() : GetComponentInParent<NpcLoader>()?.Container;
                if (_owner == null)
                {
                    _healthOwnerWaitFrames++;
                    if (!_isPlayer && _healthOwnerWaitFrames == 300) // ~5s at 60fps — structurally wrong, not just late-init
                        Logger.LogWarning($"[StatusBarAdapter] '{name}' still has no NpcLoader owner after {_healthOwnerWaitFrames} frames — is this bar actually parented under the NPC hierarchy?", LogCat.Ui);
                    return;
                }
                Logger.Log($"[StatusBarAdapter] '{name}' health bar bound to '{(_isPlayer ? "hero" : _owner.Instance?.GetName(NpcNameSlot.Slot0))}' after {_healthOwnerWaitFrames} frame(s)", LogCat.Ui);
            }

            var hp = _owner.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var hpMax = _owner.Vob.GetAttribute((int)NpcAttribute.HitPointsMax);
            if (_isPlayer && _lastHitPoints != int.MinValue && hp != _lastHitPoints)
                ShowHeroHitNumber(hp - _lastHitPoints, "HP");

            if (_isPlayer)
            {
                var mana = _owner.Vob.GetAttribute((int)NpcAttribute.Mana);
                if (_lastMana != int.MinValue && mana != _lastMana)
                    ShowHeroHitNumber(mana - _lastMana, FloatingHitNumber.ManaUnit);
                _lastMana = mana;
            }

            if (hp != _lastHitPoints || hpMax != _lastHitPointsMax)
            {
                _lastHitPoints = hp;
                _lastHitPointsMax = hpMax;
                SetFillAmount(hp, hpMax);
                UpdateHealthNumbers();
            }

            // The text is a child of the bar - hide it whenever the bar itself is hidden (fade, HideHealthBar).
            if (_healthNumbers != null)
                _healthNumbers.enabled = _isHealthNumbersShown && _background.enabled;
        }

        /// <summary>
        /// "MANA/MAX" on the hero's mana bar while it is shown (rune/scroll in the hand) - same option as HP/MAX. The
        /// fill is set by whoever shows the bar, only the numbers are handled here.
        /// </summary>
        private void UpdateHeroManaNumbers()
        {
            _owner = _npcService.GetHeroContainer();
            if (_owner == null)
                return;

            var mana = _owner.Vob.GetAttribute((int)NpcAttribute.Mana);
            var manaMax = _owner.Vob.GetAttribute((int)NpcAttribute.ManaMax);
            if (mana != _lastHitPoints || manaMax != _lastHitPointsMax)
            {
                // The HP fields hold this bar's value - one bar shows one attribute.
                _lastHitPoints = mana;
                _lastHitPointsMax = manaMax;
                UpdateHealthNumbers();
            }

            if (_healthNumbers != null)
                _healthNumbers.enabled = _isHealthNumbersShown && _background.enabled;
        }

        /// <summary>
        /// "HP/MAX" on the bar - option "healthNumbers" in our settings menu (Gothic.ini, off by default).
        /// The text is created on first use: Gothic's font only exists after the game booted.
        /// </summary>
        private void UpdateHealthNumbers()
        {
            _isHealthNumbersShown = _configService.Gothic.GetInt(Constants.IniNames.HealthNumbers, 0) == 1;
            if (!_isHealthNumbersShown)
                return;

            if (_healthNumbers == null)
                _healthNumbers = CreateHealthNumbersText();

            _healthNumbers.text = $"{_lastHitPoints}/{_lastHitPointsMax}";
        }

        /// <summary>
        /// NPC bars sit in the focus NameCanvas next to the NPC's >NameText<: the numbers copy its layout and go right
        /// above the name. (Not under the bar itself - its background is rotated 180°, the text came out mirrored.)
        /// The player's bar has no name - there the numbers fill the bar.
        /// </summary>
        private TMP_Text CreateHealthNumbersText()
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
                _healthNumbersNameRect = nameRect;
                _healthNumbersBaseFontSize = nameText.fontSize;
                PlaceHealthNumbers(text);
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

        /// <summary>
        /// Same font size as the name, right above it.
        /// </summary>
        private void PlaceHealthNumbers(TMP_Text text)
        {
            if (text == null || _healthNumbersNameRect == null)
                return;

            text.fontSize = _healthNumbersBaseFontSize;
            ((RectTransform)text.transform).anchoredPosition = _healthNumbersNameRect.anchoredPosition +
                Vector2.up * _healthNumbersNameRect.rect.height;
        }

        /// <summary>
        /// "-12 HP" next to the hero's bar (option "hitNumbers", like the numbers over NPCs). Starts at the bar's right
        /// end - found via its corners in canvas space, the bar's background can be rotated.
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

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == Constants.IniNames.HealthNumbers && _statusType is StatusType.Health or StatusType.Mana &&
                _owner != null)
                UpdateHealthNumbers();
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
        }

        public void EnableBar()
        {
            _background.enabled = true;
            _statusValue.enabled = true;
        }

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
            {
                _background.enabled = true;
                _statusValue.enabled = true;
            }

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
            {
                _background.enabled = false;
                _statusValue.enabled = false;
            }
        }

        private void SetAlpha(float a)
        {
            var bg = _background.color;
            bg.a = a;
            _background.color = bg;

            var val = _statusValue.color;
            val.a = a;
            _statusValue.color = val;
        }
    }
}
