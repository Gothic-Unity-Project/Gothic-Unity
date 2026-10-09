---
name: daedalus-bridge
description: How C# talks to Gothic's Daedalus VM in Gothic-UnZENity - externals, NpcInstance vs vob storage, inventory, calling B_* helpers via vm.Call. Use whenever implementing or fixing a Daedalus external (VmExternalDomain, NpcAiService, NpcInventoryService, FightService), AI states / perceptions, XP/attributes, items, or any "the script does X but our game does Y" bug.
---

# Daedalus ↔ C# bridge

Gothic game logic lives in Daedalus scripts (`.d`, compiled to `GOTHIC.DAT`) executed by ZenKit's `DaedalusVm`.
Our C# code implements the engine side: externals, perceptions, the AI queue, storage. Most bugs are a mismatch
between what the script expects from an external and what our implementation does.

## Golden rules

1. **Read the script before writing C#.** Find every call site of the external / state in the Gothic scripts and
   understand the expected return value and side effects. Sources (public):
   - Gothic 1 scripts: https://github.com/VaanaCZ/gothic-1-classic-scripts
   - Gothic 1 / Gothic 2 Mod Development Kit (MDK) - `_work/data/Scripts/` (ask the developer for their local copy)
   - Reference engine behaviour: OpenGothic https://github.com/Try/OpenGothic (`game/game/gamescript.cpp` lists
     externals, `game/world/objects/npc.cpp` NPC logic)
   - ZenKit / ZenKitCS: https://github.com/GothicKit/ZenKit, https://github.com/GothicKit/ZenKitCS (symlinked at `./ZenKitCS/`)
2. **Check for a `B_*` / `C_*` helper before hardcoding logic in C#.** XP, spells, weapon selection, attitude, item
   transfer usually already exist in Daedalus. Call them instead of reimplementing:
   ```csharp
   var vm = _gameStateService.GothicVm;
   var oldSelf = vm.GlobalSelf; var oldOther = vm.GlobalOther;
   try
   {
       vm.GlobalSelf = npc.Instance;
       vm.GlobalOther = hero.Instance;
       // Call<Return, Param1, Param2, ...>(nameOrIndex, args) - first generic is the return type (omit if void)
       var ret = vm.Call<int, NpcInstance, NpcInstance, int, int>("B_GIVEINVITEMS", npc, hero, itemIndex, 2);
   }
   finally
   {
       vm.GlobalSelf = oldSelf; vm.GlobalOther = oldOther;
   }
   ```
   Raw stack push (`memint_stackpushint`-style) is not supported - always use the generic `Call<>` overloads.
3. **Match the declared parameter types exactly.** A `VAR FLOAT` parameter registered as `int` delivers the raw
   IEEE-754 bits (500.0 → 1140457472). Check the `extern` declaration (`Externals.d`) before registering.
4. **Unimplemented externals fail silently.** `DefaultExternal` logs
   `Method >NAME< not yet implemented in DaedalusVM` and returns 0 - the script then takes the wrong branch (loops,
   NPCs that never fight back, quests that never advance). Check for these warnings first.
5. **Nested `vm.Call` during `InitInstance` is dangerous.** An external running inside an instance constructor
   (EquipItem, Mdl_SetVisualBody...) must not call script functions that can fail - a failing nested call aborts
   the whole outer constructor (NPC loses talents/items). Defer to the next frame.
6. **Uncaught C# exceptions inside an external corrupt the VM.** Each throw across the native→managed boundary
   unbalances the VM stack; after enough of them you get `stack overflow` / `pop_instance` errors and a native crash.
   Guard externals and log instead of throwing.

## Two storages: NpcInstance vs vob

`NpcInstance` (Daedalus) and the ZenKit `INpc` vob are **separate** and only copied once at spawn.

| Data | Who reads it |
|---|---|
| `vob.GetAttribute/SetAttribute`, `vob.Xp/Level/Lp` | HP bar, FightService, status menu, save game |
| `npc.Instance.GetAttribute`, `hero.Exp/Level/Lp`, aivars | Daedalus (`self.attribute[ATR_HITPOINTS]`, `hero.exp`) |

- When C# changes one side, decide whether the other must be synced. After script XP changes (`B_GiveXP`,
  `B_DeathXP`) call `SyncHeroInstanceToVob()` so the UI updates.
- `Npc_ChangeAttribute(self, ATR_HITPOINTS, 12)` **adds** 12, it does not set. Clamp even attributes to their
  paired `_MAX` (odd index).
- Aivars live on the Instance and must be flushed to `Vob.AiVars[]` before saving.

## Inventory

ZenKit `INpc` has two item storages that do not sync:
- **Packed** - `GetPacked(cat)/SetPacked(cat, ...)` - all runtime inventory (CreateInvItems, RemoveInvItems,
  backpack). Use this.
- **VOB item list** - `GetItem(i)/ItemCount/ClearItems()` - only filled from world/save files. Don't use at runtime.

Names: `ItemInstance.Name` is the **display name** ("Old Mace"), not the symbol (`ITMW_1H_MACE_02`). Cache keys,
packed inventory and `ContentItem.Name` use the symbol name:
```csharp
var symbolName = vm.GetSymbolByIndex(item.Index)?.Name;
_vmCacheService.TryGetItemData(symbolName); // case-insensitive
```
World-placed items have `Amount = 0` meaning "one" - use `Mathf.Max(1, amount)`.
Get Daedalus data via `VmCacheService` (never init instances manually); NPC container via `npcInstance.GetUserData()`.
ZenKit's `Pop<NpcInstance>()` can return a fresh wrapper without UserData - resolve by index if `GetUserData()` is null.

## Naming conventions in scripts

| Prefix | Meaning |
|---|---|
| `ZS_X` / `ZS_X_Loop` / `ZS_X_End` | AI state entry / per-tick loop (returns LOOP_CONTINUE / LOOP_END) / cleanup |
| `B_*` | reusable behaviour block - callable from C# |
| `C_*` | pure condition, returns bool |
| `TA_*`, `Rtn_*` | daily routine entries / schedules |
| `DIA_*` | dialog instances |
| `AIV_*`, `HAI_*` | aivar indices, human AI constants |
| `PERC_*` | perception types |

## AI and perceptions

See [references/ai-perceptions.md](references/ai-perceptions.md) for the state machine, queued `AI_StartState`,
perception split between C# and Daedalus, and known traps (target writers, guild/party exclusions).

## Before you finish

- Add `Logger.LogWarning(..., LogCat.X)` on every early-return / silent fallback in the external.
- Think about save/load: is the new state in the vob, the instance, `UNITYSAVE.json`, or nowhere?
