# 2. Magic: VR casting, NPC casters, summons, transformations

[← Overview](README.md)

## What changed

**Hero casting in VR**
- Spell casting overhaul: the rune is held in one hand; the spell effect glows in the hand while mana is invested
  (at the spell's own `time_per_mana`), minimum cast time configurable for "instant" G2 spells.
- Projectile spells (fireball, fire arrow, lightning, ice block…) are charged in the free hand and thrown; throw
  strength from the swing; Gothic invest levels change effect and sound with a burst.
- G2 spells pay their mana in `Spell_Cast_*` like the engine (they fizzled and still ate mana). Refused spells refund.
- Telekinesis (capsule range, bag targeting), Light spell, teleports put the held rune into the backpack.
- Spells without `MFX_<name>_INIT` show their own `VISUALFX` in the hand (ice wave shows ice, not a fireball).

**NPC casters**
- Mages and archers complete their casts (protected from `Npc_ClearAIQueue` resets), get real spell effects,
  fire `PERC_ASSESSMAGIC` so Daedalus drives the reaction (sleep, freeze, fear, berserk…).

**Summons**
- Spawned beside the caster, never fight their master (`NpcContainer.SummonedBy` ↔ Friendly), run, defend the
  hero, and are a training dummy for their own summoner (hits don't turn them hostile).
- NPCs treat the hero's summons the way they treat the hero: enemies fight them, everyone else ignores them.
  **Deviation:** vanilla G2 makes humans hostile to the `GIL_SUMMONED_*` guilds (`B_InitMonsterAttitudes`), so guards
  attack summons there.

**Transformation scrolls**
- The hero becomes the monster: a puppet without AI, camera at its eyes, walks/strafes/swims/sneaks like Gothic.

**Other spells**
- Shrink scales monsters (`Mdl_SetModelScale`, kept on load). Fear: NPCs flee (`AI_Flee`). Berserk.

## Key commits

`eaba6cf6` casting overhaul · `d8e51ea3` NPC casters · `65a4ed33` casts complete · `942032bc` summons ·
`cdc628c2` throwable spells · `5e898826` G2 mana in Spell_Cast · `934b0305` time_per_mana · `a063883c` shrink ·
`f2ebe1f2`/`040ecaf8`/`fc784fa9` transformations · `64d0bd47` VISUALFX in hand · `04d53a8d` teleport keeps items ·
`79896b75` AI_Flee · `a6206509` summons as training dummy · `bc831b78` NPCs ignore the hero's summons

## Main files

`Gothic-VR/.../VobItem/VRRuneCaster.cs` (+1356) · `VRTransformPuppet.cs` (+748) · `VRTransformService.cs` ·
`Const/SpellConst.cs` · `Services/Npc/FightService.cs` (SpellHit) · `Domain/Npc/Actions/AnimationActions/Flee.cs`

## Config flags

`EnableThrowableSpells`, `EnableSpellTimePerMana`, `MinSpellCastSeconds`, `EnableVrTransformations`,
`EnableSummonIgnoresMasterHits`, `EnableNpcsIgnoreHeroSummons`, `EnableModelScale`, `EnableAiFlee`, `EnableTeleportKeepsHeldItems`,
`EnableSpellBodyFx`, `SummonSpawnRangeMultiplier`, `RangedCombatRangeMultiplier`, `EnableCasterPerception`.

## Review notes

- ✅ Mana and spell logic call the Daedalus functions (`Spell_ProcessMana`, `Spell_Cast_*`) instead of hardcoding.
- ⚠️ **`VRRuneCaster.cs` is a 1,356-line adapter.** `CLAUDE.md` asks adapters to stay thin. Charging, invest levels,
  projectile throwing and spell lookup should move into a service/domain (`VRSpellCastService`?).
- ⚠️ `VRTransformPuppet.cs` (748 lines) is the same story.
- ⚠️ G2's AI uses the G1 fight move table (known issue).

## How to test

- G1 MT and G2: fireball charged in the free hand and thrown; charge levels show bursts.
- G2: summon a golem; it runs, fights enemies, ignores your hits.
- Transformation scroll: walk, swim, sneak as the wolf.
- Shrink the G1 troll; save, load — still small.
