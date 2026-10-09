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

## Key commits

`75bd89fd`/`b773c4c0`/`a1a8f2a2`/`e37c8952` movers · `9636179d` trigger system · `cd614258`/`f200e883` wheels ·
`a696f99b` keys · `f44ca0ac` MOBSI · `74302081` G2 beds · `6bd758e0` MT beds · `3edf3460` dig spots ·
`00f95e72` rooms · `2fb20421`/`8e6b8f44`/`be5105d6` seated NPCs · `05be2e1a` INIT_ triggers · `8764fe40` VOB tree

## Main files

`Adapters/Vob/MoverAdapter.cs`, `TriggerListHandler.cs`, `CodeMasterHandler.cs`, `MessageFilterHandler.cs`,
`MoverControllerHandler.cs`, `Trigger*Handler.cs` (all new) · `Services/Vobs/VobService.cs` (+410) ·
`Domain/Vobs/VobInitializerDomain.cs` · `Services/World/RoomService.cs` · VR: `VRWheelInteraction.cs`,
`VRDigSpot.cs`, `VRPlayerService.cs` (`HandleMobGrab`, MOBSI)

## Config flags

`MoverSpeedMultiplier`, `EnableMobsiDialogs`, `EnableMobSeatFix`, `EnableNoTurnWhileUsingMob`, `EnableDigSpots`,
`DigSpotsRequireTool`, `EnableChildVobLoaders`, `EnableEnterRoomPerception`, `EnableEmptyVobContainerChildren`,
`EnableWorldSpaceChildVobs`, `DebugTraceVobVisual` (debug: logs a VOB's loader creation, init and destruction).

## Review notes

- ✅ Trigger handlers are small, one per VOB type — fits the adapter pattern.
- ⚠️ `VobService` keeps growing (movers, triggers, mob owners diagnostics). The trigger dispatch could be its own
  service.
- ℹ️ The G1 OrcGraveyard gate (WHEEL) from Chapter 3 was the original driver for wheels; verify it end to end.

## How to test

- G1: Old Camp main gate winch; Chapter 3 graveyard gate. G2: open a gate via lever.
- Pray at a shrine, sleep in a bed (G1 MT and G2), dig an X mark in G2.
