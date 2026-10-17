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
- HVR keyboard/mouse simulator in PC builds via `GameSettings.json` `"EnableVRDeviceSimulator"` (it is plain runtime
  code; only the old XRIT simulator was Editor-only). XR is stopped while it runs.

**Added 2026-10-10 – 2026-10-11**

- **Game profiles:** pick G1, G2 or a mod from a list at start (`GameSettings.json`
  `EnableGameProfiles`/`GameProfiles`).
- **Restart game** in the Immersion settings (Editor re-enters Play, a PC build restarts its exe).
- **Marvin:** shown/hidden from the Immersion settings; `TP +4 m` (Gothic's K) with fall rescue; god mode; "Friends"
  spawns a list of NPCs (e.g. the Irdorath crew); the panel scrolls with more than 6 options.
- After the ending's videos and credits the game returns to the main menu / profiles instead of quitting.

**Added 2026-10-11 – 2026-10-13**

- **Licenses in every build:** `LICENSE` and `THIRD_PARTY_NOTICES.md` (Bink decoder LGPL-2.1+/MIT, NVorbis, Uber Logger,
  package dependencies; OpenGothic as behavior reference; no Gothic data shipped) are copied next to the PC executable
  and into the Android APK (temporary `StreamingAssets/Licenses`, removed after the build).
- `main` merged in (shared Claude Code setup, robustness split).
- Changelog for the experimental build's Discord channel ([CHANGELOG.md](CHANGELOG.md)).
- **Marvin teams:** "Friends" is now "Irdo team" (the Irdorath crew including Torlof in one button); "Cheat friends"
  spawns the scripts' test helpers SH and CH (`MarvinCheatFriendsNpcSymbols`).
- **Fall rescue** goes back to the last spot the hero stood on (`642070e5`) - Mroczne Tajemnice's abandoned mine has
  its START over the void.

**Added 2026-10-16**

- **`main` merged** (`f9b48c31`: split/02-mods, split/03-devtools). From `main`: Marvin mode toggled in game
  (`MarvinService.IsMarvinMode`; `DeveloperConfig.PreselectMarvinMode` only starts with it on - was
  `ActivateMarvinMode`), the Marvin tab as a command console (`MarvinCommandService`, actions in `MarvinCheatDomain`),
  `PrintScreen` overlay (`VRPrintScreen`), the on-screen keyboard in the VR module via `ContextTextInputService` (save
  names; our Core keyboard and `EnableSaveNameKeyboard` are gone), NVorbis from `com.gothic.core.binaries`.
- Kept from the branch: game profiles and the build keys in `GameSettings.json` (`main` has no mod source in builds
  yet - its issue #395), our `PrintScreen` registration for scripts (G1/G2 signatures, `AI_PrintScreen`).
- Our Marvin buttons missing in the console are commands now: `cheat god` (`EnableMarvinGodMode`), `goto forward`
  (`EnableMarvinBlink`, now through `TeleportPlayerTo` - held items go into the backpack), `team irdorath`
  (`MarvinFriendsNpcSymbols`), `team helpers` (`MarvinCheatFriendsNpcSymbols`). Fall rescue and the Immersion "Marvin"
  option follow `MarvinService.IsMarvinMode`.
- The prefab cache is created in the `ResourceCacheService` constructor: our player scene (profile menu) loads before
  `Init()`, and `main`'s `PrintScreen` overlay needs a prefab there.
- ⚠️ Left over: `EnableLevel5Cheat`, `EnableGuildCheat`, `EnableScavengerGuildCheat`, `EnableTimeSkip`,
  `MarvinSpawnNpcSymbol`, `MarvinGiveItems` are unused now (the console has these commands) - to be removed; two
  screen message paths (scripts: ours, Marvin: `main`'s overlay) - to be unified.

## Key commits

`bb6e7c4d`/`5959c1ad`/`8ea94468`/`aebea3c9`/`5e898826` Marvin · `e26326bb` build · `7c3afb78` project settings ·
`0d220118` ZSpy · `76f2eace` Production written out · `bc632361` NVorbis

2026-10-10/11: `73d5fb2f` profiles · `17553ec0` restart · `29fddfb8`/`e43f685c`/`b6075f2c`/`6c08c5d3`/`a85286c7` Marvin
· `bc8dd945` ending to menu

2026-10-11/13: `22b00d9a` third party notices · `d3130b8f` licenses in builds · `4d9acf18` merge `main` · `360812b6` changelog

2026-10-16: `f9b48c31` merge `main` (Marvin console, in-game Marvin mode, keyboard, NVorbis package)

## Main files

`Models/Config/DeveloperConfig.cs` (+472) · `Resources/DeveloperConfigs/Production.asset` ·
`Gothic-VR/Adapters/Marvin/MarvinTabHandler.cs` · `Gothic-VR/Editor/VRBuilderActions.cs` · `ProjectSettings/*` ·
`Scenes/Bootstrap.unity`

Config flags (2026-10-10/11): `EnableMarvinBlink`, `EnableMarvinFallRescue`, `EnableMarvinGodMode`,
`MarvinFriendsNpcSymbols`, `EnableEndingReturnsToMenu`

Config (2026-10-13): `MarvinCheatFriendsNpcSymbols` (`adf2d34e` Marvin teams)

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
- `EnableGameProfiles: true` in `GameSettings.json`: the profile list shows before the game.
- Finish G1 or G2 (or skip to the ending video): the game comes back to the menu.

- Build PCVR: `LICENSE` and `THIRD_PARTY_NOTICES.md` next to the executable.
- In game: menu, type `marvin`, close - "testmode on" and the Marvin panel; `cheat god`, `goto forward`,
  `insert ITFO_APPLE 3`, `team helpers`. Save: name typed on the keyboard.
