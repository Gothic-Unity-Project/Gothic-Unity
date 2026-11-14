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

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// Lock-on for thrown spells: the rune hand points at an NPC and holds its trigger - the NPC is locked and a spell
    /// released by the other hand flies to it (VRSpellThrow). Sticky while the trigger is held; breaks when the target
    /// is down, out of reach (the spell's Range) or out of sight (Gothic's sight rule) for LockLostSightSeconds.
    /// Worker of VRPlayerService (the lock's owner, read via VRPlayerService.SpellLockTarget), ticked by VRRuneCaster
    /// only while a throwable rune is held in a hand. Not for ring spells and the simulator. Map: Docs/vr-magic.md.
    /// </summary>
    public class VrSpellLockDomain
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly PlayerService _playerService;

        private float _range;
        private VrHandSide _runeHandSide;
        // The rune hand aims up to the spell's range (VRPlayerService.GetSpellTarget) while the trigger searches.
        private bool _isSearching;
        // The lock broke by itself: the trigger has to be let go before it locks again.
        private bool _isWaitingForRelease;
        private Transform _heroRoot;
        private float _nextSightCheckTime;
        // Time the target went out of sight, -1 = in sight.
        private float _sightLostTime = -1f;

        /// <summary>
        /// The locked NPC, null if none.
        /// </summary>
        public NpcContainer Target { get; private set; }

        /// <summary>
        /// Every frame while the rune is held and the other hand is free to throw. range: how far the spell flies
        /// (VRSpellThrow.Range). true the frame the target got locked.
        /// </summary>
        public bool Tick(VrHandSide runeHandSide, float range)
        {
            if ((Target != null || _isSearching) && runeHandSide != _runeHandSide)
                Clear();

            if (!_vrInput.GetButton(runeHandSide, VrButton.Trigger).IsActive)
            {
                Clear();
                _isWaitingForRelease = false;
                return false;
            }

            if (_isWaitingForRelease)
                return false;

            _runeHandSide = runeHandSide;
            _range = range;
            if (Target == null)
                return Search();

            CheckLock();
            return false;
        }

        /// <summary>
        /// Unlocks quietly (trigger let go or no free throwing hand): no vibration. A lock lost by itself still waits
        /// for the trigger to be let go.
        /// </summary>
        public void Clear()
        {
            StopSearching();
            if (Target == null)
                return;

            Logger.Log("[VrSpellLockDomain] Unlocked", LogCat.VR);
            Unlock();
        }

        /// <summary>
        /// The rune is gone: unlocks quietly and forgets the wait for the trigger's release - the next rune starts
        /// fresh.
        /// </summary>
        public void End()
        {
            Clear();
            _isWaitingForRelease = false;
        }

        /// <summary>
        /// Pressed, nothing locked yet: the first standing NPC the rune hand points at is locked - pressing a moment
        /// before pointing works too.
        /// </summary>
        private bool Search()
        {
            if (!_isSearching)
            {
                _vrPlayerService.ActivateSpellTargeting(_runeHandSide, _range);
                _isSearching = true;
            }

            var candidate = _vrPlayerService.GetSpellTarget(_runeHandSide);
            if (candidate == null || candidate.Go == null || candidate.IsDown())
                return false;

            var hero = _npcService.GetHeroContainer();
            if (candidate == hero || hero?.Go == null)
                return false;

            Lock(candidate, hero.Go.transform);
            return true;
        }

        private void Lock(NpcContainer target, Transform heroRoot)
        {
            Target = target;
            _heroRoot = heroRoot;
            _sightLostTime = -1f;
            _nextSightCheckTime = Time.time + VRConstants.Magic.LockSightCheckSeconds;

            // Gothic's focus is the spell's target.
            _playerService.FocusNpc = target;

            // Highlight (VRFocus) first: dropping the far reach ends the hand's hover on the target right after.
            GlobalEventDispatcher.HeroSpellTargetLocked.Invoke((int)_runeHandSide);
            StopSearching();

            Logger.Log($"[VrSpellLockDomain] Locked {target.Instance.GetName(NpcNameSlot.Slot0)} with the " +
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
            Logger.Log($"[VrSpellLockDomain] Lock on {Target.Instance.GetName(NpcNameSlot.Slot0)} lost - {reason}",
                LogCat.VR);
            Unlock();
            _isWaitingForRelease = true;
            GlobalEventDispatcher.HeroSpellTargetLockLost.Invoke((int)_runeHandSide);
        }

        // VRFocus sees SpellLockTarget gone and drops the highlight.
        private void Unlock()
        {
            _heroRoot = null;
            Target = null;
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
