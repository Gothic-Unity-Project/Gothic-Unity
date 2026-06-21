# Save/Load System — Gothic-UnZENity

## Overview

Two-layer save architecture. Every save slot contains:

```
Saves/savegame{N}/
  SAVEDAT.SAV         ← ZenKit: Daedalus symbol table, quest log, dialog told-flags, time
  SAVEINFO.SAV        ← ZenKit: metadata (title, world name, day/time)
  THUMB.SAV           ← ZenKit: 256×256 screenshot
  WORLD.SAV           ← ZenKit: full world VOB tree (items, movers, chest states)
  UNITYSAVE.json      ← our custom: hero pos/stats/inventory, NPC dirty delta, guild attitudes

Saves/UNITYNPCINIT.json   ← baseline NPC spawn snapshot (taken once on new game, shared across slots)
Saves/GOLDENSAVE/         ← optional: world .SAV stubs for Gothic.exe compat (see below)
```

**Everything is gated behind `DeveloperConfig.EnableSaveLoadSystem`.** When the flag is off, save/load is completely inert. Default is `false` — enable explicitly to test.

For mods, the folder name changes: `saves_{modname}/` (e.g. `DM_E.ini` → `saves_dm_e/`).

---

## What Works (V3)

| Feature | Stored in | Notes |
|---|---|---|
| Hero position/rotation | UNITYSAVE.json | Applied before scene spawn |
| Hero attributes (HP, MaxHP, Mana, Str, etc.) | UNITYSAVE.json | All 8 NpcAttribute slots |
| Hero level, XP, LP, guild | UNITYSAVE.json | |
| Hero full inventory | UNITYSAVE.json | All categories; clear+restore on load |
| Game time (day/hour/minute) | SAVEDAT.SAV | Via `RestoreDaedalusState` |
| Daedalus VM symbol states | SAVEDAT.SAV | All non-const int/float globals → quest flags, chapter, etc. |
| Quest log topics | SAVEDAT.SAV | Copied from live `Save.State` |
| Dialog told-flags | SAVEDAT.SAV | Which NPC dialog options have been seen |
| World VOB state (items/movers/chests) | WORLD.SAV | Items removed from world stay gone; chests stay open. **NPCs are excluded** — see Architecture. |
| Dropped/grabbed world item positions | WORLD.SAV + tracking | Items picked up and put down in the world have their Unity transform synced to the ZenKit VOB before WORLD.SAV is written |
| NPC dirty delta — position/rotation | UNITYSAVE.json | NPCs snapshot on cull; restored when they enter range |
| NPC dirty delta — attributes | UNITYSAVE.json | HP, mana etc. preserved across cull cycles |
| NPC dead flag | UNITYSAVE.json | `BodyState = BsDead` applied on restore |
| Guild attitude matrix | UNITYSAVE.json | Flat int array; faction changes persist |
| Screenshot thumbnail | THUMB.SAV | 256×256 RGB565, Y-flipped for Gothic.exe compat |

---

## What Does NOT Persist

| Feature | Status | Notes |
|---|---|---|
| NPC inventory | Not saved | Stolen items reappear; only hero inventory is tracked |
| NPC AI state / current ZS_ function | Struct saved, never applied | NPCs reset to Daedalus routine on load (acceptable — same as Gothic 1) |
| Equipped items (hero) | Not saved | Daedalus startup re-equips default gear; backpack inventory IS correct |
| Active audio/particle state | Not saved | Not needed |
| Physics state (flying objects etc.) | Not saved | Not needed |

---

## Load-Multiple-Times During a Session

**Supported.** Every `LoadSavedGame()` call fully resets state:

```csharp
_worlds.ClearAndReleaseMemory();   // world cache wiped
_npcSnapshots.Clear();             // culling snapshots wiped
_pendingNpcRestore = null;
_pendingNpcInit = null;
_pendingSaveRestore = save;        // new save wired in
```

Pattern `Start → Load → Save → Load → Load` works. Each load triggers a full scene reload; pending state from any previous load is overwritten.

**Watch for:** If a second load shows `ChangeWorld: falling back to .zen` in logs but the first load showed `loaded VOB state from WORLD.SAV`, that's a scene-reuse race condition where `_worlds` was repopulated before `ChangeWorld` ran. Check that the world scene actually unloads between loads.

---

## Architecture — Code Paths

### NPC Loading — Why NPCs Are Excluded from WORLD.SAV

