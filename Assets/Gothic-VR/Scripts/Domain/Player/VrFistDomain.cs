using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Audio;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Player;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Melee = Gothic.VR.VRConstants.Melee;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// The hero's fists: a fist is an empty hand with grip + trigger held; it punches like a weapon swing.
    /// Owned by VRWeaponService, which folds IsReadied into the hero's weapon state.
    /// </summary>
    public class VrFistDomain
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly IVrSfxPlayer _vrSfxPlayer;
        [Inject] private readonly IVrInput _vrInput;

        private const string _fistAttackAnimName = "S_FISTATTACK";

        private readonly List<(IVrGrabber Hand, VRFistHitbox Hitbox)> _fists = new();
        private readonly Dictionary<VrHandSide, float> _lastPunchTime = new();
        private readonly Dictionary<VrHandSide, bool> _isFistSwinging = new();
        private SfxModel _fistSwingSound;
        private bool _isFistSwingSoundResolved;
        private float _nextHitboxRetryTime;

        /// <summary>
        /// At least one hand is a fist - the hero is in fist mode (NPCs react like to drawn fists).
        /// </summary>
        public bool IsReadied { get; private set; }

        /// <summary>
        /// An empty hand with grip + trigger held is a fist - the hero is in fist mode
        /// (NPCs react like to drawn fists) and its knuckle hitbox punches.
        /// Returns true when IsReadied changed, so the owner updates the hero's weapon state.
        /// </summary>
        public bool Tick()
        {
            if (NeedsHitboxes() && Time.time >= _nextHitboxRetryTime)
                CreateFistHitboxes();

            var isAnyFist = false;
            for (var i = 0; i < _fists.Count; i++)
            {
                var (hand, hitbox) = _fists[i];
                var side = hand.HandSide;
                var isFist = hand.Held == null &&
                             _vrInput.GetButton(side, VrButton.Grip).IsActive &&
                             _vrInput.GetButton(side, VrButton.Trigger).IsActive;
                if (hitbox.gameObject.activeSelf != isFist)
                    hitbox.gameObject.SetActive(isFist);
                isAnyFist |= isFist;

                if (isFist)
                {
                    TickFistSwing(hand, hitbox);
                    hitbox.SetDebugVisible(_configService.Dev.ShowNpcColliders);
                }
                else
                {
                    _isFistSwinging[side] = false;
                }
            }

            if (isAnyFist == IsReadied)
                return false;
            IsReadied = isAnyFist;
            Logger.Log($"[VRFist] Fists {(isAnyFist ? "up" : "down")}", LogCat.Fight);
            return true;
        }

        private bool NeedsHitboxes()
        {
            if (_fists.Count == 0)
                return true;
            for (var i = 0; i < _fists.Count; i++)
            {
                if (_fists[i].Hand == null || _fists[i].Hitbox == null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A swing (hand faster than a punch needs) is heard and felt like a weapon swing: the fist attack's sound from
        /// Humans.mds (S_FISTATTACK) and a short vibration - once per swing.
        /// </summary>
        private void TickFistSwing(IVrGrabber hand, VRFistHitbox hitbox)
        {
            var side = hand.HandSide;
            var speed = _vrInput.GetControllerVelocity(side).magnitude;
            var isSwinging = speed >= Melee.FistPunchVelocity;
            _isFistSwinging.TryGetValue(side, out var wasSwinging);
            _isFistSwinging[side] = isSwinging;
            if (!isSwinging || wasSwinging)
                return;

            _hapticsService.Vibrate(side, VrHapticPreset.FistSwing);
            var sound = GetFistSwingSound();
            if (sound != null)
                _vrSfxPlayer.Play(_audioService.CreateAudioClip(sound.GetRandomSound()), hitbox.transform.position);
        }

        private SfxModel GetFistSwingSound()
        {
            if (_isFistSwingSoundResolved)
                return _fistSwingSound;
            _isFistSwingSoundResolved = true;

            var mds = _resourceCacheService.TryGetModelScript(Constants.Animations.HumanModelScript, false);
            var anim = mds?.Animations.FirstOrDefault(a => a.Name.EqualsIgnoreCase(_fistAttackAnimName));
            var soundName = anim?.SoundEffects.FirstOrDefault()?.Name;
            _fistSwingSound = soundName != null ? _vmCacheService.TryGetSfxData(soundName) : null;
            if (_fistSwingSound == null)
                Logger.LogWarning($"[VRFist] Humans.mds: {_fistAttackAnimName} has no sound - fist swings are silent",
                    LogCat.Fight);
            return _fistSwingSound;
        }

        private void CreateFistHitboxes()
        {
            foreach (var (_, hitbox) in _fists)
            {
                if (hitbox != null)
                    Object.Destroy(hitbox.gameObject);
            }
            _fists.Clear();
            _nextHitboxRetryTime = Time.time + Melee.FistHitboxRetrySeconds;

            var rig = _vrPlayerService.Rig;
            foreach (var hand in _vrPlayerService.GetHandGrabbers())
            {
                var handVisual = rig.GetHandVisual(hand.HandSide);
                if (handVisual == null)
                {
                    Logger.LogWarning($"[VRFist] No hand visual for {hand.HandSide} - no fist hitbox, retry in "
                                      + $"{Melee.FistHitboxRetrySeconds:F0}s", LogCat.Fight);
                    continue;
                }
                var hitbox = VRFistHitbox.Create(handVisual, hand.HandSide);
                hitbox.OnNpcEntered = OnFistEnteredNpc;
                _fists.Add((hand, hitbox));
            }
        }

        /// <summary>
        /// A punch needs a swing (hand speed) like a weapon attack, and one punch per hand per moment.
        /// The damage is the engine's fist damage (FightService: strength, blunt).
        /// </summary>
        private void OnFistEnteredNpc(VRFistHitbox hitbox, NpcLoader npcLoader, Vector3 position)
        {
            var hero = _playerService.HeroContainer;
            var target = npcLoader.Container;
            if (hero == null || target == null || target == hero)
                return;
            // An unconscious hero doesn't punch; a knocked out human is finished off by a stab only (a fist finished
            // him - Bullit and his quest were gone after one punch).
            // A dead NPC takes no damage (FightService.OnHit returns on IsDead) - no punch haptics or log for him.
            if (hero.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious || target.IsKnockedOut() ||
                target.Props.IsDead)
                return;

            var side = hitbox.HandSide;
            var targetName = target.Instance.GetName(NpcNameSlot.Slot0);
            var speed = _vrInput.GetControllerVelocity(side).magnitude;
            if (speed < Melee.FistPunchVelocity)
            {
                Logger.LogEditor($"[VRFist] {side} fist touched {targetName} - too slow ({speed:F1} m/s)",
                    LogCat.Fight);
                return;
            }
            if (_lastPunchTime.TryGetValue(side, out var last) && Time.time - last < Melee.FistPunchCooldownSeconds)
                return;

            _lastPunchTime[side] = Time.time;
            _hapticsService.Vibrate(side, VrHapticPreset.FistHit);
            Logger.Log($"[VRFist] {side} punch at {speed:F1} m/s → {targetName}", LogCat.Fight);
            GlobalEventDispatcher.FightHit.Invoke(hero, target, position, null);
        }
    }
}
