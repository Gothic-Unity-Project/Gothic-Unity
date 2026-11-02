using System;
using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// DeveloperConfig.EnableHeroSpellStates: sleep and freeze spells work on the hero. The hero runs no Daedalus states
    /// (its PERC_ASSESSMAGIC -> B_AssessMagic -> ZS_MagicSleep never happens), so the state is set here: the casters'
    /// scripts see it (B_SelectWeapon: !Npc_IsInState(oth, ZS_MagicSleep), C_NpcIsDown) and stop casting - Xardas put
    /// the hero to sleep again and again. The hero can't move until the spell's time (SPL_TIME_SLEEP/FREEZE) is over;
    /// damage wakes a sleeping hero (B_StopMagicSleep).
    /// DeveloperConfig.EnableHeroSleepBody: asleep, the spell's effect on its target (emFXCollDyn of spellFX_Sleep, the
    /// "z"s) floats over the hero's head; VRHeroBody lets the body fall asleep.
    /// Owned by NpcAiService.
    /// </summary>
    public class HeroSpellStateDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ParticleService _particleService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        private const float _defaultSleepSeconds = 30f;
        private const float _defaultFreezeSeconds = 19f;
        // The sleep effect above the eyes (m).
        private const float _sleepFxHeight = 0.3f;

        public const string SleepStateName = "ZS_MAGICSLEEP";

        private List<GameObject> _sleepFx;

        private NpcContainer _hero;
        private string _stateName;
        private int _previousStateIndex;
        private string _previousStateName;
        private float _endTime;
        private int _hitPoints;
        private bool _isActive;

        /// <summary>
        /// The state a spell puts its victim in (the scripts' B_AssessMagic_*): null for the others.
        /// </summary>
        public static string GetSpellState(string mfxName)
        {
            if (string.IsNullOrEmpty(mfxName))
                return null;
            if (mfxName.Equals("Sleep", StringComparison.OrdinalIgnoreCase))
                return SleepStateName;
            // Mroczne Tajemnice's crawler web freezes like the ice block (B_AssessMagic_CrawlerWeb).
            if (mfxName.Equals("IceCube", StringComparison.OrdinalIgnoreCase) ||
                mfxName.Equals("IceWave", StringComparison.OrdinalIgnoreCase) ||
                mfxName.Equals("CrawlerWeb", StringComparison.OrdinalIgnoreCase))
                return "ZS_MAGICFREEZE";
            return null;
        }

        public void Apply(NpcContainer hero, string stateName)
        {
            if (hero?.Go == null || string.IsNullOrEmpty(stateName))
                return;
            var vm = _gameStateService.GothicVm;
            var stateSymbol = vm.GetSymbolByName(stateName);
            if (stateSymbol == null)
                return;

            var isSleep = stateName == SleepStateName;
            var timeSymbol = vm.GetSymbolByName(isSleep ? "SPL_TIME_SLEEP" : "SPL_TIME_FREEZE");
            var seconds = timeSymbol?.GetInt(0) ?? 0;
            if (seconds <= 0)
                seconds = (int)(isSleep ? _defaultSleepSeconds : _defaultFreezeSeconds);

            var wasActive = _isActive;
            if (!_isActive)
            {
                _previousStateIndex = hero.Vob.CurrentStateIndex;
                _previousStateName = hero.Vob.CurrentStateName;
                _contextInteractionService.LockPlayerInPlace();
            }

            _hero = hero;
            _stateName = stateName;
            _endTime = Time.time + seconds;
            _hitPoints = hero.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            _isActive = true;
            hero.Vob.CurrentStateIndex = stateSymbol.Index;
            hero.Vob.CurrentStateName = stateName;
            if (isSleep)
                PlaySleepFx();
            if (!wasActive)
                _unityMonoService.StartCoroutine(WatchState());
            Logger.Log($"[HeroSpellState] Hero in {stateName} for {seconds} s", LogCat.Fight);
        }

        private IEnumerator WatchState()
        {
            while (_isActive)
            {
                yield return null;

                // The hero's body is gone (world change).
                if (_hero?.Go == null)
                {
                    End("hero removed");
                    yield break;
                }

                var hitPoints = _hero.Vob.GetAttribute((int)NpcAttribute.HitPoints);
                var isWokenUp = _stateName == SleepStateName && hitPoints < _hitPoints;
                _hitPoints = hitPoints;
                if (Time.time >= _endTime || isWokenUp || hitPoints <= 0)
                    End(isWokenUp ? "woken up by damage" : "time over");
            }
        }

        private void PlaySleepFx()
        {
            if (_sleepFx != null || !_configService.Dev.EnableHeroSleepBody || Camera.main == null)
                return;

            var fxName = _vmCacheService.TryGetVfxData("spellFX_Sleep")?.EmFxCollDynS;
            if (string.IsNullOrEmpty(fxName))
            {
                Logger.LogWarning("[HeroSpellState] spellFX_Sleep has no target effect - no sleep effect", LogCat.Fight);
                return;
            }

            var head = Camera.main.transform;
            _sleepFx = _particleService.PlayVisualFx(fxName, head.position + Vector3.up * _sleepFxHeight, head, true);
        }

        private void StopSleepFx()
        {
            if (_sleepFx == null)
                return;
            foreach (var fx in _sleepFx)
            {
                if (fx != null)
                    Object.Destroy(fx);
            }
            _sleepFx = null;
        }

        private void End(string reason)
        {
            _isActive = false;
            StopSleepFx();
            // Only if nothing else (a cutscene, a dialog) set another state meanwhile.
            if (_hero?.Vob != null && _hero.Vob.CurrentStateName == _stateName)
            {
                _hero.Vob.CurrentStateIndex = _previousStateIndex;
                _hero.Vob.CurrentStateName = _previousStateName;
            }
            _contextInteractionService.UnlockPlayer();
            Logger.Log($"[HeroSpellState] Hero out of {_stateName} ({reason})", LogCat.Fight);
        }
    }
}