WORLD.SAV stores a VOB tree that includes `oCNpc` entries for every NPC alive when the file was written. Daedalus startup scripts also call `Wld_InsertNpc()` to spawn all NPCs from scratch. Loading both sources creates duplicate NPCs — two Diegos walking around, etc.

**Resolution:** On load, `ChangeWorld()` filters `VirtualObjectType.oCNpc` out of WORLD.SAV's `RootObjects`. The `Npcs` list is always taken from the original `.zen` world (which is empty — NPCs come exclusively from Daedalus). Hero position is always read from `UNITYSAVE.json → PlayerService.HeroSpawnPosition`, never from the WORLD.SAV hero VOB.

```
NPC sources at load time:
  Daedalus startup → Wld_InsertNpc() → all world NPCs spawned
  UNITYSAVE.json   → NPC dirty delta → position/HP/dead overrides applied when NPC enters range
  WORLD.SAV        → items / movers / chests ONLY (oCNpc filtered out)
```

### Loose Item Tracking

Items picked up from the world or dropped from the backpack have their Unity `Transform` synced to the ZenKit VOB in `PrepareWorldDataForSaving`, so WORLD.SAV records the correct position.

- **VRBackpack.OnItemPutOutOfBackpack** → `SaveGameService.TrackLooseItem(vobContainer)`
- **VRBackpack.OnItemPutIntoBackpack** → `SaveGameService.UntrackLooseItem(vobContainer)`
- **VRPlayerItemAdapter.OnBeforeGrabbed** → `SaveGameService.TrackLooseItem(vobContainer)` (covers direct world grab without going through backpack)

`_looseWorldItems` is a `Dictionary<IVirtualObject, VobContainer>`. At save time, `PrepareWorldDataForSaving` iterates it and writes `vob.Position`/`vob.Rotation` from the live Unity transform. Items spawn kinematic by default, so a loaded floating item is frozen in place — acceptable.

**Known gap:** Items taken directly from a chest are not in `_looseWorldItems` or `CurrentWorldData.Vobs` until placed in the world or backpack. They will appear at their original chest position on load if dropped between chest and backpack.

### Save

```
SaveCurrentGame(SlotId, title)
  FlushDaedalusState(saveGame)         → symbol table, quest log, time → SAVEDAT.SAV
  PrepareWorldDataForSaving(...)       → syncs _looseWorldItems transforms → ZenKit VOBs
                                         then merges hero/NPC VOB positions into world VOB tree
  saveGame.Save(path, world, name)     → ZenKit writes SAVEDAT/SAVEINFO/THUMB/WORLD.SAV
  CopyGoldenSaveToSlot(SlotId)         → copies GOLDENSAVE world stubs (Gothic.exe compat, optional)
  SaveUnityCustomData(SlotId)          → UNITYSAVE.json: hero stats + inv + attitudes + NPC delta
```

### Load

```
LoadSavedGame(SlotId)
  _worlds.Clear(); _npcSnapshots.Clear(); _looseWorldItems.Clear()
  _pendingSaveRestore = save
  LoadUnityCustomData(SlotId)          → reads UNITYSAVE.json; sets _pendingHeroRestore + _pendingNpcRestore
                                         also sets PlayerService.HeroSpawnPosition before scene loads

[Scene reload happens]

ChangeWorld(worldName)
  Save.LoadWorld(saveName)             → ZenKit reads WORLD.SAV
    RootObjects: filter out oCNpc → items, movers, chests only
    Npcs:        always from original .zen (empty; Daedalus spawns NPCs)
    on failure   → falls back to .zen

[World content spawns from VOB tree — NPCs from Daedalus, items from WORLD.SAV]

OnWorldSceneLoaded()
  RestoreDaedalusState(save)           → symbol values, game time
  ApplyHeroRestore(data)               → attributes, level, XP, LP, guild
                                          ExtNpcClearInventory → ExtCreateInvItems for each saved item
                                          GuildAttitudes array copy

[NPC enters visible range]
OnNpcCullingChanged(npc, visible=true)
  ApplyNpcSavedState(npc, entry)       → position, attributes, IsDead → BodyState = BsDead
```

---

## GOLDENSAVE

Optional folder at `Saves/GOLDENSAVE/`. If it exists, its world `.SAV` files are copied to the save slot after ZenKit writes our files. This lets Gothic.exe open our save slots in its own load menu (metadata + world stubs match what Gothic expects).

