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

**Added 2026-10-18**

- **NPCs react to the hero's swings** (`b34c58ba`), like OpenGothic's `FightAlgo::fillQueue`: the VR swing (the held
  weapon passing the attack speed) is the hero's "prehit" for 0.4 s (`GlobalEventDispatcher.HeroMeleeSwing`). An NPC
  within 3 m, both facing each other (NPC 30 degrees, the hero's head 45), picks `FA_ENEMY_STORMPREHIT` (hero running) or
  `FA_ENEMY_PREHIT` - parry, jump back, strafe. These tables were never used before. The NPC picks a move once per
  `AI_Attack`, so the swing cuts short a wait/strafe and (`EnableNpcPrehitCancelsAttack`) the NPC's own swing before
  its hit frame plus its queued combo swings.
- **Parries block** (`b34c58ba`), like OpenGothic's `Npc::takeDamage`: a parry facing the hero (30 degrees) or a jump
  back (90 degrees) takes no damage; the whole animation counts (the engine: its `DEF_WINDOW`). Weapons clash with
  `CS_IAI_<material>_<material>` (G1 has only `CS_IAI_ME_ME`, else `CS_IAI`) and `CPFX_IAI_*` sparks; the hero's
  material comes from the swung weapon - the held weapon isn't equipped in VR. Fists don't clash.
- **Weapon trail at half opacity** (`692f18f1`): `WeaponTrailOpacity` scales color, alpha and Gothic/UI's alpha cutoff
  (0.5 - only the old alpha 0.53 passed it, a lower alpha alone made the trail vanish).
- ℹ️ Blocking an NPC's hit with the hero's own weapon in VR is still open (no good idea yet).
- **One swing, one hit** (`c4c2a8d0`, `EnableSingleHitPerNpcAttack`): the next attack with the same animation saw the
  previous track still blending out - its hit frame counted as reached and the action ended at once (a shadowbeast hit
  2-3 times per swing).
- **Combos like the engine** (`c4c2a8d0`, `EnableEngineNpcCombo`), OpenGothic `Pose::continueCombo` /
  `Sequence::isFinished`: an attack ends at its `DEF_HIT_END`; an attack asked for in the `DEF_WINDOW` continues the same
  animation from there to its next `DEF_OPT_FRAME` (a master's `s_1hAttack`: 4 parts, hits at 4/36/73/107). Before, the
  whole animation played and only the first part dealt damage; single-part attacks (shadowbeast, wolf) were restarted
  and hit twice. Left/right attacks are separate animations, unchanged.
- **Animated NPC bodies** (`ccb310bb`, `NpcBodyBounds`): hitbox (`EnableAnimatedNpcHitbox`) and personal space
  (`EnableBodyPersonalSpace`) follow the bones + skin instead of the root bone - a shadowbeast's BIP01 is at its hind
  legs, the hitbox on the rotated bone lay sideways over its hind half, the head went into the camera. Fight ranges stay
  Gothic's: `FIGHT_RANGE_BASE` is "BIP01 to bbox", it already counts the body.
- **NPC archers shoot real projectiles** (`df525fb4`, `EnableNpcPhysicalProjectiles`), like OpenGothic
  `World::shootBullet`: from the right hand, ballistic aim (30 m/s, gravity), the arrow/bolt flies and hits whatever it
  meets (`Projectile`, moved from `Gothic-VR` to Core; `ProjectileService` holds the ammo/shot logic shared with the VR
  bow and crossbow). The hit chance (`DamageCalculator`: G1 dexterity, G2 talent, by distance up to 45 m) aims a miss
  0.7-1.6 m past the target. Before, a shot hit instantly when the target was in front. Streak behind flying
  projectiles (`EnableProjectileTrail`), stopped once stuck in a body.
- **Ammo like the engine** (`36bfce77`): `Npc_HasRangedWeaponWithAmmo` counts the munition
  (`EnableRangedWeaponNeedsAmmo`) - `B_SelectWeapon` switches to melee when it's gone; `B_FillQuiver` refills only pure
  archers. G1's `ZS_Attack_Loop` gives a melee fight up beyond 10 m unless `AIV_LASTHITBYRANGEDWEAPON` - an archer out
  of ammo gets it set and keeps chasing (`EnableOutOfAmmoKeepsChasing`).
- **Archers get around cover** (`c1eb596c`): the shot's ballistic path is sphere-cast in 8 segments; blocked, the
  archer strafes to the side whose test position (1.5 m) has a free path, after 2 tries runs closer. The focus check
  needs a free head-to-head line, so the hero behind a pillar was "not in focus" - turn only, the archer froze
  (`EnableRangedFocusIgnoresCover`: facing him within 30 degrees counts). The strafe/run ends once the path is free,
  checked every 0.15 s (`EnableBlockedShotMoveInterrupt`); the run used the 20 m ranged reach as stop distance and
  stopped every frame.
- **Finishing moves and the hero's death** (`d0cb1968`): `AI_FinishingMove` (G1 `ZS_Attack_End`: hostile/intruder
  only) killed the target at once and did nothing to the hero. Like OpenGothic (`AiQueue` AI_FinishingMove,
  `Npc::finishingMove`, `canFinish`) it's queued (`EnableEngineFinishingMove`): walk to `NPC_ATTACK_FINISH_DISTANCE`,
  turn, a melee weapon drawn (our knockout sheathes it early), `t_1hSFinish`/`t_2hSFinish`; the target dies at the
  animation's hit frame - they have none, so at its sound event (`FIG_SwordFinal`, frame 35 of 139,
  `EnableFinishWithStabSound`, `3c23821d`; before: at the end, the scream long after the stab), `PERC_ASSESSMURDER`. The hero dies (`EnableHeroDeath`, ini option `heroDeath` - off = only
  knocked out); the knockout recovery doesn't revive him. VR (`EnableHeroFallCamera`, needs `EnableVrHeroBody`): the
  body plays `T_STAND_2_WOUNDEDB`/`S_WOUNDEDB`, `T_WOUNDEDB_2_STAND`, `S_DEADB`, lies on the floor (lowest bone) with
  its head under the eyes; the camera follows the animated head's height through `HVRCameraRig.CameraYOffset` (like a
  transformation) - never sideways, the HMD's rotation stays the player's. Dead, the game menu opens after 4 s. Not
  saved: a load starts standing, the offset is restored. The hero's death scream `SVM_<voice>_DEAD` (`b53715a9`,
  OpenGothic `emitSoundSVM`). Option "Death" in the Immersion menu (`3d80cb5f`). Lying or standing up isn't sneaking
  (`f048ff61`: the lowered camera counted as crouching, `VRHeroBody.IsHeroDown`).
