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

**Added 2026-10-10 – 2026-10-11**

- **Strike out of the run:** NPCs and monsters running at the hero strike out of the run half of the time
  (`t_<weapon>AttackMove`: fist/1h humans, orcs, wolves, scavengers); the run animation keeps playing under the strike
  instead of freezing the legs.
- **NPC archers and crossbowmen** fire one shot per attack, the reload (`t_BowReload`/`t_CBowReload`) belongs to the
  same attack - before the single-frame shot animation chained into bursts.
- **Ranged give-up:** archers, crossbowmen and mages leave the attack when the hero is out of reach (24 m), like the
  script's own ranged abort.
- **Minimal damage:** a hit never does less than `NPC_MINIMAL_DAMAGE` unless the target is immune (a meatbug burned in a
  fire storm without losing HP).
- **Projectiles hit logic VOBs:** arrows, bolts and spell projectiles trigger `zCTrigger`/`oCTriggerScript`/movers with
  `reactToOnDamage` once per projectile, home on them within 12° (they're often invisible) and such targets get a green
  X where to shoot (see [World](07-world-vobs.md)).

**Added 2026-10-11 – 2026-10-13**

- **Knocked out NPCs fall down** like in the engine (`T_Stand_2_Wounded` → `S_Wounded`). G2's `ZS_Unconscious` only
  calls `Mdl_ApplyRandomAni` for a third of the tough guys (G1 always), everybody else stood or T-posed while down
  (Buster). `Mdl_ApplyRandomAni` no longer drops anybody (eating monsters got the fall too).
- **The hero's summons act for him:** their knockouts and kills count as his (`ZS_Unconscious` checks
  `C_NpcIsHero(other)` - Buster's admission duel goes on after the goblins beat him), bystanders blame the hero for
  their hits (guards go for him, the victim still fights the summon), and summons stop hitting a downed NPC like the
  scripts (`ZS_MM_Attack_Loop` ends on `C_NpcIsDown`).
- **Combo limit:** NPC/monster combos are as long as the attack animation's `DEF_WINDOW` pairs (OpenGothic
  `Pose::continueCombo`), then the attack plays to its end. `ZS_MM_Attack_Loop` queues `AI_Attack` every loop and each
  window cut the animation into a new attack - a lurker bit 4 times a second without end.
- **VR fists:** an empty hand with grip + trigger held is a fist - the hero is in fist mode (NPCs react like to raised
  fists) and a knuckle hitbox punches with a swing (hand speed ≥ 2 m/s, one punch per hand per 0.5 s). Damage is the
  engine's fist damage: in fist mode an equipped sword doesn't add its damage. A swing plays the fist attack's sound
  from `Humans.mds` (`S_FISTATTACK`) with a short vibration, a punch vibrates harder; `ShowNpcColliders` shows the
  punch volume as a sphere. The fist stays closed over highlighted NPCs and items, near (hand grabber) and far (force
  grabber, its own hover pose).
- **Frozen isn't down:** `ZS_MagicFreeze` sets `BS_UNCONSCIOUS` like a knockout; like the scripts' `C_NpcIsDown`
  only knocked out (not frozen) and dead NPCs count as down - monsters kept a frozen victim as target and the next hit
  no longer kills it at once.
- **Web crawler melee:** Mroczne Tajemnice's mage crawler casts from afar but uses its melee table (`FAI_MINECRAWLER`)
  and bites when its target is frozen or within twice its fist range.

**Added 2026-10-16**

