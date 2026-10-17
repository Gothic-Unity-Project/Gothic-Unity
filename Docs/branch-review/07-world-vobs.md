# 7. World: movers, triggers, MOBSI, rooms

[← Overview](README.md)

## What changed

- **Movers** (`zCMover`): keyframe movement with Open/Close/Toggle, speed fix, chained triggers, all same-named
  instances triggered, Gothic `//` target prefixes handled.
- **Trigger system:** `zCTrigger`, `zCTriggerList`, `zCCodeMaster`, `zCMessageFilter`, `zCMoverController`,
  untouch and world-start triggers; world `INIT_` triggers with lazy DI and a cycle cap.
- **Gates and winches:** `VRWheelInteraction` for WHEEL mobs; mob `ConditionFunction` gates the trigger.
- **Doors and containers:** keys and lock picks unlock; locked doors block the player (physics layer matrix).
- **MOBSI dialogs:** shrines, alchemy/rune tables, book stands open their dialogs; G2 beds are beds (sleep dialog);
  G1 beds/shrines work in mods whose hero isn't `PC_HERO` (Mroczne Tajemnice: `PC_Rockefeller`).
- **G2 treasure X marks:** visible and dug up with a weapon.
- **Portal rooms:** BSP sectors from the world's room materials (like OpenGothic), `Wld_AssignRoomToGuild`,
  portal guild externals for G1 and G2.
- Seated NPCs sit correctly, talk seated or stand up; mob seat fix.
- **VOB tree fixes (all worlds):** an unnamed `zCVob` without a visual only groups its children - as a lazy loader
  without bounds it never loaded and nothing below it appeared (~8000 VOBs in a big mod world); its children are now
  VOBs of their own. Child VOBs sit at their own position (ZEN positions are world positions) instead of their
  parent's origin. Movers and children of visual-less VOBs get precached bounds (static cache v6).

**Added 2026-10-10 – 2026-10-11**

- **Logic VOBs below other VOBs** (triggers, trigger lists, code masters, movers, message filters) are registered and
  get their children; children of VOBs we don't build (cutscene cameras, fog zones, …) and of decal/particle VOBs with
  those visuals off are created; a reload replaces destroyed entries. Triggers send their own name (code masters count
  slaves by name).
- **Movers like the engine:** every keyframe with the engine's timing per segment (translation cm/ms, rotation as
  rotations/s, low speeds scaled up); `zCMessageFilter` Enable/Disable/Toggle and `startEnabled`; keyframes relative to
  the VOB; a mover below another mover moves along with it (Jharkendar portal petals).
- **`zCTriggerList` ALL:** delays add up from target to target (OpenGothic), the targets fire in list order.
- **`zCCodeMaster` like the engine:** every press counts, an ordered code is judged after as many presses as slaves, a
  wrong one fires the failure target (Irdorath touchplate puzzles).
- **Mobs:** the condition function runs with `self` = player ("missing something" lines are heard); a mob with a trigger
  target and `useWithItem` needs the item (Jharkendar portal ring).
- **Damage targets:** projectiles trigger logic VOBs with `reactToOnDamage`, auto-aim at them, a green X marks invisible
  ones; treasure X marks are visible in builds (URP Unlit placeholder material).
- Result: **G2 Irdorath is completable** (bridges, traps, gates, wheels, pillars, main door, last door).

**Added 2026-10-11 – 2026-10-13**

- **Smithing:** crafting mobs move their `useWithItem` like the engine's human animation events (`HUMANS.MDS`
  `T_<scheme>_S0_2_S1`/`_S1_2_S0`: `ItemInsert`, `ItemExchange`, `ItemRemove`, `ItemDestroy`) - the forge turns raw
  steel into hot steel, the anvil takes the hot steel before `SMITHWEAPON_S1` counts it. Before the forge did nothing
  and the anvil only said "With what?".
- **NPCs work at their mobs:** at mobs without FRONT/BACK animations (saw, repair spot, roast spit) NPCs play the plain
  ones like OpenGothic - G2's pirates stood next to the saw and spit (`S_BAUMSAEGE_BACK_S1` not found).
- **Bookstands show their book:** the page a mob shows with `Doc_Show` (`Use_Bookstand*_S1`) appears above it like a
  held document; touch again or walk away (3 m) to close.
- **Collisions like the engine:** only polygons of materials without `noCollDet` collide - trees block at the trunk,
  not the leaves, bushes on the level mesh can be walked through.
- **Fireplaces keep their children:** `CreateFire` returned null without a fire tree and the child VOBs were dropped
  (Mroczne Tajemnice's smithy furnace on the smugglers' beach).

**Added 2026-10-13**

