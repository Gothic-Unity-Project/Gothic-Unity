using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.Vob.VobItem;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrSpellRing: the spell taken out of the favorites ring - an invisible rune riding on one hand (not
    /// grabbed, the grip does nothing), cast by that hand (VRRuneCaster.RingHand). The spell stays in the inventory.
    /// - The hand shows the spell's effect while it isn't aiming/charging (then VRRuneCaster shows its own).
    /// - A trigger click of the other, empty hand puts it away. So does grabbing something with the spell hand, being
    ///   knocked out or dying.
    /// </summary>
    public class VRFavoritesRingSpell : MonoBehaviour
    {
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _configService;

        // Seconds a trigger press of the other hand may last to count as a click.
        private const float _clickSeconds = 0.6f;

        private ItemInstance _item;
        private VrHandSide _hand;
        private VobContainer _container;
        private VRRuneCaster _caster;
        private GameObject _effect;
        private bool _hasNoEffect;
        private float _otherTriggerDownTime = -1f;
        private bool _isPutAway;

        private VrHandSide OtherHand => _hand == VrHandSide.Left ? VrHandSide.Right : VrHandSide.Left;

        private void Awake()
        {
            this.Inject();
        }

        public void Init(ItemInstance item, VrHandSide hand, VobContainer container, VRRuneCaster caster)
        {
            _item = item;
            _hand = hand;
            _container = container;
            _caster = caster;
        }

        private void Update()
        {
            if (_isPutAway)
                return;

            var bodyState = _playerService.HeroContainer?.Props.BodyState;
            var spellHandItem = _hand == VrHandSide.Left
                ? _vrPlayerService.GrabbedItemLeft
                : _vrPlayerService.GrabbedItemRight;
            if (spellHandItem != null ||
                bodyState is VmGothicEnums.BodyState.BsUnconscious or VmGothicEnums.BodyState.BsDead)
            {
                PutAway("the spell hand grabbed something / hero down");
                return;
            }

            // The last scroll was used up (VRRuneCaster, single-use scrolls) or the spell taken off in the backpack.
            var hero = _playerService.HeroContainer;
            if (hero != null && _item != null && !hero.Props.EquippedItems.Any(i => i.Index == _item.Index))
            {
                PutAway("no longer equipped (last scroll used up)");
                return;
            }

            UpdateEffect();
            UpdatePutAwayClick();
        }

        private void UpdateEffect()
        {
            var isShown = _caster == null || !_caster.IsBusy;
            if (isShown && _effect == null && !_hasNoEffect)
            {
                var handGo = _vrPlayerService.GetHandModelGo(_hand);
                if (handGo != null && _item != null)
                {
                    _effect = VRSpellEffect.Create(_item, handGo, _gameStateService.GothicVm, _vmCacheService,
                        _meshService, _configService.Dev.EnableSpellEffectGothicMotion);
                    _hasNoEffect = _effect == null;
                }
            }
            if (_effect != null && _effect.activeSelf != isShown)
                _effect.SetActive(isShown);
        }

        /// <summary>
        /// The other hand - empty - clicks its trigger (press and release within _clickSeconds).
        /// </summary>
        private void UpdatePutAwayClick()
        {
            var otherItem = OtherHand == VrHandSide.Left
                ? _vrPlayerService.GrabbedItemLeft
                : _vrPlayerService.GrabbedItemRight;
            if (otherItem != null || (_caster != null && _caster.IsBusy))
            {
                _otherTriggerDownTime = -1f;
                return;
            }

            var trigger = VrControls.GetButton(OtherHand, VrButton.Trigger);
            if (trigger.IsJustActivated)
                _otherTriggerDownTime = Time.time;
            if (!trigger.IsJustDeactivated || _otherTriggerDownTime < 0f)
                return;

            var isClick = Time.time - _otherTriggerDownTime <= _clickSeconds;
            _otherTriggerDownTime = -1f;
            if (isClick)
                PutAway("trigger click of the other hand");
        }

        /// <summary>
        /// DeveloperConfig.EnableRingSpellReplacesHandSpell: another spell taken into this hand replaces this one.
        /// </summary>
        public void Replace()
        {
            if (!_isPutAway)
                PutAway("another spell taken into this hand");
        }

        private void PutAway(string reason)
        {
            _isPutAway = true;
            if (_effect != null)
                Destroy(_effect);
            _effect = null;
            _hapticsService.Vibrate(_hand, VrHapticPreset.Info);
            Logger.Log($"[FavoritesRing] {VRFavoritesRing.GetSpellName(_item)} put away ({reason})", LogCat.VR);

            // Destroys this GameObject with its VRRuneCaster (magic mode off).
            if (_container != null)
                _vobService.RemoveWorldItem(_container);
            else
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_effect != null)
                Destroy(_effect);
        }
    }
}
