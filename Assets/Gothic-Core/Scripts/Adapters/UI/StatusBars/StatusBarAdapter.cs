using System;
using System.Collections;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Reflex.Attributes;
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
        private int _healthOwnerWaitFrames;

        public enum StatusType
        {
            Health,
            Mana,
            Misc
        }

        public StatusType Type => _statusType;

        private void Awake()
        {
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
            if (hp != _lastHitPoints || hpMax != _lastHitPointsMax)
            {
                _lastHitPoints = hp;
                _lastHitPointsMax = hpMax;
                SetFillAmount(hp, hpMax);
            }
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
