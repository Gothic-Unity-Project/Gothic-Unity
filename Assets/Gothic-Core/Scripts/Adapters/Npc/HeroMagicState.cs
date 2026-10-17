using System;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// DeveloperConfig.EnableHeroSpellStates: sleep and freeze spells work on the hero. The hero runs no Daedalus states
    /// (its PERC_ASSESSMAGIC -> B_AssessMagic -> ZS_MagicSleep never happens), so the state is set here: the casters'
    /// scripts see it (B_SelectWeapon: !Npc_IsInState(oth, ZS_MagicSleep), C_NpcIsDown) and stop casting - Xardas put
    /// the hero to sleep again and again. The hero can't move until the spell's time (SPL_TIME_SLEEP/FREEZE) is over;
    /// damage wakes a sleeping hero (B_StopMagicSleep).
    /// </summary>
    public class HeroMagicState : MonoBehaviour
    {
        private const float _defaultSleepSeconds = 30f;
        private const float _defaultFreezeSeconds = 19f;

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
                return "ZS_MAGICSLEEP";
            // Mroczne Tajemnice's crawler web freezes like the ice block (B_AssessMagic_CrawlerWeb).
            if (mfxName.Equals("IceCube", StringComparison.OrdinalIgnoreCase) ||
                mfxName.Equals("IceWave", StringComparison.OrdinalIgnoreCase) ||
                mfxName.Equals("CrawlerWeb", StringComparison.OrdinalIgnoreCase))
                return "ZS_MAGICFREEZE";
            return null;
        }

        public static void Apply(NpcContainer hero, string stateName)
        {
            if (hero?.Go == null || string.IsNullOrEmpty(stateName))
                return;
            var vm = ReflexProjectInstaller.DIContainer.Resolve<GameStateService>().GothicVm;
            var stateSymbol = vm.GetSymbolByName(stateName);
            if (stateSymbol == null)
                return;

            var isSleep = stateName == "ZS_MAGICSLEEP";
            var timeSymbol = vm.GetSymbolByName(isSleep ? "SPL_TIME_SLEEP" : "SPL_TIME_FREEZE");
            var seconds = timeSymbol?.GetInt(0) ?? 0;
            if (seconds <= 0)
                seconds = (int)(isSleep ? _defaultSleepSeconds : _defaultFreezeSeconds);

            var state = hero.Go.GetComponent<HeroMagicState>() ?? hero.Go.AddComponent<HeroMagicState>();
            state.Begin(hero, stateName, stateSymbol.Index, seconds);
        }

        private void Begin(NpcContainer hero, string stateName, int stateIndex, float seconds)
        {
            if (!_isActive)
            {
                _previousStateIndex = hero.Vob.CurrentStateIndex;
                _previousStateName = hero.Vob.CurrentStateName;
                ReflexProjectInstaller.DIContainer.Resolve<ContextInteractionService>().LockPlayerInPlace();
            }

            _hero = hero;
            _stateName = stateName;
            _endTime = Time.time + seconds;
            _hitPoints = hero.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            _isActive = true;
            hero.Vob.CurrentStateIndex = stateIndex;
            hero.Vob.CurrentStateName = stateName;
            Logger.Log($"[HeroMagicState] Hero in {stateName} for {seconds:F0} s", LogCat.Fight);
        }

        private void Update()
        {
            if (!_isActive)
                return;

            var hitPoints = _hero.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            var isWokenUp = _stateName == "ZS_MAGICSLEEP" && hitPoints < _hitPoints;
            _hitPoints = hitPoints;
            if (Time.time >= _endTime || isWokenUp || hitPoints <= 0)
                End(isWokenUp ? "woken up by damage" : "time over");
        }

        private void OnDestroy()
        {
            if (_isActive)
                End("destroyed");
        }

        private void End(string reason)
        {
            _isActive = false;
            // Only if nothing else (a cutscene, a dialog) set another state meanwhile.
            if (_hero.Vob.CurrentStateName == _stateName)
            {
                _hero.Vob.CurrentStateIndex = _previousStateIndex;
                _hero.Vob.CurrentStateName = _previousStateName;
            }
            ReflexProjectInstaller.DIContainer.Resolve<ContextInteractionService>().UnlockPlayer();
            Logger.Log($"[HeroMagicState] Hero out of {_stateName} ({reason})", LogCat.Fight);
        }
    }
}
