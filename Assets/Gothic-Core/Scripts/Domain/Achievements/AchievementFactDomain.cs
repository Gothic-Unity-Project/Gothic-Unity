using System;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Achievements;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Doc;
using Gothic.Core.Services;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// Translates the game's events into achievement facts (no ZenKit types beyond this class) and reports the script
    /// moments that may have changed story variables. Owned by AchievementService, which gets everything through
    /// the callbacks of AddListeners() and decides whether it's recorded (gate).
    /// </summary>
    public class AchievementFactDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;

        private Action<AchievementFact> _onFact;
        private Action<string> _onCounter;
        private Action<string, string> _onDistinct;
        private Action _onScriptMoment;


        /// <param name="onFact">A fact to check against the definitions indexed for it.</param>
        /// <param name="onCounter">A counter rose (AchievementCounterIds).</param>
        /// <param name="onDistinct">A distinct counter got a key (counter id, key).</param>
        /// <param name="onScriptMoment">A script finished that may have changed story variables.</param>
        public void AddListeners(Action<AchievementFact> onFact, Action<string> onCounter,
            Action<string, string> onDistinct, Action onScriptMoment)
        {
            _onFact = onFact;
            _onCounter = onCounter;
            _onDistinct = onDistinct;
            _onScriptMoment = onScriptMoment;

            GlobalEventDispatcher.LogTopicStatusChanged.AddListener(OnLogTopicStatusChanged);
            GlobalEventDispatcher.HeroTrueGuildChanged.AddListener(OnHeroTrueGuildChanged);
            GlobalEventDispatcher.DialogInfoTold.AddListener(OnDialogInfoTold);
            GlobalEventDispatcher.HeroItemsChanged.AddListener(OnHeroItemsChanged);
            GlobalEventDispatcher.NpcKilled.AddListener(OnNpcKilled);
            GlobalEventDispatcher.LockPickComboFinished.AddListener(OnLockOpened);
            GlobalEventDispatcher.DocShow.AddListener(OnDocShown);
            GlobalEventDispatcher.DialogInfoFinished.AddListener(OnDialogInfoFinished);
            GlobalEventDispatcher.NpcDeathScriptsFinished.AddListener(OnNpcDeathScriptsFinished);
            GlobalEventDispatcher.ItemFunctionFinished.AddListener(OnItemFunctionFinished);
            GlobalEventDispatcher.MobStateFunctionFinished.AddListener(OnScriptFunctionFinished);
            GlobalEventDispatcher.TriggerScriptFinished.AddListener(OnScriptFunctionFinished);
            GlobalEventDispatcher.ChapterIntroduced.AddListener(OnScriptFunctionFinished);
        }

        public void RemoveListeners()
        {
            GlobalEventDispatcher.LogTopicStatusChanged.RemoveListener(OnLogTopicStatusChanged);
            GlobalEventDispatcher.HeroTrueGuildChanged.RemoveListener(OnHeroTrueGuildChanged);
            GlobalEventDispatcher.DialogInfoTold.RemoveListener(OnDialogInfoTold);
            GlobalEventDispatcher.HeroItemsChanged.RemoveListener(OnHeroItemsChanged);
            GlobalEventDispatcher.NpcKilled.RemoveListener(OnNpcKilled);
            GlobalEventDispatcher.LockPickComboFinished.RemoveListener(OnLockOpened);
            GlobalEventDispatcher.DocShow.RemoveListener(OnDocShown);
            GlobalEventDispatcher.DialogInfoFinished.RemoveListener(OnDialogInfoFinished);
            GlobalEventDispatcher.NpcDeathScriptsFinished.RemoveListener(OnNpcDeathScriptsFinished);
            GlobalEventDispatcher.ItemFunctionFinished.RemoveListener(OnItemFunctionFinished);
            GlobalEventDispatcher.MobStateFunctionFinished.RemoveListener(OnScriptFunctionFinished);
            GlobalEventDispatcher.TriggerScriptFinished.RemoveListener(OnScriptFunctionFinished);
            GlobalEventDispatcher.ChapterIntroduced.RemoveListener(OnScriptFunctionFinished);
        }

        private void OnLogTopicStatusChanged(string topic, SaveTopicStatus status)
        {
            _onFact(new AchievementFact(AchievementFactKind.LogTopic, stringKey: topic, value: (int)status));
        }

        private void OnHeroTrueGuildChanged(int guild)
        {
            _onFact(new AchievementFact(AchievementFactKind.TrueGuild, value: guild));
        }

        private void OnDialogInfoTold(int infoIndex)
        {
            _onFact(new AchievementFact(AchievementFactKind.InfoTold, infoIndex));
        }

        private void OnHeroItemsChanged(int itemIndex)
        {
            _onFact(new AchievementFact(AchievementFactKind.HeroItem, itemIndex));
        }

        /// <summary>
        /// A kill by the hero's summon is the hero's (GetCreditedAttacker), falls and drowning are the victim's own.
        /// </summary>
        private void OnNpcKilled(NpcContainer victim, NpcContainer killer)
        {
            if (victim?.Instance == null)
                return;

            var creditedKiller = killer.GetCreditedAttacker(_npcService.GetHeroContainer());
            var isByHero = creditedKiller != victim && creditedKiller?.Instance != null &&
                           creditedKiller.Instance.IsHero(_gameStateService.GothicVm);
            _onFact(new AchievementFact(AchievementFactKind.Kill, victim.Instance.Index, isByHero: isByHero));
        }

        /// <summary>
        /// LockPickComboFinished is any opened lock (also by key) - only a lock pick counts.
        /// </summary>
        private void OnLockOpened(VobContainer tool, VobContainer lockable, int hand)
        {
            if (tool?.Vob is IItem item && Constants.Items.IsLockPick(item.GetSymbolName()))
                _onCounter(AchievementCounterIds.LockpickSuccess);
        }

        /// <summary>
        /// Only documents from an item count (a mob's page, e.g. a lectern, has no stable name).
        /// </summary>
        private void OnDocShown(DocModel doc, GameObject target)
        {
            if (!string.IsNullOrEmpty(doc?.SourceName))
                _onDistinct(AchievementCounterIds.DocumentShown, doc.SourceName);
        }

        private void OnDialogInfoFinished(int function) => _onScriptMoment();

        private void OnNpcDeathScriptsFinished(NpcInstance dead) => _onScriptMoment();

        private void OnItemFunctionFinished(ItemInstance item) => _onScriptMoment();

        /// <summary>
        /// A mob's _S1, a trigger script or IntroduceChapter ran.
        /// </summary>
        private void OnScriptFunctionFinished(string name) => _onScriptMoment();
    }
}
