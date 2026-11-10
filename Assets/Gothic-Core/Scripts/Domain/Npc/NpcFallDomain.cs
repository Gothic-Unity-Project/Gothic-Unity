using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Gothic's falling rules for NPCs and monsters (C_GILVALUES in Species.d): who falls at all, which drop is safe
    /// and how much a fall hurts. Pure rules - the NpcJumpFall adapter does the moving.
    /// </summary>
    public static class NpcFallDomain
    {
        /// <summary>
        /// WATER_DEPTH_KNEE and WATER_DEPTH_CHEST of flying monsters (Bloodfly, Demon): they never touch water or fall.
        /// </summary>
        private const int _flyerWaterDepth = 999999;

        /// <summary>
        /// Safe drops are probed this far in front of the NPC.
        /// </summary>
        private const float _dropProbeDistance = 1f;

        /// <summary>
        /// Both water depths (cm) of a guild mark it as flying.
        /// </summary>
        public static bool IsFlyer(int waterDepthKneeCm, int waterDepthChestCm)
        {
            return waterDepthKneeCm >= _flyerWaterDepth && waterDepthChestCm >= _flyerWaterDepth;
        }

        /// <summary>
        /// Whether the NPC may fall, climb or be thrown: not dead/unconscious/swimming/diving, not a flyer, not in water.
        /// </summary>
        public static bool CanMove(GuildValuesInstance values, NpcContainer npc)
        {
            var bodyState = npc.Props.BodyState;
            if (bodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious
                or VmGothicEnums.BodyState.BsSwim or VmGothicEnums.BodyState.BsDive)
                return false;

            var guild = npc.Instance.GetValuesGuild();
            if (values != null && IsFlyer(values.GetWaterDepthKnee(guild), values.GetWaterDepthChest(guild)))
                return false;
            return npc.Vob?.AiHuman == null || npc.Vob.AiHuman.WaterLevel == (int)ZenGineConst.WaterLevel.Normal;
        }

        /// <summary>
        /// FALLDOWN_HEIGHT in metres: falls up to it don't hurt. 0 if the guild has none.
        /// </summary>
        public static float GetSafeFallHeight(GuildValuesInstance values, NpcContainer npc)
        {
            return (values?.GetFallDownHeight(npc.Instance.GetValuesGuild()) ?? 0) / 100f;
        }

        /// <summary>
        /// Ground below the next meter in that direction, not deeper than FALLDOWN_HEIGHT: a chasing NPC may jump down
        /// to a target below instead of stopping at the edge. position is the NPC's root, restHeight its height above
        /// the feet.
        /// </summary>
        public static bool IsSafeDropAhead(GuildValuesInstance values, NpcContainer npc, Vector3 position,
            float restHeight, Vector3 direction)
        {
            if (restHeight <= 0f)
                return false;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return false;

            var safeHeight = GetSafeFallHeight(values, npc);
            if (safeHeight <= 0f)
                return false;

            var origin = position + direction.normalized * _dropProbeDistance;
            return Physics.Raycast(origin, Vector3.down, restHeight + safeHeight, 1 << Constants.DefaultLayer,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Engine: FALLDOWN_DAMAGE HP for every meter above FALLDOWN_HEIGHT (cm), minus PROT_FALL. 0 for no damage.
        /// </summary>
        public static int GetFallDamage(GuildValuesInstance values, NpcContainer npc, float height)
        {
            if (values == null)
                return 0;

            var guild = npc.Instance.GetValuesGuild();
            var safeHeight = values.GetFallDownHeight(guild) / 100f;
            var damagePerMeter = values.GetFallDownDamage(guild);
            if (safeHeight <= 0f || damagePerMeter <= 0 || height <= safeHeight)
                return 0;

            var damage = Mathf.RoundToInt((height - safeHeight) * damagePerMeter) -
                         npc.Vob.GetProtection((int)DamageType.Fall);
            return Mathf.Max(damage, 0);
        }
    }
}
