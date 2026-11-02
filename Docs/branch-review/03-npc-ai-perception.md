# 3. NPC AI: perceptions, targeting, routines

[← Overview](README.md)

<!-- toc -->
## Contents

- [What changed](#what-changed)
  - [Perceptions like the engine](#perceptions-like-the-engine)
  - [Targeting](#targeting)
  - [Routines](#routines)
  - [Added 2026-10-10 – 2026-10-11](#added-2026-10-10--2026-10-11)
  - [Added 2026-10-13](#added-2026-10-13)
  - [Added 2026-10-16](#added-2026-10-16)
  - [Added 2026-10-17](#added-2026-10-17)
  - [Added 2026-10-24](#added-2026-10-24)
  - [Added 2026-10-25 (afternoon)](#added-2026-10-25-afternoon)
  - [Added 2026-10-26](#added-2026-10-26)
- [Key commits](#key-commits)
- [Main files](#main-files)
- [Config flags](#config-flags)
- [Review notes](#review-notes)
- [How to test](#how-to-test)
<!-- /toc -->

## What changed

### Perceptions like the engine

- `Npc_SendPassivePerc` reaches the NPCs around the sender (was: executed on the sender itself). Fixes guard
  warnings (`ASSESSWARN`), `NPCCOMMAND` and others.
- Active perceptions (enemies, bodies) work without the hero nearby — NPCs fight monsters on their own.
- New: `ASSESSBODY` (monsters eat corpses), `ASSESSMURDER`/`ASSESSDEFEAT` to witnesses, `OBSERVESUSPECT` (hero
  sneaking), `ASSESSENTERROOM` (the room is the room floor under the feet, like OpenGothic's ground ray - walls
  and roofs don't count), `ASSESSUSEMOB`, footstep `ASSESSQUIETSOUND`. `ZS_*` perception functions are
  started as states (with loop/end), like the engine.
- NPCs react to the hero readying a weapon or rune in VR, and to casting (`ASSESSCASTER`).

### Targeting

- `Npc_GetNextTarget`, target switching for non-hero attackers, party members never auto-targeted, summons never
  target their master, `AIV_EnemyOverride` road bandits don't pick the hero (G2).
- `CheckForArmedNpcThreat` (NPC→NPC armed threat) no longer restarts `ZS_AssessFighter` on the hero every tick.

### Routines

- Culled NPCs move to their routine waypoint on schedule change; route resume from the nearest waypoint;
  free point (FP) locking so guards keep their posts; perceptions reset on routine change.
- Important dialogs checked in `nr` order like the engine; loopless states end like the engine.
- `AI_UseItem` (NPCs drink potions after fights), `AI_Flee`, `Npc_GetLookAtTarget`.

### Added 2026-10-10 – 2026-10-11

- **Senses like the engine:** sight in the full senses range but only in front (FOV), hearing only while the target
  walks/runs and within half the range, smell 3 m - before NPCs sensed the hero all around.
- **Orc threats:** orcs turn, play `T_WARN` and shout `$DIEMORTALENEMY` before chasing; an NPC without `$RUNCOWARD` (orc
  voice) says `$GETOUTOFHERE` when giving up.
- **Chases end:** `ZS_ProclaimAndPunish` gives up after `HAI_TIME_PURSUIT` mid-chase, followed by a short cooldown;
  fighting NPCs don't give up next to a moving VR hero (sticky run 1.5 s, attack loop time reset within 4 m).
- **Guild table:** orcs of all castes are friends and every monster guild is friendly to itself (only the human block
  came from the scripts); comrades warned by an orc attack the hero, not the orc; orcs tolerate the Ulu-Mulu in the
  hero's hand or at the belt.
- **Sleeping resets NPCs:** after `PC_Sleep`/time skip every living NPC starts over like after culling (routine waypoint
  of the new time, empty queue, no target, weapon away).
- **G2 wisp amulet:** the wisp follows the hero and flies to items - new externals `AI_GotoItem`, `Npc_GetHeightToItem`,
  `Wld_RemoveNpc`.
- `ZS_Dead_Loop` runs once after `ZS_Dead` - G2 counts killed dragons there (undead dragon's video, teleport and
  `UndeadDragonIsDead`).
- `AI_ContinueRoutine` on an NPC without a routine goes back to its start state instead of throwing.

### Added 2026-10-13

- **Guides wait again:** guides and followers (`ZS_GuidePC`/`ZS_FollowPC`, G2 `ZS_Guide_Player`/`ZS_Follow_Player`) sense
  the hero all around their senses range like the engine's smell. With the quiet senses (smell 3 m) a guide running
  ahead with its back to the hero never got `PERC_ASSESSPLAYER` (`B_GuidePC_AssessPlayer`/`B_AssessGuidePlayer` stop and
  turn to the hero) - Baal Parvez, Lares and Jack Alligator never waited.
- **Guides fight monsters on the way:** an NPC's own attitude (`Npc_SetAttitude`) only counts towards the hero like the
  engine (OpenGothic `personAttitude`) - between NPCs the guild table decides. Baal Parvez, friendly to the hero as a
  guide, was friendly to every lurker and scavenger. Guides keep the engine's senses for enemies too, and a hit
  interrupts an NPC's walking (the engine's stumble) so `ZS_ReactToDamage`'s `AI_StartState` doesn't wait behind a long
  `AI_GotoWP`.

### Added 2026-10-16

- **`Hlp_GetNpc(self)`:** like the engine, `Hlp_GetNpc` resolves instance variables (`SELF`, `OTHER`, ...) to the NPC
  they point to, not only NPC instance definitions. Mroczne Tajemnice's `ZS_DragonEscape_Loop` calls
  `B_KillNpc(self)` and then `Wld_InsertNpc(NON_2013_ALGIRION)`: the null NPC threw in `Npc_ChangeAttribute`, the
  function stopped before the insert and the dragon only vanished. `Npc_ChangeAttribute` without an NPC now warns.

### Added 2026-10-17

- **`AI_StartState(..., 1, ...)` ends the old state:** `ExtAiStartState` zeroed the current state's `_End` and cleared
  the queue right away, so the `_End` never ran. A dialog ending in a fight (`AI_StartState(self, ZS_Attack, 1, "")`)
  skipped `ZS_Talk_End` - the hero kept the dialog's `AIV_INVINCIBLE`, nobody talked to him and attacked NPCs didn't
  fight back. The `_End` is passed to `StartState` and called when the queue reaches it (`AiHandler.CallStateEnd`,
  with the old state's other/victim); a talking NPC (dialog lines queued) keeps its queue so the lines come first,
  like the engine's AI message - the fight or the dragon's change follow. Elsewhere the queue is still cleared at once.
- **Knockouts are the engine's state change** (`0a7f3944`): `FightService` starts `ZS_Unconscious` through
  `ExtAiStartState(..., isScriptCall: false)` - no old `_End` (`ZS_ProclaimAndPunish_End` queued weapon draws and new
  states on the lying NPC, `ZS_Unconscious` never started), no waiting for dialog lines. A knocked out NPC gets no
  perception but `PERC_ASSESSMAGIC` until he stands up (what `ZS_Unconscious` enables), also in the frames before the
  state starts.
- **Beaten NPCs** (`69bb0d3a`): `AI_TurnAway` was a stub - it turns the back to the other NPC now. A human the hero
  beat before (`AIV_WASDEFEATEDBYSC`) is a coward towards him: `ZS_AssessEnemy` -> `ZS_ProclaimAndPunish` ->
  `ZS_TurnAway` ("I didn't see anything"). Attacked by the hero himself he drew, put away and looked away in a loop -
  he fights back now (`EnableDefeatedNpcFightsBack`, opinion). As a witness the enemy perception (no view angle, like
  `Npc_CanSeeNpcFreeLOS`) restarted `ZS_AssessEnemy` and the 20 s of `ZS_TurnAway` never ran out; in that state the
  hero isn't an enemy anymore, and when it runs out the hostile temporary attitude goes back to the permanent one like
  `B_ResetTempAttitude` - nothing in the scripts resets it there.
- **A state's `_End` runs once** (`fe43a81b`): `ZS_Attack_End` says `$NeverTryThatAgain` and calls
  `AI_StartState(..., 1, ...)` - that took the running `ZS_Attack_End` as the old state's `_End` and ran it again. A
  state in its End/AfterEnd phase passes no `_End` on.

### Added 2026-10-24

- **Whirl around** (`bda6d4e1`, `EnableAiWhirlAround`): `AI_WhirlAround` was a plain turn. `WhirlAround` plays
  Humans.mds `t_surprise_CW`/`t_surprise_CCW` by the turn's side (then `s_surprise`) and turns the body along with the
  animation (SmoothStep over its duration); monsters without it just turn. G1 `ZS_AssessFighter` -> `B_WhirlAround` uses
  it when the hero draws behind an NPC. `B_SelectWeapon` -> `B_EquipAndDrawBestMeleeWeapon` calls `B_FullStop`
  (`Npc_ClearAIQueue`) a second time and dropped the queued whirl - only NPCs with a ranged weapon (Lukis) whirled.
  `ExtNpcClearAiQueue` keeps a queued whirl at the front (whatever the script queues after the clear follows it) and
  lets a running one finish.
- **Sneaking behind guards** (`1fe4496d`, `EnableSneakBehindGuards`, opinion): G1 `ZS_Guard_Loop` /
  `ZS_GuardPalisade_Loop` call `B_SmartTurnToNpc(self, hero)` within 8 m by distance alone (`AI_TurnToNpc` when he
  can't see the hero) - in the original game too. `ExtAiTurnToNpc` turns such a guard back to his FP (`AlignToFp`)
  instead when the hero sneaks (`BS_SNEAK`) and the guard can't see him (FOV). OpenGothic: smell senses all around in
  the senses range regardless of sneaking, hearing only through noise perceptions - the sneaking hero's steps send no
  `PERC_ASSESSQUIETSOUND` (ours: `EnableQuietSenses`, smell 3 m).

### Added 2026-10-25 (afternoon)

- **Proclaim from afar** (`7d91ccdb`, `EnableProclaimFromAfar`, opinion): `ZS_ProclaimAndPunish_Loop` only runs once
  `AI_GotoNpc` reached the hero - a hero running away right after his attack was chased forever, the loop's 5 m and
  `HAI_TIME_PURSUIT` never had a say. `AiHandler.CheckProclaimChase`: within 12 m the queue is cleared and the state ends
  (proclaim, attack - Lukis draws his crossbow at once); after `HAI_TIME_PURSUIT` the loop decides.
- **No freeze after fights** (`4ff1afc4`, `EnableUseItemGlobalItem`): G1 `ZS_HealSelf_Loop` eats food found by
  `Npc_GetInvItemBySlot` with `AI_UseItem(self, item)` - the external got the symbol `ITEM`, not the item in it;
  `UseItemToState` had nothing to play and waited forever (Lukis stood with his crossbow half put away until talked to).
  `ResolveItemId` takes `GlobalItem`; a use with nothing to play ends at once.

### Added 2026-10-26

- **Sneaking needs the talent** (`f97f2a73`): crouching is `BS_SNEAK` only with `NPC_TALENT_SNEAK` (OpenGothic
  `Npc::canSneak`).

## Key commits

`b459f4f7` passive perc broadcast, ASSESSBODY, MURDER/DEFEAT · `00f95e72` hero perceptions (sneak, rooms, usemob) ·
`ed809d8a`/`dc4788dc` reactions to weapon/rune/casting · `59ba4bb6`/`c272029e`/`05571acd`/`05500b62` target
switching · `1f73a5a6` EnemyOverride · `1e18996d` culled NPC routines · `d7d99a77`/`7978e766` route resume ·
`5efcb33a`/`bfb28b3d` FP locks · `7d95812a` important dialog order · `a063883c` AI_UseItem

2026-10-10/11: `6c4452d7` senses · `b6085f3c` orc threats · `c42cfdac`/`fab42830` chase give-up ·
`868a1960`/`ad2b0947`/`7be610d8` orc guilds, warnings, Ulu-Mulu · `26c60bf1` sleep reset · `9f5b92a1` wisp · `0da3529f`
ZS_Dead_Loop · `247ac69d` routine-less NPCs

2026-10-13: `d522e413` guides sense the hero · `857bd827` NPC attitudes towards NPCs, guides fight

2026-10-16: `63eddacb` Hlp_GetNpc resolves instance variables

2026-10-17: `18de3228` AI_StartState runs the old state's _End after a dialog's lines · `0a7f3944` knocked out NPCs
stay down · `fe43a81b` a state's _End runs once

2026-10-18: `69bb0d3a` AI_TurnAway, beaten NPCs fight back, looking away ends

2026-10-24: `bda6d4e1` whirl around · `1fe4496d` sneaking behind guards

2026-10-25: `7d91ccdb` proclaim from afar · `4ff1afc4` no freeze after fights

## Main files

`Services/Npc/NpcAiService.cs` (+904) · `Services/Npc/HeroPerceptionService.cs` (new) ·
`Services/World/RoomService.cs` (new) · `Adapters/Npc/AiHandler.cs` · `Domain/Vm/VmExternalDomain.cs`

## Config flags

`EnablePassivePercBroadcast`, `EnableNpcPerceptionsWithoutHero`, `EnableAssessBodyPerception`,
`EnableMurderDefeatPerceptions`, `EnableHeroSneakBodyState`, `EnableUseMobPerception`,
`EnableFootstepQuietSound`, `EnableEnterRoomPerception`, `EnableFloorRoomLookup`, `EnableEnemyOverrideNextTarget`,
`EnableRoutinePerceptionReset`, `EnableCasterPerception`, `EnableNpcTargetIsEnemy`, `EnableImportantInfoOrder`,
`EnableLooplessStatesEnd`, `EnableDetectNpcSkipsDead`.

2026-10-10/11: `EnableQuietSenses`, `EnablePursuitWarn`, `EnableChaseGiveUpCooldown`, `EnableStickyHeroRun`,
`EnableTimeSkipResetsNpcs`, `EnableDeadStateLoop`

2026-10-13: `EnableEscortSensesHero`, `EnablePersonalAttitudeOnlyForHero`, `EnableHitStopsWalking`

2026-10-16: `EnableHlpGetNpcVariables`

2026-10-17: `EnableQueuedStateEnd`, `EnableDownedNpcPerceptions`

2026-10-18: `EnableAiTurnAway`, `EnableDefeatedNpcFightsBack`, `EnableTurnAwayIgnoresEnemyBehind`

2026-10-24: `EnableAiWhirlAround`, `EnableSneakBehindGuards`

2026-10-25: `EnableProclaimFromAfar`, `EnableUseItemGlobalItem`

## Review notes

- ✅ Each perception is matched to OpenGothic (`worldobjects.cpp`, `npc.cpp`) and the original scripts; deviations
  are commented. New externals are only registered when the scripts declare them.
- ⚠️ **Performance:** several perceptions scan the full NPC cache per NPC per perception tick (enemy, body,
  armed threat, next target) — O(n²) per second. Fine in the G2 city in testing; a spatial query would scale better.
- ⚠️ `NpcAiService` is now ~1,900 lines. Perception broadcasting and targeting could move to their own services.
- ⚠️ Symbol lookups by name (`GetSymbolByName("AIV_ENEMYOVERRIDE")`) happen in hot paths; cache them.
- ℹ️ Theft (`ASSESSTHEFT`) and item-drop quiet sounds are not done on purpose.

## How to test

- G2 city: kill someone in front of witnesses → "Murderer!".
- G1 Old Camp: enter a hut (log `[Rooms] Hero: '' -> '…'`), crouch in front of a guard, pick a chest owned by
  `GIL_GRD`.
- G1: guards fight a wolf with the hero far away.
- Stand still behind an orc: it doesn't see you. Walk: it hears you within half its range.
- Attack an orc in a group: its comrades attack you. Hold the Ulu-Mulu: orcs tolerate you.
- Sleep in a bed: NPCs stand at their new routine spots. G2: wear the wisp amulet.

- G1/MT: Baal Parvez to the swamp camp, G2: Jack Alligator to the pirate camp / Lares - fall behind, they stop and wait.

- G1/MT: Baal Parvez attacks a lurker that comes within ~10 m by himself ("$DIEMONSTER").
- Mroczne Tajemnice ancient temple: finish the Black Dragon's talk - it teleports away and Algirion appears.
- A dialog that ends in a fight (G1 MT Rost): the NPC finishes its lines, then attacks (log `ZS_TALK_END (state ended
  by AI_StartState)`); afterwards other NPCs talk to you and fight back.
- Knock out a gate guard: he lies until he stands up (`Start new routine >ZS_UNCONSCIOUS<`), no reaction to drawn
  weapons or fights meanwhile. Get beaten by a guard: "$NeverTryThatAgain" once, then he searches you.