**Without GOLDENSAVE:** Our own load works fine. Gothic.exe cannot open the slot. Log shows warning `CopyGoldenSaveToSlot: no GOLDENSAVE` but save succeeds.

**With GOLDENSAVE:** Gothic.exe can open the slot and see metadata/thumbnail. It will not see our runtime changes (hero position, inventory changes, etc.) — only the Daedalus state in SAVEDAT.

---

## Mod Support

- **Daedalus layer:** Symbol states stored by name, not index. Unknown symbols on load are silently skipped. New Daedalus variables from mods default to 0 when loading a pre-mod save. Same approach as OpenGothic.
- **UNITYSAVE.json:** JSON with nullable fields. Unknown keys are ignored on load. Version field (`Version = 3`) is present for future migration logic.
- **Guild attitudes:** Only restored when saved array length matches current `GuildCount * GuildCount`. Mods that add guilds will skip attitude restore (safe fallback — attitudes re-initialize from Daedalus).
- **Save folder isolation:** Mods use `saves_{modname}/` folder, so mod saves never collide with vanilla saves.
- **NPC talent count:** Clamped to 12 when writing WORLD.SAV for Gothic.exe compat (mods with `NPC_TALENT_MAX > 12` would corrupt the binary otherwise).
- **AiVars:** Padded to exactly 50 entries per NPC in the VOB tree for Gothic.exe format compliance.

---

## Key Files

| File | Purpose |
|---|---|
| `Assets/Gothic-Core/Scripts/Services/World/SaveGameService.cs` | Main orchestrator — all save/load logic, `_looseWorldItems` tracking |
| `Assets/Gothic-Core/Scripts/Services/World/UnityCustomSave.cs` | JSON schema: `UnityCustomSave`, `NpcSaveEntry`, `HeroInventoryEntry`, `NpcInitEntry` |
| `Assets/Gothic-Core/Scripts/Models/Config/DeveloperConfig.cs` | `EnableSaveLoadSystem` flag |
| `Assets/Gothic-Core/Scripts/Domain/Npc/NpcInitializerDomain.cs` | Gate points where flag controls NPC snapshot save and VOB→Instance restore |
| `Assets/Gothic-Core/Scripts/Services/Npc/NpcService.cs` | Gate for `PendingNpcInit` (merged snapshot path vs normal init) |
| `Assets/Gothic-Core/Scripts/Services/Npc/NpcInventoryService.cs` | `GetAllInventoryItems`, `ExtNpcClearInventory`, `ExtCreateInvItems` |
| `Assets/Gothic-VR/Scripts/Adapters/Player/VRBackpack.cs` | Calls `TrackLooseItem`/`UntrackLooseItem` when items leave/enter backpack |
| `Assets/Gothic-VR/Scripts/Adapters/Player/VRPlayerItemAdapter.cs` | Calls `TrackLooseItem` on direct world grab (no backpack involved) |

---

## Reference: How Other Engines Handle This

### ZenKit (what we use)
- `SaveGame(GameVersion)` — top-level handle
- `save.Load(path)` / `save.Save(path, world, worldName)` — directory-based
- `save.LoadWorld(name)` — returns `World` with full VOB tree (nullable if world not in save)
- `SaveState` — Daedalus symbol dumps, quest log, dialog told-flags, guild attitudes, game time
- G1 vs G2: constructor parameter only; same API surface

### OpenGothic (reference implementation)
- ZIP-based binary, versioned (`Current = 55`, back-compat to `36`)
- Saves Daedalus symbols **by name** (same as us) → mod-safe; missing symbols silently skipped
- Per-entity serialization: NPC (full inventory, AI state, waypoint, fight state), Item (all instance fields including equipped)
- Hero serialized into `HeroStorage` blob on world transitions
- Our two-layer approach (ZenKit + UNITYSAVE.json) mirrors this split: native Gothic layer vs Unity-specific layer

---

## Testing Scenarios

All scenarios assume `EnableSaveLoadSystem = true`, no pre-existing saves, no GOLDENSAVE.

### 1. Basic cycle — position and stats

1. Start new game, walk to a specific landmark
2. Save to slot 1
3. Load slot 1
4. **Expect:** Hero spawns at exact saved position. HP/Mana/level/XP match.
5. **Log to check:** `SaveUnityCustomData: level=X hp=Y/Z` then `ApplyHeroRestore: vob AFTER level=X hp=Y/Z` — numbers must match.
6. **Non-issue:** `CopyGoldenSaveToSlot: no GOLDENSAVE` warning is expected.

