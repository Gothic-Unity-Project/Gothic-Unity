# 13. Architecture and conventions check

[← Overview](README.md)

How far the branch diverges from the rules in `CLAUDE.md`, `.editorconfig` and the Coding Style Guide. Checked
automatically over all 190 changed C# files, plus a manual look at the biggest classes.

## Summary

| Rule | Result |
|---|---|
| Star topology, Core never references VR/Flat | ✅ No new violations. (`NpcMeshBuilder` already used `Gothic.VR.Adapters.Npc` on `main`.) |
| Namespaces follow folders | ✅ All 62 new files match |
| Private fields `_camelCase`, public `PascalCase` | ✅ No new violations found |
| Boolean names with verb prefix (`Is…`, `Has…`, `Enable…`) | ✅ Followed (`IsSwingInProgress`, `IsRuntimeSlot`, `Enable…` flags) |
| No `#region` | ✅ None added (two pre-existing on `main`) |
| Logging via `Logger.Log(message, LogCat)` | ✅ ~650 calls with a category; `Debug.Log` only in the editor build script |
| `[Tooltip]` instead of field comments in configs | ✅ All new `DeveloperConfig` fields have tooltips |
| Events through `GlobalEventDispatcher` | ✅ 17 new events added there. ⚠️ Exceptions below |
| New services registered in Reflex | ✅ All 9 new services are registered (Core installer or VR scene installer) |
| One class per file | ⚠️ 3 new files with several types |
| Max line width 120 | ⚠️ ~760 added lines over 120 (128 of them are Bink codec tables, 55 config tooltips) |
| Adapters are thin, logic lives in services/domain | ⚠️ Main divergence — see below |
| Allman braces, 4 spaces, `var` | ✅ Followed |

## Findings in detail

### ⚠️ Logic in adapters (main divergence)

`CLAUDE.md`: "Adapters … are thin wrappers — logic belongs in services". Several new VR adapters hold real logic:

| Adapter | Lines | What would move out |
|---|---|---|
| `Gothic-VR/.../VRRuneCaster.cs` | 1,356 | Spell charging, invest levels, mana timing, projectile setup → a `VRSpellCastService`/domain |
| `Gothic-VR/.../VRHeroBody.cs` | 768 | Mesh cutting, slot mapping → domain; IK can stay |
| `Gothic-VR/.../VRTransformPuppet.cs` | 748 | Puppet movement decisions → `VRTransformService` (already exists) |
| `Gothic-VR/.../VRStackSplitter.cs` | 670 | Split timing and target rules → `StackSplitService` (thin today) |
| `Gothic-Core/Adapters/Npc/NpcJumpFall.cs` | 647 | Fall/climb decisions, fall damage → domain |

Counter-example done right: **trade** — `TradePricing` (domain, unit tested), `TradeSession` (model),
`TradeService` (service), `VRTradeCounter` (presentation).

### ⚠️ God classes keep growing

| Class | `main` → branch |
|---|---|
| `VmExternalDomain` | 1,283 → 2,168 lines |
| `NpcAiService` | 558 → 1,370 |
| `FightService` | 149 → 965 |
| `SaveGameService` | 392 → 1,070 |
| `VobService` | 543 → 933 |
| `DeveloperConfig` | 295 → 757 (116 new fields) |

Suggested splits: perception broadcasting and targeting out of `NpcAiService`; knockout/death and fight
perceptions out of `FightService`; trigger dispatch out of `VobService`; externals grouped by topic
(`VmExternalDomain` partial classes per area: doc, mob, perception, spells).

### ⚠️ Patterns outside the project conventions

- **Static singletons** instead of DI: `VRCinema._instance`, `VRScreenMessages._instance`. Other static state is
  caches (materials, shaders, font) — acceptable.
- **Plain C# events** instead of `GlobalEventDispatcher`: `VRPlayerService.TelekinesisDeactivated`.
  `VRProjectile.OnNpcHit/OnWorldHit/OnExpired` are per-instance callbacks (not global events) — fine as is.
- **Several types per file:** `UnityCustomSave.cs` (5 save DTOs), `DocModel.cs` (`DocModel` + `DocPage`),
  `BinkBitStream.cs` (2, ported code).
- **Fully qualified type names** in `ReflexProjectInstaller` for `TradeService`/`StackSplitService` (missing `using`).

### ℹ️ Domain classes injecting services

Several animation actions (`DrawWeapon`, `UseItemEffect`, …) inject services (`NpcService`,
`NpcInventoryService`). This pattern already existed on `main`, so it's consistent with the codebase, but it
blurs the "Domain doesn't depend on Services" layering if that's meant strictly.

### ✅ Things that fit the architecture well

- New Daedalus externals are registered only when the scripts declare the symbol (`RegisterIfDeclared`), so G1, G2
  and mods with different script sets all boot.
- Engine behavior is reproduced by **calling Daedalus** (`B_*`, `Spell_ProcessMana`, `ZS_*` states) instead of
  hardcoding, matching the project's "check B_* first" practice.
- New features are behind `DeveloperConfig` flags with tooltips, so they can be disabled without code changes.
- Comments explain the "why" and reference OpenGothic or the original scripts.

## Recommended follow-ups (not blockers)

1. Move logic out of the five large VR/Core adapters above (start with `VRRuneCaster`).
2. Split `NpcAiService`, `FightService`, `VmExternalDomain`.
3. Replace the two static singletons with DI; move `TelekinesisDeactivated` to `GlobalEventDispatcher`.
4. Flag cleanup: promote confirmed flags to always-on, delete dead code paths.
5. Wrap lines over 120 in hand-written code (leave the Bink tables).
