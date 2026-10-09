---
name: feature-checklist
description: Implementation checklist for new features and fixes in Gothic-UnZENity - DeveloperConfig flags (C# field AND asset value), Logger/LogCat, GlobalEventDispatcher, Reflex registration, physics layers, save/load impact, search scope. Use when implementing any feature or non-trivial fix, adding a config flag, or touching layers/colliders.
---

# Feature checklist

## Before coding

- [ ] Read the system you're changing end to end (adapter → service → domain → model). Follow the layer rules in
      CLAUDE.md: logic in services/domain, adapters stay thin.
- [ ] If it's Gothic behaviour, read the Daedalus scripts first and prefer calling an existing `B_*` helper (see
      the `daedalus-bridge` skill).
- [ ] **Search narrowly.** The repo is huge - never grep the project root or all of `Assets/`. Scope to e.g.
      `Assets/Gothic-Core/Scripts/Services/`.

## DeveloperConfig flags

New/experimental behaviour goes behind a flag in `Assets/Gothic-Core/Scripts/Models/Config/DeveloperConfig.cs`.
Untested features go in the `TODO TEST ME V1s and MVPs` section until confirmed in a playtest.

**Adding the C# field is not enough.** DeveloperConfig is a serialized ScriptableObject: once the asset is saved,
the C# initializer is ignored (a new `bool` is `false`, and even changing an existing default does nothing).
Also set the value in the asset(s):
- `Assets/Gothic-Core/Resources/DeveloperConfigs/Production.asset` (YAML; a missing key = 0/false; add
  `FieldName: 1` next to a related field), or via the Inspector / MCP `manage_scriptable_object`
- the developer's own local config asset, if they use one

Values that must work in standalone builds need a `GameSettings.json` counterpart - Editor-only DeveloperConfig
fields are not what builds read.

## Logging

- Always `Logger.Log / LogWarning / LogError(message, LogCat.X)`, never `Debug.Log`.
  In plain C# classes: `using Gothic.Core.Logging;` and `using Logger = Gothic.Core.Logging.Logger;`
- Every silent path (early return, missing component, empty list, fallback) gets a warning with the NPC/item name.
- Per-tick checks: one-shot flag so the log fires on the transition only.

## Wiring

- Cross-service communication via `GlobalEventDispatcher` events.
- New services registered in `ReflexProjectInstaller.InstallBindings()` (or the VR/Flat scene installer).
  Watch for DI cycles - resolve lazily via `DIContainer.Resolve<T>()` if needed.
- VR-only code in `Gothic-VR`, guarded by `GOTHIC_HVR_INSTALLED` where it touches HurricaneVR.

## Physics layers

Adding/changing a layer or trigger? See [references/physics-layers.md](references/physics-layers.md). The collision
matrix is symmetric - update **both** rows.

## Save/load impact (state it in your summary)

For every new piece of runtime state:
1. Where is it stored - ZenKit save (vob / instance / Daedalus symbols), `UNITYSAVE.json`, or nowhere?
2. On load: restored once, lost, or applied twice (INIT scripts re-run, restore-from-vob, EquipItem during init)?
3. Is it affected by `EnableSaveLoadSystem` (off by default)?

## Done

- [ ] Compiles (don't trigger a compile while the developer is in play mode)
- [ ] Short summary: one line per changed file
- [ ] Manual test steps (what to do in game, G1 / G2)