- **Fires burn where they are:** fire trees (`oCMobFire` vobTree) place their VOBs relative to the fire's slot. The
  cached tree zeroed only its roots, so the smoke under a fire's light kept the tree's absolute position (every
  `FIRETREE_MEDIUM` smoke at (-36, -5, 24) m - a smoke column at the Old Camp's gate), and particle effects took the
  tree position as a world position (torch flames at the world's origin).
- **Fires by day and night:** fireplaces and torches (`oCMobFire`) follow the scripts' mob routine of their scheme (G1
  `STARTUP`: `Wld_SetMobRoutine` `FIREPLACE` lit 22:00-05:00 - stored but never applied before). Fires indoors (light
  mapped: houses, mines) always burn; touching one in VR lights it or puts it out until the routine switches; further
  than 60 m the effects are off. A fire that is out gives no light, smoke nor sound.

**Added 2026-10-16**

- **Level change arrival:** a `oCTriggerChangeLevel`'s start VOB is looked up as a free point, a waypoint or any
  named VOB. When it's missing (Mroczne Tajemnice's temple sends you to `TEMPLE_TO_CAVE`, which the abandoned mine
  doesn't have) the hero stands at the way point next to the trigger back to the old world - outside its box, height
  weighted triple so it's the same floor - instead of a null reference and a stalled loading screen. The engine would
  use START, which in that mine is over the void.

**Added 2026-10-17**

- **Animation combinations** (`aniComb`): a combination names numbered variants of one animation (the engine picks
  one by the angle to a target). The ore vein's S1 (`t_Ore_S0_2_S1`, `s_Ore_S1`, `t_Ore_S1_2_S0` -> `c_Ore_*_1..3`)
  exists only like this - miners stood frozen in S0. `AnimationService.GetTrack` plays the middle variant with the
  combination's layer, next and flags (like an alias). ZenKit doesn't expose a combination's events; `s_Ore_S1`'s
  pickaxe hit from `HUMANS.MDS` (`*eventSFX`/`*eventPFX` `pickore` at frame 25, `ZS_RIGHTHAND`) is added in code.
- **Occupied mobs:** `GetFreeInteractableWithin10M` took the nearest mob of the scheme without looking at its users -
  two miners spawned next to each other hacked at the same vein. A mob whose slots (`ZS_*`) are all used by other NPCs
  (seated, or walking there with `UseMob`) is skipped, the nearest free slot is taken; the mob is reserved as soon as
  it's chosen and released when no slot is left.
- **World points per world:** free points (START, `FP_*`) and waypoints were never cleared between worlds
  (`GameStateService.Reset` is never called) and registered with `TryAdd` - the first world's won. A new game after
  loading an Old Mine save started at the mine's START, which lies in the Old Camp in the main world. They're cleared
  in `BootstrapService.LoadWorld` before every world; a new game also clears a spawn position/level change left from
  a load or portal. `[Spawn]` logs tell where the hero was placed and why.
- **NPCs wait for the world's VOBs:** VOBs are created in the background after the world is entered
  (`InitVobCoroutine`); an NPC on a scaffold snapped to the ground below or fell through it. It stays kinematic until
  the VOB creation queue is empty (max 10 s), then snaps to the ground.
- **Dig spots are treasure X marks only** (`f59f1737`): every mob used with a melee tool counted (MT's anvil with the
  sledgehammer, G2's `Goldhacken`/`Stomp`) - their mob dialog was skipped, `SMITHERY_S1` left the hero's
  `AIV_INVINCIBLE` set (all NPCs ignored him) and one anvil took the renderers of three anvils nested below it: its
  bounds covered the whole Old Camp. Dig spots are `B_SCGetTreasure` with the VOB's own renderers now.
- **Mob dialogs next to their own mob** (`598e1bd0`): the dialog anchor had the same nested renderers and landed
  hundreds of meters away - `main`'s range check didn't start the smithing dialog.

## Key commits

`75bd89fd`/`b773c4c0`/`a1a8f2a2`/`e37c8952` movers · `9636179d` trigger system · `cd614258`/`f200e883` wheels ·
`a696f99b` keys · `f44ca0ac` MOBSI · `74302081` G2 beds · `6bd758e0` MT beds · `3edf3460` dig spots ·
`00f95e72` rooms · `2fb20421`/`8e6b8f44`/`be5105d6` seated NPCs · `05be2e1a` INIT_ triggers · `8764fe40` VOB tree

2026-10-10/11: `136de852` sender names · `1f9e8c8d`/`80304460`/`a480f3dd`/`d8666bdb` child logic VOBs · `2ea79afb` mover
timing · `54fe5c8e` enable state · `1417b802` nested movers · `30ddbfd1` trigger list delays · `481eb8e8` code master ·
`358f0d92`/`5e175caa` mob condition and item · `e62a1a23`/`f93c37a9`/`03b61a16`/`4ee473e0` damage targets and X marks

2026-10-11/13: `d6f3fb6c` smithing items · `10a998e6` mob animations without position · `3108985a` bookstands · `7a7f20ab` material collision filter · `5ce0cbcd` fireplace children

2026-10-13: `ef9bcdd3` fire trees relative to the slot · `3442aebd` fire states (routine, indoors, toggle, distance)

2026-10-16: `e276d797` level change arrival fallback

2026-10-17: `39fbe01c` animation combinations (miners dig) · `63ce6bfa` occupied mobs · `f11dc5c6` world points
reset · `93d2466a` new game spawn reset · `90301646` NPCs wait for VOBs · `f59f1737` dig spots treasure only ·
`598e1bd0` mob dialog anchor

## Main files

`Adapters/Vob/MoverAdapter.cs`, `TriggerListHandler.cs`, `CodeMasterHandler.cs`, `MessageFilterHandler.cs`,
`MoverControllerHandler.cs`, `Trigger*Handler.cs` (all new) · `Services/Vobs/VobService.cs` (+410) ·
`Domain/Vobs/VobInitializerDomain.cs` · `Services/World/RoomService.cs` · VR: `VRWheelInteraction.cs`,
`VRDigSpot.cs`, `VRPlayerService.cs` (`HandleMobGrab`, MOBSI)

## Config flags

`MoverSpeedMultiplier`, `EnableMobsiDialogs`, `EnableMobSeatFix`, `EnableNoTurnWhileUsingMob`, `EnableDigSpots`,
`DigSpotsRequireTool`, `EnableChildVobLoaders`, `EnableEnterRoomPerception`, `EnableEmptyVobContainerChildren`,
`EnableWorldSpaceChildVobs`, `DebugTraceVobVisual` (debug: logs a VOB's loader creation, init and destruction).

2026-10-10/11: `EnableChildLogicVobs`, `EnableEngineMoverTiming`, `EnableTriggerEnableState`,
`EnableMoverRelativeKeyframes`, `EnableNestedMovers`, `EnableTriggerListSummedDelays`, `EnableEngineCodeMaster`,
`EnableMobRequiredItem`, `EnableDamageTargetAutoAim`, `EnableDamageTargetMarkers`

2026-10-11/13: `EnableMobItemEvents`, `EnableMobAnimationWithoutPosition`, `EnableMobDocuments`, `EnableMaterialCollisionFilter`, `EnableFireVobChildren`

2026-10-13: `EnableFireTreeRelativePositions`, `EnableFireStates`

2026-10-16: `EnableLevelChangeSpawnFallback`

2026-10-17: `EnableAnimationCombines`, `EnableMobOccupancy`, `EnableWorldPointsReset`, `EnableNewGameSpawnReset`,
`EnableNpcWaitsForWorldVobs`, `EnableDigSpotTreasureOnly`, `EnableMobDialogOwnBounds`

## Review notes

- ✅ Trigger handlers are small, one per VOB type — fits the adapter pattern.
- ⚠️ `VobService` keeps growing (movers, triggers, mob owners diagnostics). The trigger dispatch could be its own
  service.
- ℹ️ The G1 OrcGraveyard gate (WHEEL) from Chapter 3 was the original driver for wheels; verify it end to end.

## How to test

- G1: Old Camp main gate winch; Chapter 3 graveyard gate. G2: open a gate via lever.
- Pray at a shrine, sleep in a bed (G1 MT and G2), dig an X mark in G2.
- G2 Irdorath end to end: shoot the bridge targets, gate levers (bottom rooms, then top rooms after the wheels turned
  again), touchplate rooms (left room order: SWITCH_02 → 03 → 01), four pillars, main door, Innos statue + last door
  (letter read first).
- G2 Jharkendar portal: without the ornament ring it refuses ("missing item"), with it all petals open.
- G2 Adanos temple door near the troll area.

- G2 Onar's farm: raw steel from Bennet, touch the forge (hot steel in the inventory), then the anvil - the
  smithing dialog with named weapons.
- G2 pirate beach: pirates saw wood and turn the roast spit.
- G2 monastery/city: touch a bookstand - the book appears above it; touch again or walk away to close.
- Walk through bushes and tree leaves, bump into trunks, walls and rocks.
- Mroczne Tajemnice: the smithy on the smugglers' beach has its furnace, anvil and grindstone.

- G1 Old Camp: every fireplace has its fire and smoke, no smoke column at the gate.

- G1: torches/fireplaces outside are out by day and burn from 22:00 (Marvin skip time); in mines they always burn; touching one toggles it; from the swamp camp no Old Camp smoke is visible.
- Mroczne Tajemnice: from the ancient temple back through the portal - you arrive next to the mine's portal; G1/G2
  level changes (Old Mine, Irdorath) still arrive at their spots.
- G1 Old Mine: miners dig with pickaxe sounds and ore splinters, each at its own vein; benches still seat two.
- Load an Old Mine save, then New game: you start next to Diego (log `[Spawn] World start 'START'`). Load a save in
  the Old Mine: miners stay on their scaffolds.
- Mroczne Tajemnice Old Camp: touch an anvil with the sledgehammer in the backpack - the smithing dialog opens next
  to it; fight with a sword near the smithy - no `[VRDigSpot] Hit` in the log.
