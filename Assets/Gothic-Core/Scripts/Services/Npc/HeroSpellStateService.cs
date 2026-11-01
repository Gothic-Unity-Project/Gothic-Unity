using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Sleep and freeze spells on the hero. The hero runs no Daedalus states (its PERC_ASSESSMAGIC -> B_AssessMagic ->
    /// ZS_MagicSleep never happens), so the state is set here: the casters' scripts see it (B_SelectWeapon:
    /// !Npc_IsInState(oth, ZS_MagicSleep), C_NpcIsDown) and stop casting - a mage put the hero to sleep again and again.
    /// The hero can't move until the spell's time (SPL_TIME_SLEEP / SPL_TIME_FREEZE) is over, damage wakes a sleeping
    /// hero (B_StopMagicSleep). Found on BoroBongo's branch.
    /// </summary>
    public class HeroSpellStateService
    {
        public const string SleepState = "ZS_MAGICSLEEP";
        public const string FreezeState = "ZS_MAGICFREEZE";

        private const float _defaultSleepSeconds = 30f;
        private const float _defaultFreezeSeconds = 19f;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        private NpcContainer _hero;
        private Coroutine _running;
        private string _stateName;
        private float _endTime;
        private int _previousStateIndex;
        private string _previousStateName;

        public bool IsActive => _stateName != null;

        public HeroSpellStateService()
        {
            // A new world or a loaded game ends the spell: the hero must not stay locked, nor asleep or frozen for
            // the scripts.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(EndOnLoad);
            GlobalEventDispatcher.LoadGameStart.AddListener(EndOnLoad);
        }

        /// <summary>
        /// The state a spell (its spellFxInstanceNames entry) puts its victim in, like the scripts' B_AssessMagic_*:
        /// null for the others.
        /// </summary>
        public static string GetSpellState(string spellName)
        {
            if (spellName.EqualsIgnoreCase("Sleep"))
                return SleepState;
            // A mod's crawler web freezes like the ice block (B_AssessMagic_CrawlerWeb).
            if (spellName.EqualsIgnoreCase("IceCube") || spellName.EqualsIgnoreCase("IceWave") ||
                spellName.EqualsIgnoreCase("CrawlerWeb"))
                return FreezeState;
            return null;
        }

        /// <summary>
        /// Puts the hero into ZS_MAGICSLEEP or ZS_MAGICFREEZE: a spell hit him, or a script started the state on him
        /// (G2's Spell_Logic_Sleep: AI_StartState(other, ZS_MagicSleep)). Again while active: the time starts anew.
        /// </summary>
        public bool TryApply(NpcContainer hero, string stateName)
        {
            if (hero?.PrefabProps == null || stateName is not (SleepState or FreezeState))
                return false;

            var vm = _gameStateService.GothicVm;
            var stateSymbol = vm.GetSymbolByName(stateName);
            if (stateSymbol == null)
                return false;

            var isSleep = stateName == SleepState;
            var seconds = vm.GetSymbolByName(isSleep ? "SPL_TIME_SLEEP" : "SPL_TIME_FREEZE")?.GetInt(0) ?? 0;
            if (seconds <= 0)
                seconds = (int)(isSleep ? _defaultSleepSeconds : _defaultFreezeSeconds);

            if (!IsActive)
            {
                _previousStateIndex = hero.Vob.CurrentStateIndex;
                _previousStateName = hero.Vob.CurrentStateName;
                _contextInteractionService.LockPlayer(PlayerLocks.MagicState);
            }

            _stateName = stateName;
            _endTime = Time.time + seconds;
            hero.Vob.CurrentStateIndex = stateSymbol.Index;
            hero.Vob.CurrentStateName = stateName;
            _hero = hero;
            if (_running == null)
                _running = hero.PrefabProps.StartCoroutine(RunState(hero));
            Logger.Log($"[HeroSpellState] Hero in {stateName} for {seconds} s.", LogCat.Fight);
            return true;
        }

        private IEnumerator RunState(NpcContainer hero)
        {
            var hitPoints = hero.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            while (true)
            {
                yield return null;

                var currentHitPoints = hero.Vob.GetAttribute((int)NpcAttribute.HitPoints);
                var isWokenUp = _stateName == SleepState && currentHitPoints < hitPoints;
                hitPoints = currentHitPoints;
                if (Time.time >= _endTime || isWokenUp || currentHitPoints <= 0)
                {
                    End(hero, isWokenUp ? "woken up by damage" : "time over");
                    yield break;
                }
            }
        }

        private void EndOnLoad()
        {
            if (!IsActive)
                return;

            // The world change may have dropped the hero's GameObject (and the coroutine with it) already.
            if (_running != null && _hero.PrefabProps != null)
                _hero.PrefabProps.StopCoroutine(_running);
            End(_hero, "world changed or game loaded");
        }

        private void End(NpcContainer hero, string reason)
        {
            // Only if nothing else (a dialog, a cutscene) set another state meanwhile.
            if (hero.Vob.CurrentStateName == _stateName)
            {
                hero.Vob.CurrentStateIndex = _previousStateIndex;
                hero.Vob.CurrentStateName = _previousStateName;
            }

            _contextInteractionService.UnlockPlayer(PlayerLocks.MagicState);
            Logger.Log($"[HeroSpellState] Hero out of {_stateName} ({reason}).", LogCat.Fight);
            _stateName = null;
            _running = null;
            _hero = null;
        }
    }
}
