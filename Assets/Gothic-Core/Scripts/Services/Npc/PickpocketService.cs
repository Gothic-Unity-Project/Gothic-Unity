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
    /// DeveloperConfig.EnableVrPickpocket: G1's pickpocketing (the engine's steal window - G2 does it in dialogs,
    /// DIA_*_PICKPOCKET). The hero with NPC_TALENT_PICKPOCKET sneaks up to a human who can't see him and takes items
    /// from his pockets. Every item taken can get him caught: the talent value is the chance (Npc_Default 90, each
    /// level -40 by B_GiveSkill: 50 trained, 10 master - Fingers: "only as a master you take something without him
    /// noticing", "even the best get caught sometimes").
    /// Caught: the victim gets PERC_CATCHTHIEF (ZS_CatchThief: whirls around, ZS_AssessEnemy), witnesses within
    /// HAI_DIST_ASSESSTHEFT the passive PERC_ASSESSTHEFT with the victim set (B_AssessTheft: "$BEHINDYOU").
    /// </summary>
    public class PickpocketService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcAiService _npcAiService;

        // Seconds a hand that was near the purse can't start a talk with its NPC (HVR grabs the NPC with the same grip).
        private const float _talkBlockSeconds = 1f;
        private NpcContainer _talkBlockedNpc;
        private float _talkBlockedUntil;

        /// <summary>
        /// The NPC being robbed right now (VR: his pockets are open).
        /// </summary>
        public NpcContainer RobbedNpc { get; set; }

        /// <summary>
        /// A hand is at the NPC's purse: a grip there is for the purse, not for talking to him.
        /// </summary>
        public void BlockTalk(NpcContainer npc)
        {
            _talkBlockedNpc = npc;
            _talkBlockedUntil = Time.time + _talkBlockSeconds;
        }

        public bool IsTalkBlocked(NpcContainer npc)
        {
            return npc != null && (npc == RobbedNpc || (npc == _talkBlockedNpc && Time.time < _talkBlockedUntil));
        }

        private const string _talentSymbol = "NPC_TALENT_PICKPOCKET";
        private const string _humanSeparatorSymbol = "GIL_SEPERATOR_HUM";
        private int _talentId = -1;
        private int _humanSeparator = -1;

        // No game picked yet (profile menu): no VM and no game version.
        public bool IsEnabled => _configService.Dev.EnableVrPickpocket && _configService.Gothic != null &&
                                 _gameStateService.GothicVm != null && _contextGameVersionService.IsGothic1();

        public NpcContainer Hero => (_gameStateService.GothicVm?.GlobalHero as NpcInstance)?.GetUserData();

        /// <summary>
        /// The hero knows pickpocketing (Fingers, G1) and sneaks.
        /// </summary>
        public bool CanHeroSteal()
        {
            var hero = Hero;
            if (!IsEnabled || hero?.Props == null || _gameStateService.Dialogs.IsInDialog)
                return false;
            return hero.Props.BodyState == VmGothicEnums.BodyState.BsSneak &&
                   _npcHelperService.ExtNpcGetTalentSkill(hero.Instance, GetTalentId()) > 0;
        }

        /// <summary>
        /// A human who is up, not fighting and doesn't see the hero (C_NpcIsHuman, the engine's steal check).
        /// </summary>
        public bool CanBeRobbed(NpcContainer npc)
        {
            var hero = Hero;
            if (npc?.Instance == null || npc.Props == null || npc.Go == null || hero?.Instance == null || npc == hero)
                return false;
            if (npc.PrefabProps != null && npc.PrefabProps.IsHero())
                return false;
            if (npc.Instance.Guild >= GetHumanSeparator() || NpcHelperService.IsDown(npc))
                return false;
            if ((VmGothicEnums.WeaponState)npc.Vob.FightMode != VmGothicEnums.WeaponState.NoWeapon)
                return false;
            return !_npcHelperService.CanSeeNpc(npc.Instance, hero.Instance, false);
        }

        /// <summary>
        /// One item taken: caught with the talent value's chance.
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
            var witnesses = _npcAiService.BroadcastPassivePerception(hero, VmGothicEnums.PerceptionType.AssessTheft,
                victim.Instance, hero.Instance, victim);
            Logger.Log($"[Pickpocket] {victim.Instance.GetName(NpcNameSlot.Slot0)} caught the hero " +
                       $"(PERC_CATCHTHIEF {(isVictimReacting ? "sent" : "not registered")}, " +
                       $"PERC_ASSESSTHEFT to {witnesses} NPC(s))", LogCat.Npc);
        }

        private int GetTalentId()
        {
            if (_talentId < 0)
                _talentId = _gameStateService.GothicVm.GetSymbolByName(_talentSymbol)?.GetInt(0) ?? 6;
            return _talentId;
        }

        private int GetHumanSeparator()
        {
            if (_humanSeparator < 0)
                _humanSeparator = _gameStateService.GothicVm.GetSymbolByName(_humanSeparatorSymbol)?.GetInt(0) ??
                                  (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM;
            return _humanSeparator;
        }
    }
}
