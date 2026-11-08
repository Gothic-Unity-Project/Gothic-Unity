using System;
using Gothic.Core.Models.Container;

namespace Gothic.Core.Services.Context
{
    /// <summary>
    /// The hero's parry. The platform knows whether he holds a block and which NPC
    /// weapons touched it (VR: both hands on a melee weapon, both triggers held). Core (HeroBlockDomain) asks and keeps
    /// the Gothic rules: which attackers can be blocked, the weapon clash, HeroBlockedHit.
    /// </summary>
    public interface IContextHeroBlockService
    {
        /// <summary>
        /// The hero holds a block right now - his own melee hits don't count meanwhile.
        /// </summary>
        bool IsBlocking(NpcContainer hero);

        /// <summary>
        /// Material (C_ITEM.material) of the weapon the blocking hero holds, -1 without one. It isn't equipped.
        /// </summary>
        int GetBlockWeaponMaterial(NpcContainer hero);

        /// <summary>
        /// An NPC's weapon (or fist) touched the hero's held weapon while he blocks.
        /// </summary>
        void ReportBlockContact(NpcContainer attacker);

        /// <summary>
        /// An NPC's melee hit reached the hero. False: no block, the hit lands now. True: the block decides - now or
        /// within a short wait for the weapons' touch - and calls onBlocked, or onMissed to let the hit land after all.
        /// </summary>
        bool TryBlock(NpcContainer attacker, NpcContainer hero, Action onBlocked, Action onMissed);
    }
}
