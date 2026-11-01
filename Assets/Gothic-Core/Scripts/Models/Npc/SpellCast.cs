using System.Collections.Generic;
using Gothic.Core.Models.Container;
using ZenKit.Daedalus;

namespace Gothic.Core.Models.Npc
{
    /// <summary>
    /// A spell being cast, like the engine's cast state (OpenGothic Npc castLevel, manaInvested): the mana invested so
    /// far, the level reached and when the next mana goes in. Started and driven by MagicService for the NPC's
    /// CastSpell action and the VR hero's held rune or scroll.
    /// </summary>
    public class SpellCast
    {
        public NpcContainer Caster;
        // The scripts' other during the cast (Spell_ProcessMana). Null without one.
        public NpcContainer Target;
        public ItemInstance Item;
        public SpellInstance Spell;
        public int SpellId;
        public int ManaInvested;
        // 0-based like the engine's invest level, the scripts see it 1-based (Npc_GetActiveSpellLevel).
        public int Level;
        public float NextInvestTime;
        // NPCs cast as soon as this much mana went in (AI_ReadySpell). 0 for the hero, who lets go to cast.
        public int ExpectedInvest;
        public SpellCastPhase Phase;
        // G1's chain lightning: the NPCs it zaps while invested, released (PERC_ASSESSSTOPMAGIC) when it ends.
        public readonly List<NpcContainer> ZappedVictims = new();
    }
}
