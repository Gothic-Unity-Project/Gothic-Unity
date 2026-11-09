using Gothic.Core.Domain.Npc;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Pickpocketing the VR purse stands for (VRPickpocket).
    /// G1 (always on): the engine's steal window. The hero with NPC_TALENT_PICKPOCKET sneaks up to a human who can't
    /// see him and takes items from his pockets. Every item taken can get him caught: the talent value is the chance
    /// (Npc_Default 90, each level -40 by B_GiveSkill: 50 trained, 10 master - Fingers: "only as a master you take
    /// something without him noticing", "even the best get caught sometimes").
    /// Caught: the victim gets PERC_CATCHTHIEF (ZS_CatchThief: whirls around, ZS_AssessEnemy), witnesses within
    /// HAI_DIST_ASSESSTHEFT the passive PERC_ASSESSTHEFT with the victim set (B_AssessTheft: "$BEHINDYOU").
    ///
    /// G2: only a view of its own pickpocket dialog (PickpocketDialogDomain). A purse shows exactly when the NPC's
    /// DIA_*_PICKPOCKET would be offered; grabbing it is choosing "pickpocket" (B_Beklauen: gold, once per NPC).
    /// </summary>
    public class PickpocketService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcAiService _npcAiService;

        private readonly PickpocketDialogDomain _dialogDomain = new PickpocketDialogDomain().Inject();

        public bool IsGothic1 => _contextGameVersionService.IsGothic1();

        private const string _talentSymbol = "NPC_TALENT_PICKPOCKET";

        // No game picked yet (profile menu): no VM and no game version.
        public bool IsEnabled => _configService.Gothic != null && _gameStateService.GothicVm != null;

        public NpcContainer Hero => (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();

        /// <summary>
        /// The hero sneaks. G1: and knows pickpocketing (Fingers). G2's talent is its dialog's job (C_Beklauen).
        /// </summary>
        public bool CanHeroSteal()
        {
            var hero = Hero;
            if (!IsEnabled || hero?.Props == null || _gameStateService.Dialogs.IsInDialog)
                return false;
            if (hero.Props.BodyState != VmGothicEnums.BodyState.BsSneak)
                return false;
            return !IsGothic1 || _npcHelperService.ExtNpcGetTalentSkill(hero.Instance, GetTalentId()) > 0;
        }

        /// <summary>
        /// A human who is up, not fighting and doesn't see the hero (C_NpcIsHuman, the engine's steal check).
        /// G2: and his PICKPOCKET dialog option would show (checked last, cached per NPC).
        /// </summary>
        public bool CanBeRobbed(NpcContainer npc)
        {
            var hero = Hero;
            if (npc?.Instance == null || npc.Props == null || npc.Go == null || hero?.Instance == null || npc == hero)
                return false;
            if (npc.PrefabProps != null && npc.PrefabProps.IsHero())
                return false;
            if (!npc.IsHuman())
                return false;
            if (!npc.IsAsleep())
            {
                if (npc.IsDown())
                    return false;
                if ((VmGothicEnums.WeaponState)npc.Vob.FightMode != VmGothicEnums.WeaponState.NoWeapon)
                    return false;
                if (_npcHelperService.CanSeeNpc(npc.Instance, hero.Instance, false))
                    return false;
            }
            return IsGothic1 || _dialogDomain.IsOffered(npc, hero);
        }

        /// <summary>
        /// G2: "pickpocket" chosen in the NPC's dialog (B_Beklauen). True if the gold was taken, false if caught.
        /// </summary>
        public bool StealGold(NpcContainer npc)
        {
            return _dialogDomain.Steal(npc, Hero);
        }

        /// <summary>
        /// G1: one item taken, caught with the talent value's chance.
        /// </summary>
        public bool RollCaught()
        {
            var hero = Hero;
            if (hero?.Instance == null)
                return false;
            var chance = _npcHelperService.ExtNpcGetTalentValue(hero.Instance, GetTalentId());
            var roll = Random.Range(0, 100);
            Logger.Log($"[Pickpocket] roll {roll} vs. {chance} % to get caught", LogCat.Npc);
            return roll < chance;
        }

        public void CatchThief(NpcContainer victim)
        {
            var hero = Hero;
            if (victim?.Instance == null || hero?.Instance == null)
                return;

            var isVictimReacting = _npcAiService.ExecuteSinglePerception(VmGothicEnums.PerceptionType.CatchThief,
                victim, victim.Instance, hero.Instance);
            var isWitnessed = _npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessTheft,
                victim.Instance, hero.Instance, exclude: victim);
            Logger.Log($"[Pickpocket] {victim.Instance.GetName(NpcNameSlot.Slot0)} caught the hero " +
                       $"(PERC_CATCHTHIEF {(isVictimReacting ? "sent" : "not registered")}, " +
                       $"PERC_ASSESSTHEFT {(isWitnessed ? "sent" : "not sent")})", LogCat.Npc);
        }

        // Read per call: a profile switch (G1 -> G2) keeps this singleton, the talent ids differ (G1 6, G2 12).
        private int GetTalentId()
        {
            return _gameStateService.GothicVm.GetSymbolByName(_talentSymbol)?.GetInt(0) ?? 6;
        }
    }
}