- **VR finishing move** (`dad7ec3c`, `EnableVrStabFinish`, ini option `stabFinish` "Finishing" in the Immersion menu):
  any hit of the hero killed an unconscious NPC. Now only a stab does: the blade enters his hitbox (also already inside
  the padded box, `OnTriggerStay`) with >= 1.2 m/s along the blade (85% of its speed; the tip = the mesh's X axis away
  from the hand) - `FIG_SwordFinal` and a haptic pulse; then a 45 degree wrist twist around the blade (like the lock
  picks) while the blade rests (< 0.6 m/s, faster restarts the angle - a slash after the stab turned it) kills through
  `FightFinishingMove`. Pulled back 15 cm / 25 cm sideways, let go or 4 s: no finish. Fists can't finish off.
- **The dead stay dead** (`457d61cf` `EnableDeathZeroHitPoints`, `e90a5c1d` `EnableDeadSkipsRoutine`): a finished off
  NPC kept the knockout's 1 HP - `OnNpcDied` sets 0. Corpses stood up: `AiHandler.ReEnableNpc` (culled in again) moved
  them to their routine waypoint and started the routine with no animation (bind pose, legs in the ground), a queued
  `AI_ContinueRoutine` did the same, and `ClearState` (every state change) set `BS_STAND`. A dead NPC now lies where he
  died (`S_DEADB`; G1 crawler queen, harpy, meatbug only have `S_DEAD`). The dead hero (`VRHeroBody` death phase) stays
  `BS_DEAD` until a load and is locked again when the death menu closes - after Esc he walked, took hits, was knocked
  out and recovered with 1 HP.
- **Fitted ranged colliders** (`e5a55ef1`, `EnableRangedFittedColliders`, `ItemColliderFittingDomain`): bows and
  crossbows (`ItRw_Bow_*`, `ItRwCrossbow*`) are split into their mesh parts (welded by position; crossbow: stock,
  prod, trigger, string), each part sampled densely and fitted with an oriented box (PCA); a part is halved along its
  long axis while that leaves < 70 % of the volume (curved limbs, prods), up to 5 times; boxes thinner than 2.5 cm in two
  directions (strings) are dropped. Convex mesh colliders on the item itself (child objects keep their layer when a held
  item switches to VobItemNoWorldCollision). Crossbows are built by `BuildViaMmb`, bows via the cached collider path -
  both call it. Before: three fat cached capsules (13 cm) on a bow, axis aligned slices across a crossbow's prod.
- **Bows bend** (`478b8e32`, `EnableMorphByPosition`): `VRBow` blended the limbs with the bow's S_SHOOT morph, but every
  bow logged "morph frames=0" - the vertex mapping only exists if the mesh was built from the morph mesh first, and the
  bows had been built (welded, cached) without it. `MorphMeshCacheService.TryGetMorphDataForVertices` matches the Unity
  vertices to the morph mesh positions (0.1 mm).
