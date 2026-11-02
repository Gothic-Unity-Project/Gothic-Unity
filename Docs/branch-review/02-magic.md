# 2. Magic: VR casting, NPC casters, summons, transformations

[← Overview](README.md)

<!-- toc -->
## Contents

- [What changed](#what-changed)
  - [Hero casting in VR](#hero-casting-in-vr)
  - [NPC casters](#npc-casters)
  - [Summons](#summons)
  - [Transformation scrolls](#transformation-scrolls)
  - [Other spells](#other-spells)
  - [Added 2026-10-10 – 2026-10-11](#added-2026-10-10--2026-10-11)
  - [Added 2026-10-11 – 2026-10-13](#added-2026-10-11--2026-10-13)
  - [Added 2026-10-15](#added-2026-10-15)
  - [Added 2026-10-24](#added-2026-10-24)
  - [Added 2026-10-25 (afternoon)](#added-2026-10-25-afternoon)
  - [Added 2026-10-25 (evening)](#added-2026-10-25-evening)
  - [Added 2026-10-26](#added-2026-10-26)
- [Key commits](#key-commits)
- [Main files](#main-files)
- [Config flags](#config-flags)
- [Review notes](#review-notes)
- [How to test](#how-to-test)
<!-- /toc -->

## What changed

### Hero casting in VR

- Spell casting overhaul: the rune is held in one hand; the spell effect glows in the hand while mana is invested
  (at the spell's own `time_per_mana`), minimum cast time configurable for "instant" G2 spells.
- Projectile spells (fireball, fire arrow, lightning, ice block…) are charged in the free hand and thrown; throw
  strength from the swing; Gothic invest levels change effect and sound with a burst.
- G2 spells pay their mana in `Spell_Cast_*` like the engine (they fizzled and still ate mana). Refused spells refund.
- Telekinesis (capsule range, bag targeting), Light spell, teleports put the held rune into the backpack.
- Spells without `MFX_<name>_INIT` show their own `VISUALFX` in the hand (ice wave shows ice, not a fireball).

### NPC casters

- Mages and archers complete their casts (protected from `Npc_ClearAIQueue` resets), get real spell effects,
  fire `PERC_ASSESSMAGIC` so Daedalus drives the reaction (sleep, freeze, fear, berserk…).

### Summons

- Spawned beside the caster, never fight their master (`NpcContainer.SummonedBy` ↔ Friendly), run, defend the
  hero, and are a training dummy for their own summoner (hits don't turn them hostile).
- NPCs treat the hero's summons the way they treat the hero: enemies fight them, everyone else ignores them.
  **Deviation:** vanilla G2 makes humans hostile to the `GIL_SUMMONED_*` guilds (`B_InitMonsterAttitudes`), so guards
  attack summons there.

### Transformation scrolls

- The hero becomes the monster: a puppet without AI, camera at its eyes, walks/strafes/swims/sneaks like Gothic.

### Other spells

- Shrink scales monsters (`Mdl_SetModelScale`, kept on load). Fear: NPCs flee (`AI_Flee`). Berserk.

### Added 2026-10-10 – 2026-10-11

- **Burning:** fire spells (`DAM_FIRE`) set NPCs on fire for 4 s like the engine - `VOB_BURN` flames over the whole
  skinned body, fire light, burn sound and the `s_Fire_Victim` dance; another hit extends it.
- **Fire storm splash:** the burning area damages everyone within 2 m of the impact, also when it lands on the ground;
  knocked out NPCs aren't finished off.
- **NPC mages** cast one spell per attack (wind-up `t_MagRun_2_FBTShoot`, then `t_FBTShoot_2_Stand`) instead of bursts.
- Spamming a charged spell no longer stacks its effects in the hand; a throw clears them.

### Added 2026-10-11 – 2026-10-13

- **Charged spells can't be spammed:** firing before the spell is cast asks the game's `Spell_ProcessMana_Release`
  like the engine - fireball, fire storm, wind fist, ... go off at their level, fire rain, summons, transformations and
  the rest fizzle out (a summon army in 3 seconds, fire rain on repeat). Invested mana isn't refunded.
- **Summons have no target** like the engine (`targetCollectAlgo = TARGET_COLLECT_NONE`) - aimed at an NPC, the summon
  hit him for `damage_per_level` × mana and his magic perception turned him against the hero.
- **Only harmful spells hurt:** like the engine, only `SPELL_BAD` spells deal damage - sleep, light, charm, ...
  (`SPELL_NEUTRAL`/`SPELL_GOOD`) inherit `damage_per_level = 1` from `C_Spell_Proto` and hurt their target.
- **Thrown spells fly like a ball:** direction and speed come from the throwing hand's swing (sampled over 0.1 s), not
  the camera's view. A weak throw arcs with gravity, letting go without a swing drops the spell, and only a real throw
  homes on targets (homing disables gravity). In the simulator (WASD) the old camera aim stays. They fly at half the
  spell's own speed (`SpellThrowSpeedMultiplier`), and the throwing hand can't grab the rune: squeezing grip + trigger
  (like a fist) grabbed it, both hands on the rune left none to throw with and fire balls never came.
- **NPC spells are visible:** a flying spell (CAST key `emTrjMode_S` TARGET: fire ball, ice block, the crawler web)
  flies from the caster's hand to its target as its PFX and hits when it arrives; the others show their cast effect and
  the hit effect on the target. They used to hit invisibly at the cast animation's frame.
- **Sleep and freeze on the hero:** the hero runs no Daedalus states (its `PERC_ASSESSMAGIC` never put it in
  `ZS_MagicSleep`), so the caster cast again and again (G2 Xardas). Now the state is set on the hero's VOB (G1 via the
  spell, G2's `Spell_Logic_Sleep` via `AI_StartState`): the scripts see it (`Npc_IsInState`, `C_NpcIsDown`), the hero
  can't move for `SPL_TIME_SLEEP`/`SPL_TIME_FREEZE`, damage wakes him up. NPC spells follow the "only `SPELL_BAD`
  hurts" rule too.

### Added 2026-10-15

- **NPC spells can be dodged:** a flying spell steers to a mix of where its target was at the cast and where it is now
  (`NpcSpellHoming` = 0.2), hits only within 0.8 m and otherwise flies on and fades - fully homing, nobody could dodge
  a crawler's web.

### Added 2026-10-24

- **Spell ring** (`59fe4f51`, `EnableVrSpellRing`, ini `spellRing`): Gothic's spell selection (hold the magic key, the
  equipped spells circle the hero). Both hands empty, ~10 cm above the eyes, both triggers for 1 s (haptic ramp) open/close
  `VRSpellRing`: a carousel 0.55 m in front of the player and 0.2 m below the eyes, in the player controller's space, of
  the hero's equipped `ITEM_KAT_RUNE` items in equip order (max 8). The front spell shows its effect after 2 s
  (`VRSpellEffect`: MFX_[NAME]_INIT or the VISUALFX visual, like `VRRuneCaster.SpawnSpellVfx`), a hand within 9 cm
  enlarges a spell ×1.4 and shows its description ("Runa" is every rune's name). Trigger + moving sideways turns it,
  a trigger click (no move) unequips; an empty slot below the limit equips a held rune/scroll (`VRItemUser.Equip` way:
  into the inventory with [E]). Grip takes a spell: an invisible, non-grabbable rune is parented to the hand (an HVR
  force grab without colliders flung the physics hand away), `VRRuneCaster.RingHand` makes that hand aim/cast and
  charge/throw throwables; `VRSpellRingItem` shows the effect while not casting and puts the spell away on a trigger
  click of the other empty hand (also when the spell hand grabs something or the hero is down). The spell stays in the
  inventory throughout, like Gothic's readied spell.

### Added 2026-10-25 (afternoon)

- **Spell damage types** (`42b42b6d`, `EnableEngineMeleeDamage`): a spell's hit took fire protection for every spell -
  `OnSpellHit` passes the protection of the spell's own `damageType` (Windfist `DAM_FLY`, lightning `DAM_MAGIC`).
- **Ranged knockout** for spells too (`7542c3d0`) - see [Combat](01-combat.md).

### Added 2026-10-25 (evening)

- **The spell ring became the favorites ring** (`ceeee724`): `VRSpellRing` -> `VRFavoritesRing`, `VRSpellRingItem` ->
  `VRFavoritesRingSpell`; spells work as before. Weapons, items and money in it: see
  [Inventory](06-inventory-trade-loot.md).

### Added 2026-10-26

- **One press casts** (`b04baf3f`): the press aims and starts charging, the release asks
  `Spell_ProcessMana_Release` (was: aim press, charge press, fire press).
- **Magic circles** (`e3ed2cad`): `C_ITEM.mag_circle` vs `NPC_TALENT_MAGE`; too high = not cast nor equipped,
  `G_CanNotCast` says how many circles are missing.
- **Single-use scrolls** (`5bc6c82b`): `ITEM_MULTI` spell items are used up by their cast (Immersion option).
- **G1 chain lightning** (`956f1711`, `VRChainLightning`): charges one `time_per_mana`, zaps the NPC in front, jumps
  every 3 invest levels, back to the hero with nobody left; `ZS_Zapped` does the damage, the release sends
  `PERC_ASSESSSTOPMAGIC`.
- **Windfist/Stormfist throw** (`56681067`): `DAM_FLY` victims fly back and land lying (`NpcJumpFall.TryThrow`).
- **Spell lights and earthquake** (`0b9d2562`): VISUALFX `lightPresetName` from `LIGHTPRESETS.ZEN` as pooled stationary
  lights, the invest keys' `LightRange`, `FX_EarthQuake` rumbles the controllers; body effects with a downward offset
  sit on the ground.
- **Effects in hand and ring** (`137b6c72`, `e55b1636`): Gothic's own particle motion and emission (they were forced
  to 0-0.05 m/s at 50/s), the ring fits them into the slot; chain lightning shows its `emFXCreate` effect.
- **Transformations** (`b3eaf7cb`, `a5bc8d57`, `6ed57fb8`, `5e7bbfc0`): the player's speed times the monster's run
  root motion; a monster without a visual keeps the scroll / falls back to `SPL_TRF_<X>` -> instance `<X>` (MT's lurker
  scroll passes `BLACKDRAGON`); a ring spell replaces the one in the hand (two casters cast at once).

## Key commits

`eaba6cf6` casting overhaul · `d8e51ea3` NPC casters · `65a4ed33` casts complete · `942032bc` summons ·
`cdc628c2` throwable spells · `5e898826` G2 mana in Spell_Cast · `934b0305` time_per_mana · `a063883c` shrink ·
`f2ebe1f2`/`040ecaf8`/`fc784fa9` transformations · `64d0bd47` VISUALFX in hand · `04d53a8d` teleport keeps items ·
`79896b75` AI_Flee · `a6206509` summons as training dummy · `bc831b78` NPCs ignore the hero's summons

2026-10-10/11: `9b7bf640` burning · `ca86ccb8` fire storm splash · `74066e65` NPC mage one spell per attack · `f819bab9`
charge effects cleared

2026-10-11/13: `ff788ac1` spell release check · `d940825d` summons without target · `0d6977ad` only harmful spells hurt ·
`aaab7cdf` throw physics · `cac631f2` throw hand can't grab the rune · `1e7895fb` visible NPC spells · `3854e391`
sleep/freeze on the hero

2026-10-15: `b6dcf8c7` dodgeable NPC spells

2026-10-24: `59fe4f51` spell ring

2026-10-25: `42b42b6d` spell damage types

2026-10-25: `ceeee724` favorites ring
2026-10-26: `b04baf3f` one press casts · `e3ed2cad` magic circles · `5bc6c82b` single-use scrolls · `956f1711` chain lightning · `56681067` windfist throw · `0b9d2562` spell lights · `137b6c72`/`e55b1636` effects in hand and ring · `b3eaf7cb`/`a5bc8d57`/`6ed57fb8`/`5e7bbfc0` transformations

## Main files

`Gothic-VR/.../VobItem/VRRuneCaster.cs` (+1356) · `VRTransformPuppet.cs` (+748) · `VRTransformService.cs` ·
`Const/SpellConst.cs` · `Services/Npc/FightService.cs` (SpellHit) · `Domain/Npc/Actions/AnimationActions/Flee.cs`

## Config flags

`EnableThrowableSpells`, `EnableSpellTimePerMana`, `MinSpellCastSeconds`, `EnableVrTransformations`,
`EnableSummonIgnoresMasterHits`, `EnableNpcsIgnoreHeroSummons`, `EnableModelScale`, `EnableAiFlee`, `EnableTeleportKeepsHeldItems`,
`EnableSpellBodyFx`, `SummonSpawnRangeMultiplier`, `RangedCombatRangeMultiplier`, `EnableCasterPerception`.

2026-10-10/11: `EnableBurning`, `EnableSpellSpreadDamage`

2026-10-11/13: `EnableSpellReleaseCheck`, `EnableSummonSpellWithoutTarget`, `EnableOnlyBadSpellsDamage`,
`EnableSpellThrowPhysics`, `SpellThrowSpeedMultiplier`, `EnableThrowRuneOneHand`, `EnableNpcSpellFx`,
`EnableHeroSpellStates`

2026-10-15: `NpcSpellHoming`

2026-10-24: `EnableVrSpellRing`

2026-10-25: `EnableVrSpellRing` (Immersion option renamed "Favorites ring", ini `spellRing`)
2026-10-26: `EnableSpellHoldToCast`, `EnableRuneCircleRequirement`, `EnableScrollUseOption`, `EnableVrChainLightning`, `EnableWindfistThrow`, `EnableVisualFxLights`, `EnableVisualFxEarthquake`, `EnableBodyFxAtGround`, `EnableSpellEffectGothicMotion`, `EnableTransformMonsterSpeed`, `EnableTransformCancelKeepsScroll`, `EnableTransformFallbackInstance`, `EnableRingSpellReplacesHandSpell`

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
- Fireball an NPC: flames on the whole body for 4 s. Fire storm next to a group: everyone within 2 m burns.
- An NPC mage casts single spells with a wind-up, no bursts.

- Charge a fireball and release early: weaker fireball. Same with fire rain / a summon: it fizzles.
- Aim a summon rune at an NPC: only the summons appear, the NPC takes no damage and stays calm.
- G1: cast sleep on an NPC - he falls asleep without losing HP; a fireball still hurts.
- G2: anger Xardas - his sleep spell flies at you, you can't move and lose no HP, a hit wakes you up, he doesn't
  re-cast while you sleep. An NPC mage's fire ball flies visibly and hurts when it arrives.
- Throw a fireball with grip + trigger in the throwing hand - it flies, the rune stays in the other hand.
- Throw a fireball hard at an NPC: it flies fast and homes. Throw softly: it arcs into the ground. Open the hand without
  a swing: the fireball drops in front of you.
- Mroczne Tajemnice crawler / G2 fire mage: step aside when the spell flies at you - it misses and fades behind you.
- Hold the trigger with a fireball: it charges at once; let go early - a weaker fireball.
- G1 with circle 0: a circle-2 rune doesn't cast, the message says 2 circles are missing.
- Chain lightning at two NPCs: the first is zapped after ~1 s, the lightning jumps after ~2 s; alone it turns on you.
- Windfist at a scavenger: it flies back, lands lying, gets up.
- Summon a demon: red glow around you, the controllers rumble.
- MT: lurker scroll -> lurker; wolf scroll -> you run faster.
