# 10. Save / load (WIP, off by default)

[← Overview](README.md)

<!-- toc -->
## Contents

- [What changed](#what-changed)
  - [Added 2026-10-10 – 2026-10-11](#added-2026-10-10--2026-10-11)
  - [Added 2026-10-11 – 2026-10-13](#added-2026-10-11--2026-10-13)
  - [Added 2026-10-17](#added-2026-10-17)
- [Key commits](#key-commits)
- [Main files](#main-files)
- [Config flags](#config-flags)
- [Review notes](#review-notes)
- [How to test](#how-to-test)
<!-- /toc -->

## What changed

- Two layers: ZenKit's native save plus a `UNITYSAVE.json` with hero and NPC state the native save doesn't carry
  (HP, positions, dead/unconscious, routines, protection, equipped items, model scale).
- NPC restore from merged snapshots with stable keys (GO name, spawn waypoint for monsters), FP routing restored,
  dead NPC animations, session carry-over, chests tracked; multi-world init and cold-start load.
- Daedalus NPC alias bindings (e.g. G2 `BAU_4300_ADDON_BRAGO = BDT_1014_BANDIT_L`) rebound after load by re-running
  `INIT_` with `Wld_InsertNpc` suppressed.
- G2 menus: New Game, save slots 16–20, foreign saves.

### Added 2026-10-10 – 2026-10-11

- **Movers and triggers are saved:** keyframe, open and disabled state of every changed mover plus trigger activation
  counts, per world in `UNITYSAVE.json`; restored on load and when coming back to a world (level change). Before, every
  mover was closed again after loading - softlocks in Irdorath, the Adanos temple, the Jharkendar portal.
  Movers/triggers take their entry when they're created, lazy loaded ones too.

### Added 2026-10-11 – 2026-10-13

- **Movers keep their pose:** a mover restored from a save takes its saved pose only in `Start`, after its child VOBs
  exist - G2's canyon teleport platform, its trigger and the focus stone were 9 m underground after loading.
- **Code masters keep their progress** (pressed slots, count) like the engine's CodeMaster keys; older saves count
  used-up slots and free them when all were used - G2's Irdorath bridge stayed up after loading.
- **Spawned NPCs are saved** per world (Marvin, a mid-game `Wld_InsertNpc`) with position, HP and name; summons aren't.
- **Every visited world keeps its NPCs** (position, HP, dead, spawned ones) in the save and when leaving it - also in a
  game started new.

### Added 2026-10-17

- **Items and chests** (`WorldItemSaveDomain`): a world is built from its world file on load (`WORLD.SAV` isn't read),
  so taken items lay there again. Each world's items and chests are captured against the world file's
  (`WorldItemBaseline`, key `INSTANCE|x|y|z` in cm, numbered duplicates): taken/eaten/moved items and changed stacks
  (`RemovedItems`), items lying elsewhere incl. NPC drops and arrows (`AddedItems`, position + rotation + amount),
  chests with other contents or lock (`Containers`). Saved as `UnityCustomSave.ItemsByWorld`, applied in `ChangeWorld`
  before the world's objects are created; worlds not visited since a load are carried forward. Item positions are
  synced from their bodies on save and before a level change.
- Eating from the ground (`VRMouth`) removes the item from the world's list (it only destroyed the GameObject), and
  the physics chest updates its contents when a hand takes an item out or puts one in (it listened to the socket's
  own grab only, so nothing ever left it).
- **NPC inventories** are part of the NPC snapshot and replace INIT's items when the NPC comes into view - a looted
  NPC stays looted. Inventories are cleared in the packed storage (`ClearPackedInventory`; `ExtNpcClearInventory` only
  clears the world file's item list - the hero's INIT items were doubled).
- **Saving puts held items into the backpack** (`IContextInteractionService.StoreHeldItemsInBackpack`).
- Items moved by hand are saved where they lie (`e80c5767`): every hand grab adds the item to the loose items, whose
  save-time sync took the loader's spot (the original) - both syncs take the item's body (Rigidbody) now. A
  `[SaveGame] ... moved` line logs each moved item.

## Key commits

`dbb7d674`/`2eb4388e` system + data classes · `c41cf7a0` gated by flag · `f9fef67a`/`6f8b5707`/`da641097`/`9d2f1897`/
`842d2b19` NPC restore · `fefd2f0d` alias rebind · `82677fcb` protection and equipment · `081e0c1b` INIT_ trigger crash ·
`8d8f5aba` G2 menus

2026-10-10/11: `868cc590` mover/trigger state · `d8666bdb` registry after an in-game reload

2026-10-11/13: `45c99bab` mover pose after children · `dbba8c19` code master state · `8ed9824a` spawned NPCs · `6e70e3fa` world revisit state

2026-10-17: `59717dc5` items and chests · `17b1667b` NPC inventories · `070b6aa1` held items into the backpack ·
`e80c5767` items moved by hand

## Main files

`Services/World/SaveGameService.cs` (+754) · `Services/World/UnityCustomSave.cs` (new) ·
`Domain/Npc/NpcInitializerDomain.cs` (+332)

## Config flags

`EnableSaveLoadSystem` (**off** in Production), `EnableSaveFeature`, `EnableLoadFeature`.

2026-10-10/11: `EnableLogicVobSaveState`

2026-10-11/13: `EnableSpawnedNpcSaves`, `EnableWorldRevisitState`

2026-10-17: `EnableWorldItemSaveState`, `EnableNpcInventorySaveState`, `EnableSaveStoresHeldItems` (all need
`EnableSaveLoadSystem`)

## Review notes

- ⚠️ **Off by default and still WIP.** Large surface (`SaveGameService` +754) with no tests. Decide whether it
  ships behind the flag or stays out of a merge.
- ⚠️ `UnityCustomSave.cs` holds five DTO classes in one file (conflicts with "one class per file" — acceptable for
  plain data, but worth a decision).
- ✅ Newer features were written with save/load in mind (model scale, equipment, protection are saved).

## How to test

- Enable `EnableSaveLoadSystem`, save in G1 Old Camp, quit, load: NPCs at posts, dead stay dead, inventory and
  equipment kept.
- Open a door/gate/portal, save, load: it stays open and its one-time switch stays used.
- Change the world and come back: opened movers stay open. New game after a loaded save: everything closed.

- G2: activate the canyon teleport, save, load - it still works.
- G2 Irdorath: hit one bridge target, save, load, hit the other - the bridge comes down.
- Spawn NPCs with Marvin, save, load - they're back where they were.
- G2: kill NPCs in Khorinis, go to the Valley of Mines and back - they stay dead.
- Take a beer, eat a berry from the ground, take two beers from a chest, put an apple on a table, shoot arrows into a
  wall; save, load - all as left. Loot a dead NPC, save, load - still empty, its dropped weapon lies next to it.
- Eat a mushroom in the Old Mine, save, load, leave and enter again - still gone.
