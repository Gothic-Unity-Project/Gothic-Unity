using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Lock-on for thrown spells: the rune hand points at an NPC and holds its trigger - the NPC is locked and a spell
    /// released by the other hand flies to it (VRSpellThrow). Sticky while the trigger is held; breaks when the target
    /// is down, out of reach (the spell's Range) or out of sight (Gothic's sight rule) for LockLostSightSeconds.
    /// Worker of VRRuneCaster (new VRSpellLock(...).Inject()), ticked only while a throwable rune is held in a hand.
    /// Not for ring spells and the simulator. Map: Docs/vr-magic.md.
    /// </summary>
    public class VRSpellLock
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly PlayerService _playerService;

        private readonly float _range;
        private readonly VRSpellFx _fx;

        private VrHandSide _runeHandSide;
        // The rune hand's distance grab reaches the spell's range while the trigger looks for a target.
        private bool _isSearching;
        // The lock broke by itself: the trigger has to be let go before it locks again.
        private bool _isWaitingForRelease;
        private VRFocus _targetFocus;
        private Transform _heroRoot;
        private float _nextSightCheckTime;
        // Time the target went out of sight, -1 = in sight.
        private float _sightLostTime = -1f;

        /// <summary>
        /// The locked NPC, null if none. The caster's only source for the throw's target.
        /// </summary>
        public NpcContainer Target { get; private set; }

        /// <summary>
        /// range: how far the spell flies (VRSpellThrow.Range). fx: the caster's effects (lock sound).
        /// </summary>
        public VRSpellLock(float range, VRSpellFx fx)
        {
            _range = range;
            _fx = fx;
        }

        /// <summary>
        /// Every frame while the rune is held and the other hand is free to throw.
        /// </summary>
        public void Tick(VrHandSide runeHandSide)
        {
            if ((Target != null || _isSearching) && runeHandSide != _runeHandSide)
                Clear();

            if (!_vrInput.GetButton(runeHandSide, VrButton.Trigger).IsActive)
            {
                Clear();
                _isWaitingForRelease = false;
                return;
            }

            if (_isWaitingForRelease)
                return;

            _runeHandSide = runeHandSide;
            if (Target != null)
                CheckLock();
            else
                Search();
        }

        /// <summary>
        /// Unlocks quietly (trigger let go, rune let go or no free throwing hand): no vibration.
        /// </summary>
        public void Clear()
        {
            StopSearching();
            if (Target == null)
                return;

            Logger.Log("[VRSpellLock] Unlocked", LogCat.VR);
            Unlock();
        }

        /// <summary>
        /// Pressed, nothing locked yet: the first standing NPC the rune hand points at is locked - pressing a moment
        /// before pointing works too.
        /// </summary>
        private void Search()
        {
            if (!_isSearching)
            {
                _vrPlayerService.ActivateSpellTargeting(_runeHandSide, _range);
                _isSearching = true;
            }

            var candidate = _vrPlayerService.GetSpellTarget(_runeHandSide);
            if (candidate == null || candidate.Go == null || candidate.IsDown())
                return;

            var hero = _npcService.GetHeroContainer();
            if (candidate == hero || hero?.Go == null)
                return;

            Lock(candidate, hero.Go.transform);
        }

        private void Lock(NpcContainer target, Transform heroRoot)
        {
            Target = target;
            _heroRoot = heroRoot;
            _sightLostTime = -1f;
            _nextSightCheckTime = Time.time + VRConstants.Magic.LockSightCheckSeconds;

            // Highlight first: dropping the far reach ends the hand's hover on the target right after.
            _targetFocus = target.Go.GetComponentInChildren<VRFocus>();
            if (_targetFocus != null)
                _targetFocus.SetLocked(true);

            StopSearching();
            _vrPlayerService.IsSpellLockActive = true;
            // Gothic's focus is the spell's target.
            _playerService.FocusNpc = target;

            _fx.PlayOneShot("TMAG_INIT");
            GlobalEventDispatcher.HeroSpellTargetLocked.Invoke((int)_runeHandSide);
            Logger.Log($"[VRSpellLock] Locked {target.Instance.GetName(NpcNameSlot.Slot0)} with the " +
                       $"{_runeHandSide} hand (range {_range:F0} m)", LogCat.VR);
        }

        /// <summary>
        /// Locked: the target must stay standing, within reach and in sight.
        /// </summary>
        private void CheckLock()
        {
            if (Target.Go == null || !Target.Go.activeInHierarchy || Target.IsDown())
            {
                LoseLock("target down or gone");
                return;
            }

            var head = _vrPlayerService.Rig?.Head;
            if (head == null)
                return;

            var maxDistance = _range * VRConstants.Magic.LockRangeHysteresis;
            if ((Target.Go.transform.position - head.position).sqrMagnitude > maxDistance * maxDistance)
            {
                LoseLock("out of range");
                return;
            }

            if (Time.time < _nextSightCheckTime)
                return;
            _nextSightCheckTime = Time.time + VRConstants.Magic.LockSightCheckSeconds;

            if (_npcHelperService.HasFreeSight(head.position, Target.GetAimPoint(), _heroRoot, Target.Go.transform))
            {
                _sightLostTime = -1f;
                return;
            }

            if (_sightLostTime < 0f)
                _sightLostTime = Time.time;
            else if (Time.time - _sightLostTime >= VRConstants.Magic.LockLostSightSeconds)
                LoseLock("out of sight");
        }

        /// <summary>
        /// The lock broke by itself: the rune hand vibrates, and it locks again only after the trigger was let go.
        /// </summary>
        private void LoseLock(string reason)
        {
            Logger.Log($"[VRSpellLock] Lock on {Target.Instance.GetName(NpcNameSlot.Slot0)} lost - {reason}",
                LogCat.VR);
            Unlock();
            _isWaitingForRelease = true;
            GlobalEventDispatcher.HeroSpellTargetLockLost.Invoke((int)_runeHandSide);
        }

        private void Unlock()
        {
            if (_targetFocus != null)
                _targetFocus.SetLocked(false);
            _targetFocus = null;
            _heroRoot = null;
            Target = null;
            _vrPlayerService.IsSpellLockActive = false;
        }

        private void StopSearching()
        {
            if (!_isSearching)
                return;
            _isSearching = false;
            _vrPlayerService.DeactivateSpellTargeting();
        }
    }
}
