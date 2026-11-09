using System;
using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;

namespace Gothic.Core.Extensions
{
    public static class NpcContainerExtension
    {
        /// <summary>
        /// The NPC reached its mob and is in one of its states (e.g. seated on a bench).
        /// </summary>
        public static bool IsUsingMob(this NpcContainer npc)
        {
            return npc.PrefabProps?.CurrentInteractable != null && npc.Props.CurrentInteractableStateId >= 0;
        }

        /// <summary>
        /// Knocked out (BS_UNCONSCIOUS), not a magically frozen NPC. Like the scripts (C_NpcIsDown: ZS_Unconscious,
        /// ZS_MagicSleep, dead), ZS_MagicFreeze sets BS_UNCONSCIOUS via AI_PlayAniBS but the victim isn't down:
        /// Mroczne Tajemnice's crawlers freeze with their web and then bite. Before the frozen NPC was invisible to
        /// monsters, dropped as a target and killed by the next hit.
        /// </summary>
        public static bool IsKnockedOut(this NpcContainer npc)
        {
            return npc?.Props != null && npc.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious &&
                   !npc.IsMagicFrozen();
        }

        /// <summary>
        /// Dead or knocked out (a frozen NPC isn't, see IsKnockedOut).
        /// </summary>
        public static bool IsDown(this NpcContainer npc)
        {
            return npc?.Props != null && (npc.Props.IsDead || npc.IsKnockedOut());
        }

        public static bool IsMagicFrozen(this NpcContainer npc)
        {
            var state = npc?.Vob?.CurrentStateName;
            return state != null && state.StartsWith(DaedalusConst.ZsMagicFreeze, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly string[] _sleepStates = { "ZS_SLEEP", "ZS_MAGICSLEEP", "ZS_BABE_SLEEP", "ZS_SLEEPER" };
        private static GameStateService _gameStateService;

        /// <summary>
        /// A true human - not a monster or an orc. Like the scripts' C_NpcIsHuman: guild below GIL_SEPERATOR_HUM, the
        /// value the loaded scripts define (GameStateService.GuildSeparatorHuman, read once per VM by BootstrapDomain -
        /// a mod may move it). Before DI is up (EditMode tests) G1's/G2's 16.
        /// </summary>
        public static bool IsHuman(this NpcContainer npc)
        {
            _gameStateService ??= ReflexProjectInstaller.DIContainer?.Resolve<GameStateService>();
            var separator = _gameStateService?.GuildSeparatorHuman ?? (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM;
            return npc.Instance.Guild < separator;
        }

        /// <summary>
        /// Asleep in bed (ZS_Sleep, G1's ZS_Babe_Sleep/ZS_Sleeper) or by the sleep spell (ZS_MagicSleep).
        /// </summary>
        public static bool IsAsleep(this NpcContainer npc)
        {
            var state = npc?.Vob?.CurrentStateName;
            if (state == null)
                return false;
            foreach (var sleepState in _sleepStates)
            {
                if (state.Equals(sleepState, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Like the engine (OpenGothic DamageCalculator, TouchDamage): the damage of each damage type in the mask
        /// (bit index = DamageType) against the NPC's protection of that type. An immune type (protection below 0) is
        /// left out - isImmune when all of them were.
        /// </summary>
        /// <param name="damageOfType">(damage type, protection >= 0) - the damage of this type after the protection.
        /// </param>
        public static int ApplyProtection(this NpcContainer target, int damageTypeMask,
            Func<int, int, int> damageOfType, out bool isImmune)
        {
            var damage = 0;
            isImmune = true;
            for (var i = 0; i < FightConst.DamageTypeCount; i++)
            {
                if ((damageTypeMask & (1 << i)) == 0)
                    continue;

                var protection = target.Vob.GetProtection(i);
                if (protection < 0)
                    continue;

                damage += damageOfType(i, protection);
                isImmune = false;
            }
            return damage;
        }
    }
}