- **Crossbow reload** (`ab73019b`, `EnableVrCrossbowManualReload`, ini `crossbowReload`): Gothic's crossbow morphs - the
  rest mesh is cocked (S_READY has no offsets), S_SHOOT barely moves anything, S_RELAX (the NPCs' t_CBowReload) moves the
  string 25-27 cm forward to the prod. `VRCrossbowString` (stays on the item) blends between the rest mesh and that
  S_RELAX frame; the nock = the vertex moving farthest. After a shot the free hand's trigger takes the string within
  12 cm and pulls it back along the stock (haptics every 10 %), at 97 % it locks (BowReload). A cocked crossbow with
  bolts shows one on the stock, its tail at the string - that bolt flies. Socketed (holster) = cocked. Off: automatic
  after 1.5 s with the shapes.
- **Dropped items don't block** (`0e7fa467`, `EnableDroppedItemColliderLayer`): `DropItemAtPosition`'s resting box was on
  the loader (Default) - missed bolts in the ground blocked the hero and the next bolts hit them (piled up in one spot).
  It's on a TransparentFX child now (world only; not Player/Hand/VobHitbox, not in the projectile mask).
- **Fight turns** (`443948e5`, `EnableFightTurnTolerance`): the turn move ends within 20 degrees (focus is 30) or after
  2 s - 1 degree on a circling scavenger kept Ratford turning forever.

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

2026-10-18: `b34c58ba` parry/jump back from the hero's swings, parries block · `692f18f1` weapon trail opacity ·
`c4c2a8d0` one hit per swing, engine combos · `ccb310bb` animated NPC body (hitbox, personal space)

2026-10-23: `d0cb1968` finishing moves, hero death, VR fall camera · `b53715a9` death scream · `3c23821d` kill with the
stab's sound · `dad7ec3c` VR finishing move (stab and twist)

2026-10-24: `0e7fa467` dropped items don't block · `457d61cf` dead = 0 HP · `e90a5c1d` the dead stay dead ·
`e5a55ef1` fitted ranged colliders · `478b8e32` bows bend · `ab73019b` crossbow reload

2026-10-22: `443948e5` fight turn tolerance · `df525fb4` NPC projectiles · `36bfce77` archers need ammo · `c1eb596c`
archers get around cover

## Main files

`Services/Npc/FightService.cs` (+878) · `Domain/Npc/Actions/AnimationActions/AttackPlayAni.cs` (+522) ·
`Attack.cs` · `AiDrawWeapon.cs` · `UndrawWeapon.cs` · `Adapters/Animations/AnimationSystem.cs` ·
`Adapters/Vob/Projectile.cs` · `Services/Npc/ProjectileService.cs` ·
VR: `VRBow.cs`, `VRCrossbow.cs`, `VRRangedService.cs`, `VRWeaponService.cs`

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

2026-10-18: `EnableNpcPrehitReaction`, `EnableNpcPrehitCancelsAttack`, `EnableNpcParadeBlocksHits`, `WeaponTrailOpacity`,
`EnableSingleHitPerNpcAttack`, `EnableEngineNpcCombo`, `EnableAnimatedNpcHitbox`, `EnableBodyPersonalSpace`

2026-10-22: `EnableFightTurnTolerance`, `EnableNpcPhysicalProjectiles`, `EnableProjectileTrail`,
`EnableRangedWeaponNeedsAmmo`, `EnableOutOfAmmoKeepsChasing`, `EnableRangedFocusIgnoresCover`,
`EnableBlockedShotMoveInterrupt`

2026-10-23: `EnableEngineFinishingMove`, `EnableHeroDeath`, `EnableHeroFallCamera`, `EnableFinishWithStabSound`, `EnableVrStabFinish`

2026-10-24: `EnableDroppedItemColliderLayer`, `EnableDeathZeroHitPoints`, `EnableDeadSkipsRoutine`,
`EnableRangedFittedColliders`, `EnableMorphByPosition`, `EnableVrCrossbowManualReload`

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
- G1 MT: Lukis (crossbow) - dodge his bolts, hide behind a pillar: he steps aside to a free shot, then runs closer. Let
  him shoot all bolts: he draws his sword and chases you.
- An NPC fighting a scavenger (Ratford): he attacks instead of turning around.
- Get knocked out by a guard: you fall, look around, stand up after ~10 s. By an enemy who finishes (orc, hostile
  guild): he walks up, stabs, you die and the menu opens. Option "Death" off: only knocked out.
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
- G1: fight a guard with a sword - he parries or jumps back from your swings, also mid-combo (log `[Prehit]`); a parry
  clashes with sound and sparks and does no damage (log `parries`). The swing trail is pale.
- G2: a shadowbeast - one swing takes your HP once; hit its head; it doesn't push its head into the camera. A sword
  fighter's full combo: every swing hurts (log `[Combo] ... continues s_1hAttack with part 2/2`).
