# Fight

How one melee swing becomes a hit, a knockout or a death. The decisions behind it:
`Docs/adr/2026-10-29-npc-melee.md`.

## A swing

1. A state loop (`ZS_*_Attack_Loop`) calls `AI_Attack`, which queues the `Attack` action. It compares
   `FightService.GetFightDistance` (between the bodies, 3D like the engine) with `GetMeleeReach` (fight range base +
   weapon range): within it is the W-range, within three times it the G-range. The FIGHT AI table of that range picks
   the moves, each queued as an `AttackPlayAni`. Without a drawn weapon, `AI_Attack` draws first.
2. `AttackPlayAni` runs to the enemy (G-range) or swings. A swing hits at its `DEF_OPT_FRAME` if the target is within
   the reach and at most 60 degrees to the side. An attack queued in the swing's `DEF_WINDOW` continues the same
   animation with its next part (`AttackCombo`, `AiHandler.PreloadNextAttack`).
3. A hit raises `GlobalEventDispatcher.FightHit(attacker, target, hit point, weapon)`. The VR hero raises the same
   event when his weapon touches a target fast enough (`VRWeaponAttackAdapter`); the weapon is the one in his hand.

## The hit

`FightService.OnHit`:
- Nobody hurts the hero or the NPC in a running dialog. A hit on a knocked out NPC kills it.
- The damage is the engine's formula (`MeleeDamageDomain.CalculateDamage`), the part that differs between the games
  through `IContextMeleeDamageService` (G1 critical hits, G2 hit chance). `NPC_FLAG_IMMORTAL` takes nothing.
- Above 0 hit points: a stumble and a hurt sound. The victim perceives `PERC_ASSESSDAMAGE`, the NPCs around
  `PERC_ASSESSOTHERSDAMAGE` and `PERC_ASSESSFIGHTSOUND`.
- At 0 hit points, one of three:
  - **The hero** is knocked out for 10 seconds (`KnockOutHero`), his attackers calm down. He can't die yet.
  - **A human not hostile to the attacker** (`MeleeDamageDomain.IsKnockedOutNotKilled`) is knocked out: 1 hit point,
    the weapon put away, `ZS_Unconscious` started as an engine state change (the old state's `_End` doesn't run), the
    fall played. The NPCs around perceive `PERC_ASSESSDEFEAT`.
  - **Everybody else dies**: `NpcService.Kill`.

## A death

`NpcDeathDomain.Die`, for every death (a hit, or a script lowering the hit points to 0):
1. Dead body state, 0 hit points, the weapon put away, the dying animation and the scream.
   `GlobalEventDispatcher.NpcDied`.
2. One frame later `ZS_Dead` and its loop run with the killer as other (`VmService.CallAsNpc`): experience for the
   hero's own kills, loot, quest variables. The hero's instance is copied to his VOB.
3. The NPCs around perceive `PERC_ASSESSMURDER`.

Each hit, knockout and death asks all NPCs of the world once whether they saw it
(`NpcAiService.SendPerceptionToWitnesses`).

## Differences from the original

- A swing hits by reach and angle, not by the weapon's bone touching the target (`DEF_HIT_LIMB`).
- A knocked out or killed NPC puts the weapon away at once; the original drops it to the ground and the NPC picks it
  up again when it wakes up. Our NPCs can't pick items up yet.
- Not there yet: hits of archers and mages, parrying, finishing moves, the hero's death, fleeing. See the ADR.
