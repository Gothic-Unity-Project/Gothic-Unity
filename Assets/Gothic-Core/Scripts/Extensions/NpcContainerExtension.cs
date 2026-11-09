using System;
using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;

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

        /// <summary>
        /// A true human (guild below GIL_SEPERATOR_HUM, G1 and G2) - not a monster or an orc.
        /// </summary>
        public static bool IsHuman(this NpcContainer npc)
        {
            return npc.Instance.Guild < (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM;
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
