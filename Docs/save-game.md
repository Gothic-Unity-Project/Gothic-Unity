# Save games

How Gothic-Unity saves and loads a game, which ZenKit pitfalls it works around and how to store additional state.
Entry point: `SaveGameService` (`Assets/Gothic-Core/Scripts/Services/World/SaveGameService.cs`).
Why it's built this way: Docs/adr/2026-10-26-save-games.md.

## Folder layout

`<Gothic>/Saves_GothicUnity/savegameN/` (a mod: `Saves_GothicUnity_mod_<ini>/`). We never write into Gothic's own
`Saves/` folder or a mod's GothicStarter saves.

| File | Content | Written by |
|------|---------|------------|
| `SAVEINFO.SAV` | Title, world, date, game time, play time (load menu) | ZenKit |
| `THUMB.SAV` | Thumbnail. Never empty: an empty file makes `SaveGame.Load()` fail | ZenKit |
| `SAVEHDR.SAV` | Name of the current world | ZenKit |
| `SAVEDAT.SAV` | Daedalus globals (int), told infos, log topics, game time, guild attitudes | ZenKit |
| `<WORLD>.SAV` | One per visited world: VOB tree (incl. NPCs near the hero, hero itself), far NPC list, waynet | ZenKit |
| `UNITYSAVE.json` | Header + sections for state Gothic doesn't store | `UnitySaveDomain` |

A save is written into `savegameN.tmp` first and replaces the slot afterward (`ReplaceSlotFolder()`): a failed save
keeps the old slot and the save menu says so. A crash between the two folder moves leaves `savegameN.bak`: the save
menu restores it and removes leftover `.tmp` folders (`RecoverInterruptedSaves()`).

## What is stored where

Everything Gothic's format can hold is stored in it (loading original saves stays possible later):

