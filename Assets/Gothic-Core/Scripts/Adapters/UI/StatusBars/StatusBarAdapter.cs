using System;
using System.Collections;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UI;
using ZenKit.Daedalus;

namespace Gothic.Core.Adapters.UI.StatusBars
{
    public class StatusBarAdapter : MonoBehaviour
    {
        [SerializeField] private StatusType _statusType;
        public StatusType Type => _statusType;
        private Coroutine _fadeCoroutine;
        private bool _isFadingIn;
        [SerializeField] private bool _isPlayer;
        [SerializeField] private Image _background;
        [SerializeField] private Image _statusValue;

        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly NpcService _npcService;

        private NpcContainer _owner;
        private bool _isInWorld;
        private bool _isVideoPlaying;

        public enum StatusType
        {
            Health,
            Mana,
            Misc
        }

        private void Awake()
        {
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
                else if (_statusType == StatusType.Mana)
                {
                    GlobalEventDispatcher.HeroManaBarRequested.AddListener(OnHeroManaBarRequested);
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

        private void OnDestroy()
        {
            GlobalEventDispatcher.HeroManaBarRequested.RemoveListener(OnHeroManaBarRequested);
        }

        /// <summary>
        /// Health and mana follow NpcAttributeChanged while enabled. Culled NPCs are disabled - they refresh when
        /// re-enabled.
        /// </summary>
        private void OnEnable()
        {
            if (_statusType == StatusType.Misc)
                return;

            GlobalEventDispatcher.NpcAttributeChanged.AddListener(OnNpcAttributeChanged);
            Refresh();
        }

        private void OnDisable()
        {
            FinishFade();

            if (_statusType == StatusType.Misc)
                return;

            GlobalEventDispatcher.NpcAttributeChanged.RemoveListener(OnNpcAttributeChanged);
        }

        private void OnNpcAttributeChanged(NpcContainer npc, NpcAttribute attribute)
        {
            var isShownAttribute = _statusType == StatusType.Health
                ? attribute is NpcAttribute.HitPoints or NpcAttribute.HitPointsMax
                : attribute is NpcAttribute.Mana or NpcAttribute.ManaMax;
            if (!isShownAttribute || npc != GetOwner())
                return;

            Refresh();
        }

        private void OnHeroManaBarRequested(bool isShown)
        {
            if (isShown)
            {
                RefreshMana();
                FadeIn();
            }
            else
            {
                FadeOut();
            }
        }

        private void OnWorldSceneLoaded()
        {
            _isInWorld = true;
            if (!_isVideoPlaying)
                EnableBar();
            RefreshHealth();
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

        private void Refresh()
        {
            if (_statusType == StatusType.Health)
                RefreshHealth();
            else if (_statusType == StatusType.Mana)
                RefreshMana();
        }

        private void RefreshMana()
        {
            var owner = GetOwner();
            var manaMax = owner?.Vob.GetAttribute((int)NpcAttribute.ManaMax) ?? 0;
            if (manaMax <= 0)
                return;

            SetFillAmount(owner.Vob.GetAttribute((int)NpcAttribute.Mana), manaMax);
        }

        private void RefreshHealth()
        {
            var owner = GetOwner();
            if (owner == null)
                return;

            SetFillAmount(owner.Vob.GetAttribute((int)NpcAttribute.HitPoints),
                owner.Vob.GetAttribute((int)NpcAttribute.HitPointsMax));
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
        }

        public void EnableBar()
        {
            _background.enabled = true;
            _statusValue.enabled = true;
        }

        private void FadeIn(float duration = 0.25f)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine(0f, 1f, duration, enable: true));
        }

        private void FadeOut(float duration = 0.25f)
        {
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine(1f, 0f, duration, enable: false));
        }

        /// <summary>
        /// Disabled mid-fade, Unity stops the routine - the bar jumps to the fade's end instead of staying half visible.
        /// </summary>
        private void FinishFade()
        {
            if (_fadeCoroutine == null)
                return;

            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
            SetAlpha(_isFadingIn ? 1f : 0f);
            if (!_isFadingIn)
                DisableBar();
        }

        private IEnumerator FadeRoutine(float from, float to, float duration, bool enable)
        {
            _isFadingIn = enable;
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
            _fadeCoroutine = null;
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
