# 12. Dev tools, config, build, project settings

[← Overview](README.md)

## What changed

**Marvin (cheat panel)**
- Minimize/restore, configurable waypoint teleport, time skip, level +5 (with mana), guild cheats, NPC spawn by
  symbol, give items from a config list.

**DeveloperConfig**
- **116 new fields**, almost all `Enable…` feature flags with `[Tooltip]`s, plus tuning values.
- `Production.asset` is now serialized in full.

**Build and project settings**
- VR build switches the platform first and asks to start again (Unity 6 "Unable to build with the current
  configuration").
- Physics: Player ↔ VobMovable collision (locked doors block). Graphics: `Gothic/UI` always included.
  ProjectSettings: XR settings preloaded, `SENTIS_ANALYTICS_ENABLED` (added by the `com.unity.ai.inference`
  package). Tags `Subtitle`/`Title` registered (prefabs already used them).
- NVorbis DLL committed with a `.gitignore` exception.
- ZSpy Daedalus logging via `GameSettings.json`; Uber Logger categories used throughout.

## Key commits

`bb6e7c4d`/`5959c1ad`/`8ea94468`/`aebea3c9`/`5e898826` Marvin · `e26326bb` build · `7c3afb78` project settings ·
`0d220118` ZSpy · `76f2eace` Production written out · `bc632361` NVorbis

## Main files

`Models/Config/DeveloperConfig.cs` (+472) · `Resources/DeveloperConfigs/Production.asset` ·
`Gothic-VR/Adapters/Marvin/MarvinTabHandler.cs` · `Gothic-VR/Editor/VRBuilderActions.cs` · `ProjectSettings/*` ·
`Scenes/Bootstrap.unity`

## Review notes

- ✅ `Bootstrap.unity` uses `Production.asset` (as on `main`). It briefly pointed at a gitignored local config;
  fixed before pushing. Testers assign their own config locally (see [How to test](14-how-to-test.md)).
- ⚠️ **Flag sprawl:** 116 new fields. Good for safe rollout, but it needs a cleanup pass: promote stable flags to
  always-on and delete the dead branches; group the rest in `[Foldout]`s. Several are "TODO TEST ME" V1/V2 flags.
- ⚠️ **NVorbis** is committed to the repo instead of the binary-dependencies repo (comment in `.gitignore` says so).
- ℹ️ `Debug.Log` is used directly in `VRBuilderActions.cs` (editor-only build script) — acceptable outside runtime.

## How to test

- Fresh clone on another machine: open Bootstrap, play.
- Build PCVR via `Gothic.VR.Editor.VRBuilderActions.PerformWindows64Build`.