| State | Storage |
|-------|---------|
| Hero / NPC stats (level, xp, lp, attributes, protection, missions, hit chances, guild) | oCNpc VOB (`NpcProxy.CopyStatsFromInstance()`) |
| AiVars | oCNpc VOB (`ZenKitNpcExtension.TrySetAiVars()`) |
| Inventory | oCNpc `packed` strings (runtime layout) |
| Equipped items | oCNpc item list with flag `ITEM_ACTIVE` (like original Gothic) |
| Routine, waypoint | oCNpc `CurrentRoutine` (`NpcRoutineService`), `ScriptWaypoint` (`AiHandler`) |
| AI state started by `AI_StartState` (attack, flee, unconscious, ...) | oCNpc `curState` with `isRtnState` = false. Loading makes it the next state, it restarts from its start function like in Gothic (`SaveGameNpcDomain.ContinueSavedState()`). Not `ZS_Talk` |
| NPC target (`Npc_SetTarget`) | oCNpc `enemy` reference (`SaveGameWorldDomain.StoreTargets()`): NPCs of the same save and the hero (while saving) |
| Dead NPCs | HP <= 0 (`BsDead` is set when loading, they lie on the ground: `S_DeadB`) |
| Items in the world (taken, dropped, moved) | world VOB tree (`WorldContainer.Vobs`), position from the item's body |
| Chest content | `oCMobContainer.Contents` (VR: `VrChestSlots` follows its slots) |
| Movers (reached key, a move in progress) | `zCMover.ActKeyframe`, `NextKeyframe`, `ActKeyframeF`, `AdvanceDir`, `MoverState` (`MoverAdapter`) |
| Remaining trigger activations (zCTrigger, oCTriggerScript, zCTriggerList) | `CountCanBeActivated` (`ZenKitExtension.TryConsumeActivation()`) |
| Trigger list in progress (next target, seconds until it's sent) | `zCTriggerList.ActTarget`, `zCVob.NextOnTimer` (`TriggerListHandler`) |
| Script globals, infos, log topics, time, guild attitudes | `SAVEDAT.SAV` (`SaveGameDaedalusDomain`) |
| G1 script int arrays (index > 0) | `UNITYSAVE.json` section `core.scriptArrays` |
| Float, string and NPC script variables (Gothic saves ints only) | `UNITYSAVE.json` section `core.scriptVars` |
| Rain window of the day | `UNITYSAVE.json` section `core.sky` (`SkyService`; Gothic's sky controller can't be used, see pitfalls) |
| Current mob states (wheels, levers, open doors and chests) | `UNITYSAVE.json` section `core.mobStates` (`VobContainer.MobState`, per world, by type and position) |
| Code master progress (pressed switches and plates, press count) | `UNITYSAVE.json` section `core.codeMasters` (`VobContainer.CodeMasterState`, per world, by name; OpenGothic saves it too) |
| VR holster items | `UNITYSAVE.json` section `vr.carriedItems` |

Items in the VR hands are inventory (`VRPlayerService.SetGrab()`). They're inside the backpack after loading.

## Saving

Like Gothic, saving is refused while the hero is dead, during a dialog or without a running world (`SaveGameService.CanSave()`; the save menu shows the reason).

## Loading

1. `LoadSavedGame()` reads the save and its world first (`TryReadWorld()`). An unreadable save or world keeps the
   running game, the load menu says so. `LoadNewGame()` and `ChangeWorld()` (level change) read the world first too.
2. `UnloadGameStart` (old game still exists: dialogs end, VR rig is emptied), `LoadGameStart` (new VM).
3. `SAVEDAT.SAV` is restored into the new VM, `UNITYSAVE.json` sections get `Restore()`.
4. The world comes from the save (`IsWorldRestored`), otherwise from its .zen (`IsWorldEnteredFirstTime` ->
   `STARTUP_*` runs).
5. NPCs are created from the save's VOBs: their instance constructor runs (name, visuals, ...), afterward the saved
   VOB values win (`SaveGameNpcDomain.InitInstanceFromSave()`). `INIT_*` runs again without `Wld_Insert*`.
6. World start triggers don't fire for a restored world (like OpenGothic).

Leaving a world (level change) stores its state into its `WorldContainer` (`StoreCurrentWorldState()`). A revisit in
the same session restores it like a save game (`IsWorldRestored`: no `STARTUP_*`, no world start triggers).

## World items: who owns an item?

`GlobalEventDispatcher.StoreWorldStateStart` (before saving and before leaving a world) lets the platform decide by
the scene. VR (`VRCarriedItemsSaveService.SyncWorldItems()`):

- inside a socket (holster, backpack, chest, loot slot): not part of the world
- inside a hand: inventory; not part of the world. When leaving a world, the hand lets go of it silently: it's
  inside the backpack in the next world.
- lying around: part of the world
- child VOBs which were never taken (e.g. an item lying on a mover) are part of the world through their parent.
  The first grab detaches them (`SaveGameService.DetachChildVob()`).

## Adding state: ISaveStateSection

State which Gothic doesn't store goes into a section of `UNITYSAVE.json`:

```csharp
public class MyFeatureSaveSection : ISaveStateSection
{
    public string Key => "vr.myFeature";   // owner prefix: core., vr., flat., <modId>.
    public int Version => 1;              // increase when the data format changes

    public JToken Capture() => new JObject { ["Value"] = _value };

    // New game, and right before Restore(): forget the previous game.
    public void Reset() => _value = 0;

    // Called while loading, before the first world exists. Apply world related data later
    // (e.g. GlobalEventDispatcher.WorldSceneLoaded).
    public void Restore(JToken data, int savedVersion) => _value = data.Value<int>("Value");
}

saveGameService.RegisterSection(new MyFeatureSaveSection());
```

- Register once, before a save game can be loaded. Core's sections live in `Domain/SaveGames/` and are registered in
  `SaveGameService.Init()`. Modules register their own (VR: `ReflexVRSceneInstaller`), Core doesn't know them.
- `Reset()` runs for every section on a new game and before loading, also if the save has no data for it.
- Sections without a registered owner (newer build, other platform, removed mod) are kept when saving again.
- The header (`UnitySaveHeader`) stores build, game, mod ini and a script signature. Other scripts only log a warning.

## ZenKit pitfalls (shipped binaries)

| Problem | Workaround |
|---------|------------|
| `new Npc()` has `PhysicsEnabled=true`. In G1, ZenKit then reads a rigid body which wasn't written: the world save is unreadable and loading allocates memory endlessly | `SaveGameNpcDomain.ClearPhysicsFlag()` before saving |
| `Npc.AiVars` setter copies bytes instead of ints (13 of 50 values) | `ZenKitNpcExtension.TrySetAiVars()` writes the native array |
| G2 packed inventory: only the first item of category 0 survives | `FlattenPackedInventoryForSave()` / `SplitPackedInventoryAfterLoad()` |
| Empty `THUMB.SAV` makes `Load()` fail | `SaveGameThumbnailDomain` always writes a thumbnail |
| `SaveGame.Save()` deletes the target folder if the SaveGame was loaded from elsewhere; it copies the old folder first | We save through the loaded SaveGame into `.tmp`, then swap |
| World lookup inside a save is case-sensitive | Worlds are saved and looked up upper case without extension |
| G1 `SAVEDAT.SAV` stores one value per variable | Section `core.scriptArrays` |
| `IVirtualObject.Children` creates new wrappers on every call | The world list keeps the wrappers it creates the world with; children are found by values |
| A VOB reference (e.g. `Npc.Enemy`) is another object than the same VOB in the world lists | Found by name and position (`SaveGameNpcDomain.RestoreTargets()`) |
| oCMobInter `State` is Gothic's `stateNum` (number of states, 1 for every door/chest), not the current state | Never written. The current state is `VobContainer.MobState`, section `core.mobStates` |
| The shipped ZenKitCAPI has no `ZkSkyController_new`, worlds from a .zen have no sky controller | Rain window in section `core.sky` (`new SkyController()` throws and fails the save) |
| `zCTriggerList.SendOnTrigger` is the message direction (always true), not "a target is pending" | `NextOnTimer > 0` marks a pending target |

Tests:
- `Assets/Gothic-Tests/PlayMode/SaveGameZenKitTest.cs`: the ZenKit pitfalls (world round trips need a configured
  Gothic installation).
- `Assets/Gothic-Tests/EditMode/SaveGames/`: `UNITYSAVE.json` (sections kept, restored, failing) and world names.

## Known limitations

- Gothic-Unity reads only its own saves (original saves: inventories in the oCNpc item list aren't converted).
- Hand items are inside the backpack after loading (a VR hand needs the grip pressed).
- Like in Gothic, an AI state restarts from its start function: queued AI actions and state timers aren't saved
  (OpenGothic saves them). Saves of older builds marked almost every state as a routine state: their NPCs start the
  routine.
- NPC references inside script variables: monsters share their instance, any NPC of it is taken.
- Gothic's oCNpc has no body state: every NPC with HP <= 0 is dead after loading. An NPC which scripts prepared with
  HP 0 without killing it lies on the ground then.