---

### 2. Hero inventory persists

1. Pick up several items (apple, sword, lockpick, gold coins)
2. Open backpack — note exactly what you have
3. Save → Load
4. **Expect:** Backpack has same items, same counts. No duplicates.
5. **Duplicates = bug:** `ExtNpcClearInventory` didn't fire — check logs for `ApplyHeroRestore: restored N/M inventory entries`. N should equal M.
6. **Empty backpack = bug:** `GetSymbolByName` failed for all items — logs will show `item 'X' not found in VM`.

---

### 3. World item doesn't respawn (key WORLD.SAV test)

1. Pick up an item lying on the ground (e.g. ore pile, apple in starting hut)
2. Save → Load
3. **Expect:** Item is gone from the world. The WORLD.SAV has it removed.
4. **NPCs spawning is unrelated to this** — they always come from Daedalus startup scripts, not WORLD.SAV.
5. **If item respawns:** WORLD.SAV load failed — check logs for `ChangeWorld: loaded 'WORLD.ZEN' VOB state from WORLD.SAV`. If you see `falling back to .zen`, the exception message explains why.

---

### 4. Opened chest stays open

1. Find a chest, open it, take all items
2. Save → Load
3. **Expect:** Chest is open and empty.
4. **Known gap:** Items taken from chest and dropped on the ground before saving may not have their new position tracked (see scenario 4b).
5. Same WORLD.SAV failure mode as scenario 3 if chest resets.

---

### 4b. Item grabbed from world retains dropped position

1. Pick up a world item (apple, ore, weapon) — do NOT put it in the backpack
2. Carry it a few metres and drop it
3. Save → Load
4. **Expect:** Item is lying where you dropped it, not at its original spawn position.
5. **Log to check:** Item's VOB should have an updated position — `PrepareWorldDataForSaving` syncs `_looseWorldItems` transforms.
6. **If item is at original position:** VRPlayerItemAdapter's `OnBeforeGrabbed` didn't fire or the VobLoader component wasn't found — check logs.
7. **If item disappears entirely:** It was tracked as a loose item but not added back to `CurrentWorldData.Vobs` — check `OnItemPutOutOfBackpack` / direct grab path.

---

### 5. Game time persists

1. Note current time (in-game clock or day counter)
2. Save → Load
3. **Expect:** Clock matches saved time. Day/night lighting matches.

---

### 6. Killed NPC stays dead

1. Kill an NPC (e.g. bandit)
2. Walk far enough away that they get culled out (snapshot taken — watch for `OnNpcCullingChanged` log)
3. Save → Load → walk back into that area
4. **Expect:** NPC appears dead. Logs show `ApplyNpcSavedState: <name> hp=0/X dead=True`.
5. **Known limitation:** `BodyState = BsDead` is set but the NPC's AI coroutine may still run. If the NPC walks around while dead, that's a known follow-up — the AI queue needs to be drained on death restore.
6. **If NPC fully alive:** `IsDead` flag wasn't set — check `CreateNpcSnapshot` to confirm HP ≤ 0 was detected.

---

### 7. NPC position dirty delta

1. Trigger an NPC to move away from their default spot
2. Walk far enough away to cull them
3. Save → Load → return to that area
4. **Expect:** NPC spawns at their last known position, not their default waypoint.
5. **Log:** `ApplyNpcSavedState: <name> hp=X/Y dead=False` when they enter range.

---

### 8. Guild attitudes survive

1. Trigger a faction attitude change (join a camp, commit a crime that changes NPC hostility)
2. Save → Load
3. **Expect:** NPCs still have the changed attitude (hostile/friendly as at save time).
4. **Log:** `ApplyHeroRestore: restored guild attitude matrix (N entries)`. If N=0 or size mismatch, check array lengths.

---

### 9. Multiple slots are independent

1. Save to slot 1 at location A with inventory set X
2. Walk to location B, pick up different items
3. Save to slot 2
4. Load slot 1
5. **Expect:** Hero at location A with inventory X.
6. Load slot 2
7. **Expect:** Hero at location B with inventory Y. Previous load left no residue.

---

### 10. Multiple loads in one session

1. Start game → Load slot 1 → play → Save (overwrite slot 1) → Load slot 1 → Load slot 1 again
2. **Expect:** Each load works correctly. No "stuck on old data" after the second load.
3. **Watch for:** Second load showing `falling back to .zen` when first load showed `VOB state from WORLD.SAV` — would indicate a world cache not being cleared properly.

