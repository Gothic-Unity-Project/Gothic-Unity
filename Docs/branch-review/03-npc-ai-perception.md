# 3. NPC AI: perceptions, targeting, routines

[← Overview](README.md)

## What changed

**Perceptions like the engine**
- `Npc_SendPassivePerc` reaches the NPCs around the sender (was: executed on the sender itself). Fixes guard
  warnings (`ASSESSWARN`), `NPCCOMMAND` and others.
- Active perceptions (enemies, bodies) work without the hero nearby — NPCs fight monsters on their own.
- New: `ASSESSBODY` (monsters eat corpses), `ASSESSMURDER`/`ASSESSDEFEAT` to witnesses, `OBSERVESUSPECT` (hero
  sneaking), `ASSESSENTERROOM`, `ASSESSUSEMOB`, footstep `ASSESSQUIETSOUND`. `ZS_*` perception functions are
  started as states (with loop/end), like the engine.
- NPCs react to the hero readying a weapon or rune in VR, and to casting (`ASSESSCASTER`).

**Targeting**
- `Npc_GetNextTarget`, target switching for non-hero attackers, party members never auto-targeted, summons never
  target their master, `AIV_EnemyOverride` road bandits don't pick the hero (G2).
- `CheckForArmedNpcThreat` (NPC→NPC armed threat) no longer restarts `ZS_AssessFighter` on the hero every tick.

**Routines**
- Culled NPCs move to their routine waypoint on schedule change; route resume from the nearest waypoint;
  free point (FP) locking so guards keep their posts; perceptions reset on routine change.
- Important dialogs checked in `nr` order like the engine; loopless states end like the engine.
- `AI_UseItem` (NPCs drink potions after fights), `AI_Flee`, `Npc_GetLookAtTarget`.

## Key commits

`b459f4f7` passive perc broadcast, ASSESSBODY, MURDER/DEFEAT · `00f95e72` hero perceptions (sneak, rooms, usemob) ·
`ed809d8a`/`dc4788dc` reactions to weapon/rune/casting · `59ba4bb6`/`c272029e`/`05571acd`/`05500b62` target
switching · `1f73a5a6` EnemyOverride · `1e18996d` culled NPC routines · `d7d99a77`/`7978e766` route resume ·
`5efcb33a`/`bfb28b3d` FP locks · `7d95812a` important dialog order · `a063883c` AI_UseItem

## Main files

`Services/Npc/NpcAiService.cs` (+904) · `Services/Npc/HeroPerceptionService.cs` (new) ·
`Services/World/RoomService.cs` (new) · `Adapters/Npc/AiHandler.cs` · `Domain/Vm/VmExternalDomain.cs`

## Config flags

`EnablePassivePercBroadcast`, `EnableNpcPerceptionsWithoutHero`, `EnableAssessBodyPerception`,
`EnableMurderDefeatPerceptions`, `EnableHeroSneakBodyState`, `EnableUseMobPerception`,
`EnableFootstepQuietSound`, `EnableEnterRoomPerception`, `EnableEnemyOverrideNextTarget`,
`EnableRoutinePerceptionReset`, `EnableCasterPerception`, `EnableNpcTargetIsEnemy`, `EnableImportantInfoOrder`,
`EnableLooplessStatesEnd`, `EnableDetectNpcSkipsDead`.

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
