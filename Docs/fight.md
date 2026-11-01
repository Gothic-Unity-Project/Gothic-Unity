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
- Nobody hurts the hero or the NPC in a running dialog.
- A block from the front, within 45 degrees, stops the hit (`FightService.IsBlocked`, decisions in
  `Docs/adr/2026-10-31-parry.md`). An NPC blocks by its fight AI move: a parry (`AttackPlayAni.IsParrying`) holds
  against humans and monsters with a drawn weapon, animals bite through; a jump back (`IsJumpingBack`) dodges any hit.
  The VR hero parries with both hands on one melee weapon and both triggers held (`VRHeroBlock`). A parry clashes
  (`CS_IAI_ME_ME`); a blocked hit does nothing else, no damage and no blood.
- A hit on a knocked out NPC kills it.
- The damage is the engine's formula (`MeleeDamageDomain.CalculateDamage`), the part that differs between the games
  through `IContextMeleeDamageService` (G1 critical hits, G2 hit chance). `NPC_FLAG_IMMORTAL` takes nothing.
- Above 0 hit points: a stumble and a hurt sound. The victim perceives `PERC_ASSESSDAMAGE`, the NPCs around
  `PERC_ASSESSOTHERSDAMAGE` and `PERC_ASSESSFIGHTSOUND`.
- At 0 hit points, one of three:
  - **A human not hostile to the attacker** (`MeleeDamageDomain.IsKnockedOutNotKilled`) is knocked out: 1 hit point,
    the weapon put away, `ZS_Unconscious` started as an engine state change (the old state's `_End` doesn't run), the
    fall played. The NPCs around perceive `PERC_ASSESSDEFEAT`.
  - **The hero, knocked out**, lies for 10 seconds (`KnockOutHero`), his attackers calm down. Between the hero and an
    NPC the NPC's attitude counts both ways (`NpcHelperService.GetPermanentAttitude`, like OpenGothic
    `personAttitude`).
  - **Everybody else dies**: `NpcService.Kill`. Monsters and hostile humans kill the hero too.
- **Archers and crossbowmen** (`ShootRanged`, queued by `AI_Attack` while a bow or crossbow is drawn) aim, shoot and
  reload (`S_BOWAIM`, `S_BOWSHOOT`, `T_BOWRELOAD`). The fight AI uses the far ranged moves only (`FA_MY_FK_FOCUS_FAR`
  within the reference range of 20 m in G1, 15 m in G2). `FightService.Shoot` takes one arrow or bolt of the weapon's
  munition (none, no shot) and rolls the hit at the shot (`RangedDamageDomain`: certain up close, the shooter's
  chance at the reference range - G1 dexterity, G2 hit chance - none beyond 45 m). A miss is aimed 0.7 to 1.6 m beside
  the target and flies visibly past. `ArrowFlightDomain` flies the arrow at 30 m/s on a ballistic arc: it hits whoever
  it passes within 0.45 m of first, not only the target, and stops at walls. Friendly fire does nothing
  (`FightService.IsFriendlyFire`): an NPC's arrow doesn't hurt whom he's friendly with, a party member's arrow doesn't
  hurt the hero. Damage per type minus protection (G2 plus dexterity, G1 critical by the bow/crossbow talent). Arrows
  kill, they don't knock out, and leave the downed alone.
- **Mages** (`CastSpell`, queued by `AI_Attack` while a spell is drawn by `AI_ReadySpell`) cast within 35 m
  (`FA_MY_FK_FOCUS_MAG`). `MagicService` runs the engine's cast: `Spell_ProcessMana` for each portion of mana every
  `C_Spell.time_per_mana` (G1 takes 1 mana per portion), the NPC casts when its `AI_ReadySpell` invest is in, the VR
  hero when he lets go of the trigger (`Spell_ProcessMana_Release`, `VRSpellCaster`); G2 calls `Spell_Cast_<name>`.
  Projectiles (`SpellFlightDomain`, 10 m/s straight) hit the first NPC they pass; `SpellDamageDomain` takes
  `damage_per_level` x level per damage type minus protection, G1 by target type, G2 by `C_CanNpcCollideWithSpell`.
  Spells kill unless the scripts say `COLL_DONTKILL`. Other spells reach their target through `PERC_ASSESSMAGIC`.
- **The VR hero's bow and crossbow** (`VRBowShooter`): the trigger draws a bow and shoots on release, a crossbow shoots
  on the press and reloads 1.5 s. `FightService.ShootByHand` takes the munition and flies the arrow where the hand
  points (`ArrowFlightDomain.LaunchFree`, slower when not fully drawn); it hits the first NPC it passes, no hit chance.
- `AI_FinishingMove` (scripts' `ZS_Attack_End` against a hostile enemy who is down) walks there, turns and stabs
  (`FinishingMove`, `T_1HSFINISH`/`T_2HSFINISH`); the enemy dies as the stab starts, the hero as well.

## A death

`NpcDeathDomain.Die`, for every death (a hit, or a script lowering the hit points to 0):
1. Dead body state, 0 hit points, the weapon put away, the dying animation and the scream.
   `GlobalEventDispatcher.NpcDied`.
2. One frame later `ZS_Dead` and its loop run with the killer as other (`VmService.CallAsNpc`): experience for the
   hero's own kills, loot, quest variables. The hero's instance is copied to his VOB.
3. The NPCs around perceive `PERC_ASSESSMURDER`.

The hero dies the same way, and the game is over (`NpcDeathDomain.EndGame`): he stays locked until a game is loaded,
`GameStateService.InGameAndAlive` is false (the menu offers no saving or continuing), and
`GlobalEventDispatcher.HeroDied` follows `NpcDied`. In VR the view falls and darkens (`VrKnockoutView`) and the menu
opens after 5 seconds (`VRPlayer`).

Each hit, knockout and death asks all NPCs of the world once whether they saw it
(`NpcAiService.SendPerceptionToWitnesses`).

## Falling

`AiHandler` calls `NpcFallService.Tick` every frame for each awake NPC that isn't dead:
- Only a body with physics (walking, running) falls. Its rigidbody's vertical speed below -3 m/s means in the air,
  above -0.5 m/s landed again. The height counts from the last ground it stood on - or from where it was when its
  body got physics again.
- Landing costs the engine's fall damage (OpenGothic `damageFall`): the guild's `FALLDOWN_DAMAGE` per meter beyond
  its `FALLDOWN_HEIGHT`, less the fall protection; a negative protection makes it immune. The hit points go down
  through `NpcService.SetAttribute`, so a deadly fall is a death like above.
- Edge stop (`AnimationSystem.IsDeepDropAhead`, like OpenGothic `MoveAlgo::tryMove`): a moving NPC casts one ray down
  0.5 m ahead (Default layer). A drop of 3/4 of its guild's `FALLDOWN_HEIGHT` or more zeroes the horizontal move, so
  chasing NPCs don't run off cliffs; lower steps down are fine.

## Differences from the original

- A swing hits by reach and angle, not by the weapon's bone touching the target (`DEF_HIT_LIMB`).
- A knocked out or killed NPC puts the weapon away at once; the original drops it to the ground and the NPC picks it
  up again when it wakes up. Our NPCs can't pick items up yet.
- Spells show only the particles of their visual effects - no lights, sounds or invest stages. Area spells (fire rain,
  storms) don't hurt anybody yet.