---

### 11. Load a slot that doesn't exist

1. Try to load a slot number that has no save folder
2. **Expect:** Error log `SaveGame with id X doesn't exist`. No crash. Game stays on menu.

---

## Fixed Bugs (historical)

### NPC FreePoint wrong-post bug (2026-06-21)

**Symptom**: Guard NPCs (e.g. Mroczne Tajemnice's "Podejrzany typ") walk to a distant, wrong FreePoint on first spawn and after every dialog. After the NPC falls through world geometry and gets re-enabled via culling, they stay at the correct spot permanently.

**Root cause 1 — nested FreePoint position stored as local, not world** (`VobInitializerDomain.cs / CreateSpot`):  
`FreePoint.Position` was set from `vob.Position.ToUnityVector()` — the VOB's **local** offset relative to its parent in the VOB tree. For a FreePoint nested inside another VOB (common in mods), this makes the stored position wrong. `FindNearestFreePoint` then sees the physically nearby correct FP as hundreds of meters away and picks a distant one instead.  
**Fix**: pass the pre-computed `worldPosition` (accumulated parent chain + local offset, already available in `InitVob`) into `CreateSpot` and use it for `FreePoint.Position`.

**Root cause 2 — `StartRoutine` wiped restored `CurrentFreePoint` on first start** (`AiHandler.cs / StartRoutine`):  
`StartRoutine` clears `CurrentFreePoint` when `didRoutineChange = true`. On the very first call after spawn/load, `Vob.CurrentStateIndex` is 0 and the new routine index is non-zero → `didRoutineChange = true` → the FP we just restored from dirty save data was immediately cleared. GoToNextFP then ran a world-wide FP search and picked the wrong one.  
**Fix**: guard the clear with `Vob.LastAiState != 0`. On first start `LastAiState` is 0 (no prior state) so the restored FP is preserved. On genuine routine transitions (`ZS_GUARD → ZS_TALK → ZS_GUARD`) `LastAiState` is non-zero and the clear still fires.

**Why cull accidentally fixed it**: after re-enable, the same routine restarts with the same index → `didRoutineChange = false` → `CurrentFreePoint` survived → `Npc_IsOnFP("GUARD")` returned true → `B_GotoFP` skipped `AI_GotoNextFP` entirely → NPC stayed at spawn WP.

### NPC FreePoint wrong-post on save/load (2026-06-21)

**Symptom**: On load, guard NPC (Lukis) walked to the wrong FreePoint because another NPC (Huard) with a dirty position near the spawn area grabbed the correct FP first.

**Fix**: Save `CurrentFreePointName` in `NpcSaveEntry` and restore + pre-lock it during `InitNpcsFromMergedSnapshots` (dirty path). Also added `FindNearestFreePoint` short-circuit that returns `npcOnFp` directly when the NPC already owns a matching, unlocked FP — preventing unnecessary re-evaluation. `ReEnableNpc` preserves `CurrentFreePoint` when the routine WP is a WayPoint (not a FreePoint).

---

## Known Gaps / Future Work

- **NPC inventory** — Stolen items reappear on load. NPC inventories not yet tracked in UNITYSAVE.
- **NPC AI state** — `CurrentStateName`/`CurrentRoutine` are saved in snapshot struct but not applied on restore. NPCs reset to their Daedalus daily routine. This is intentional for now — restoring mid-fight or mid-walk states without re-running prerequisite Daedalus init would cause broken AI.
- **Equipped items (hero)** — Daedalus startup re-equips default gear. What was equipped at save time is not restored separately (it lives in the backpack inventory, but the equipped weapon mesh depends on Daedalus init order).
- **NPC dead AI drain** — Setting `BodyState = BsDead` on restore does not stop the NPC's AI coroutine. If AI runs with dead state, NPC can walk while appearing dead. Fix: drain `AnimationQueue` and set `CurrentLoopState = End` in `ApplyNpcSavedState` when `IsDead = true`.
- **Multi-world NPC tracking** — UNITYSAVE only captures NPCs from the currently loaded world. NPCs in other worlds (e.g. Free Mine) are not snapshotted until that world is visited.
- **Chest-to-ground item tracking** — Items taken directly from a chest and placed on the ground are not added to `_looseWorldItems` until the next grab event (VRPlayerItemAdapter). If you open a chest, drop something, and save before picking it up again, it may appear at its chest spawn position on load. Workaround: pick it up once after placing it on the ground before saving.
