using System;
using System.Collections.Generic;

namespace Gothic.Core.Const
{
    /// <summary>
    /// Spell effect categories keyed by spellFXInstanceNames entries (e.g. "Skeleton", "Icewave").
    /// These groupings are fixed Gothic game-design facts (which spells summon, which are AOE), not
    /// per-mod data, so they're listed here once and shared by every spell-casting call site
    /// (VRRuneCaster for the player, AttackPlayAni for NPCs, FightService for the actual hit/AOE fan-out)
    /// instead of duplicating the same name checks in each file.
    /// </summary>
    public static class SpellConst
    {
        /// <summary>
        /// Spells whose entire effect is Wld_SpawnNpcRange in their Spell_Logic_* Daedalus function —
        /// no C# damage handling exists (or is needed) for these.
        /// </summary>
        public static readonly HashSet<string> SummonEffectNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Skeleton", "Demon", "Golem", "ArmyOfDarkness"
        };

        /// <summary>
        /// Spells that hit every NPC in range of the caster (TARGET_COLLECT_ALL / _FALLBACK_NONE in
        /// spells_params.d) rather than a single aimed target.
        /// </summary>
        public static readonly HashSet<string> AoeEffectNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Icewave", "Chainlightning", "Firerain", "Massdeath", "Stormfist"
        };

        /// <summary>
        /// DeveloperConfig.EnableWindfistThrow: DAM_FLY spells that throw their victims away.
        /// </summary>
        public static readonly HashSet<string> ThrowEffectNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "WindFist", "Stormfist"
        };

        /// <summary>
        /// DeveloperConfig.EnableTwoHandSpellCharge: spells charged in levels (Spell_ProcessMana returns SPL_NEXTLEVEL)
        /// - G1's fireball/fire storm/thunderball/wind fist/storm fist/fire rain, G2's charged ones.
        /// </summary>
        public static readonly HashSet<string> ChargeEffectNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Fireball", "Firestorm", "Thunderball", "WindFist", "Stormfist", "Firerain",
            "ChargeFireball", "ChargeZap", "Pyrokinesis", "Waterfist"
        };

        /// <summary>
        /// Fallback AOE radius (cm) for spells whose targetCollectRange isn't exposed as a standalone
        /// SPL_RANGE_* const (Icewave/Firerain/Massdeath hardcode 1000 directly on their C_Spell_Proto
        /// instance in vanilla Gothic) — used only when no SPL_RANGE_<NAME> symbol is found at runtime.
        /// </summary>
        public const int DefaultAoeRangeCm = 1000;

        /// <summary>
        /// Spells whose impact leaves a burning area (spellFX_*_SPREAD, emFXCreate of the collide effect) - the engine
        /// damages everyone it reaches, not only the NPC the projectile hit. Also when it lands on the ground.
        /// </summary>
        public static readonly HashSet<string> SpreadEffectNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Firestorm",
            // G2: the effect name of a fire storm rune (spellFX_Pyrokinesis with MFX_Firestorm_* visuals).
            "Pyrokinesis"
        };

        /// <summary>
        /// Radius (m) of the spread area around the impact. Not in the scripts (engine/VFX side) - chosen by feel.
        /// </summary>
        public const float SpreadRangeMeters = 2f;
    }
}
