# 1. Combat: VR weapons, NPC melee, knockouts

[← Overview](README.md)

## What changed

**NPC melee** (was mostly missing on `main`)
- `AI_Attack` with Gothic's fight AI moves, combos cut at `DEF_WINDOW`, hits fired at `DEF_OPT_FRAME`
  (or at the animation's end when it has none — wolves).
- Weapon draw/sheath with the right animations, sounds and fight overlays; two-handers on the back.
- Chase give-up (`$RUNCOWARD`) after ~15 s, approach spread so NPCs don't stack.
- NPC vs NPC hit detection: reach + 60° arc check (not bone colliders yet).

**Knockouts and death**
- Engine rule (OpenGothic `checkHealth`): a human beaten in melee goes unconscious unless hostile to the attacker
  (permanent attitude to the hero, guild attitude between NPCs). Bandits die, citizens don't. Arrows kill.
- `ZS_Dead` runs on death (XP, loot init, quest variables), deferred one frame (G2 `ZS_Dead` is heavy).
- Hero knockout: attackers reset their temp attitude, sheathe and clear their AI queue, so the fight ends at once.
- NPCs drop their held weapon on death/knockout; the dropped weapon is a usable world item.

**VR weapons**
- Bows with a real string (grab with Grip, draw), aim along drawing hand → bow hand, two-handed hold.
- Crossbows shoot bolts from the inventory; 60 %+ skill (G1: master rank) allows one hand.
- Arrows/bolts that miss stay in the world. Thin box colliders along bow meshes.
- NPC weapon hitbox covers the whole body.

**Fixes worth knowing**
- `Npc_ChangeAttribute` adds a delta instead of setting the value; no more clamping strength to dexterity.
- Bystanders don't turn on whoever beats the hero; an NPC's started swing isn't cut by another NPC's hit
  (two bandits stunlocked Cavalorn).
- Berserk (G1): victims attack the next living NPC; guards knock the berserker out instead of killing.

## Key commits

`ed02071c` NPC melee · `6ebb9d56` draw/sheath · `59e39ebe` combat context + `Npc_GetNextTarget` · `9fa477b4` knockout ·
`e5dfc6ce` chase give-up, finishing moves · `4bb7903c` drop weapon · `a4d92755` hitbox · `d575c3b3`/`bf4969b0` bows ·
`c0299809` crossbow · `1770b593`/`50d634e2` give up chase · `c1a4c1ad` AI_Attack draws first, two-handers ·
`fe5777c3`/`7fef08fd`/`035ca617` berserk · `887af70a` engine knockout rule · `84f47c83` hit at opt frame

## Main files

`Services/Npc/FightService.cs` (+878) · `Domain/Npc/Actions/AnimationActions/AttackPlayAni.cs` (+522) ·
`Attack.cs` · `AiDrawWeapon.cs` · `UndrawWeapon.cs` · `Adapters/Animations/AnimationSystem.cs` ·
VR: `VRBow.cs`, `VRCrossbow.cs`, `VRProjectile.cs`, `VRRangedService.cs`, `VRWeaponService.cs`

## Config flags

`EnableNpcCombatCombos`, `EnableNpcHitDetection`, `EnableEngineUnconsciousRule`, `EnableNpcSwingKeepsOnNpcHit`,
`EnableNpcHitAtOptimalFrame`, `EnableAiAttackDrawsWeapon`, `EnableFasterChaseGiveUp`, `EnableGuardsStopBerzerk`,
`EnableVrBows`, `EnableVrCrossbow`, `EnableCrossbowMasterOneHand`, `EnableHeroDropsWeaponsOnKnockout`,
`EnableNpcRangedCombat` (**off** in Production), cheats `EnableOneHitKill`/`EnableOneHitKnockout`.

## Review notes

- ✅ Rules follow the engine (OpenGothic used as reference) and are commented with the "why".
- ⚠️ **NPC vs NPC hits are a reach + arc check, not `DEF_HIT_LIMB` bone colliders** as `CLAUDE.md` describes.
  Good enough in play; the real limb collision is still a TODO.
- ⚠️ `FightService` grew to a large class doing hit resolution, perceptions, party logic, berserk and knockouts.
  Worth splitting (e.g. a `KnockoutDomain`, a `FightPerceptionDomain`).
- ⚠️ Several C# heuristics go beyond vanilla to fix VR-specific situations (target switching for non-hero
  attackers, "swing keeps on NPC hit"). Each is behind a flag and documented, but they are deviations.
- ⚠️ `EnableNpcRangedCombat` is off in Production: NPC archers don't shoot in a release build. Decide on purpose.

## How to test

- G2: Cavalorn and the bandits in the cave — both sides lose HP, bandits die.
- G2 city: hit a citizen; guards knock you out; everyone stops fighting; nobody dies in a brawl.
- G1: a wolf attacking an NPC hurts him. Berserk spell on an NPC near guards.
- VR bow and crossbow on a target; pick up the missed arrow.