- **The winner knows it won:** G1's `ZS_Unconscious` ends with `if (Npc_IsPlayer(self))
  other.aivar[AIV_HASDEFEATEDSC] = TRUE`; the hero runs no Daedalus states, so the NPC who knocked him out (or
  executed him) gets it set in C#. Dialogs after a lost duel check it - Mroczne Tajemnice's Sheer'Ghar
  (`Info_Sheer_After`) had nothing to say after beating the hero.

## Key commits

`ed02071c` NPC melee · `6ebb9d56` draw/sheath · `59e39ebe` combat context + `Npc_GetNextTarget` · `9fa477b4` knockout ·
`e5dfc6ce` chase give-up, finishing moves · `4bb7903c` drop weapon · `a4d92755` hitbox · `d575c3b3`/`bf4969b0` bows ·
`c0299809` crossbow · `1770b593`/`50d634e2` give up chase · `c1a4c1ad` AI_Attack draws first, two-handers ·
`fe5777c3`/`7fef08fd`/`035ca617` berserk · `887af70a` engine knockout rule · `84f47c83` hit at opt frame

2026-10-10/11: `68541a82`/`1f17b679` run attack · `5bbdace2` ranged one shot · `065fcc02` ranged give-up · `a24beeaa`
minimal damage · `e62a1a23`/`f93c37a9`/`03b61a16` projectile triggers, auto-aim, X marks

2026-10-11/13: `78de091f` knockout fall · `ec35b444` summons act for the hero · `45b6db3d` combo limit · `87665888` VR fists ·
`fef6b87f` fist sound/haptics · `f71d91ee` fist stays closed on hover

2026-10-15: `99ca5d27` frozen isn't down · `075f9e69` web crawler melee

2026-10-16: `b53d87e7` AIV_HASDEFEATEDSC for the NPC who beats the hero

## Main files

`Services/Npc/FightService.cs` (+878) · `Domain/Npc/Actions/AnimationActions/AttackPlayAni.cs` (+522) ·
`Attack.cs` · `AiDrawWeapon.cs` · `UndrawWeapon.cs` · `Adapters/Animations/AnimationSystem.cs` ·
VR: `VRBow.cs`, `VRCrossbow.cs`, `VRProjectile.cs`, `VRRangedService.cs`, `VRWeaponService.cs`

## Config flags

`EnableNpcCombatCombos`, `EnableNpcHitDetection`, `EnableEngineUnconsciousRule`, `EnableNpcSwingKeepsOnNpcHit`,
`EnableNpcHitAtOptimalFrame`, `EnableAiAttackDrawsWeapon`, `EnableFasterChaseGiveUp`, `EnableGuardsStopBerzerk`,
`EnableVrBows`, `EnableVrCrossbow`, `EnableCrossbowMasterOneHand`, `EnableHeroDropsWeaponsOnKnockout`,
`EnableNpcRangedCombat` (**off** in Production), cheats `EnableOneHitKill`/`EnableOneHitKnockout`.

2026-10-10/11: `EnableNpcRunAttack`, `EnableRangedGiveUp`, `EnableMinimalDamage`, `EnableDamageTargetAutoAim`,
`EnableDamageTargetMarkers`

2026-10-11/13: `EnableKnockoutFallAnimation`, `EnableSummonActsForMaster`, `EnableNpcComboLimit`, `EnableVrFists`

2026-10-15: `EnableFrozenIsNotDown`, `EnableSpellMonsterMelee`

2026-10-16: `EnableHeroDefeatedAivar`

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
- Let a wolf or an orc run at you: about every second attack lands out of the run.
- NPC archer: one shot, reload, next shot. Run 25+ m away: it stops and goes back.
- G1 Sleeper temple / G2 Irdorath: shoot the switches and bridge targets (green X) with a bow or a spell.

- G2: knock out Buster or anyone - he falls and lies (`S_Wounded`), stands up afterwards. Same in G1.
- G2: let summoned goblins beat Buster - the duel counts as won, the summons stop hitting him while he's down. In the
  city, let a summon hit a citizen: the guards go for the hero.
- G2: a lurker or wolf bites in a normal rhythm, not 4 times a second; humans chain at most their animation's combo.
- VR: grip + trigger on an empty hand and swing into an NPC (Buster's duel) - fist damage, he fights back with fists.
  Each swing whooshes and vibrates, a punch vibrates harder.

- Mroczne Tajemnice mine: a web crawler casts from afar; once you're frozen or close it runs up and bites (no freeze
  lock); a frozen NPC isn't killed by one hit.
- Mroczne Tajemnice: lose Sheer'Ghar's duel - after standing up he has the "honor" dialog (log: `AIV_HASDEFEATEDSC`).
