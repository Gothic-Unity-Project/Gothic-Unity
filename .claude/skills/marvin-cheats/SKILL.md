---
name: marvin-cheats
description: How the in-game Marvin (cheat/debug) menu works in Gothic-Unity and how to decide whether a new setting
    belongs there instead of DeveloperConfig / GameSettings.json. Use before adding a new debug toggle, tunable
    parameter or cheat, or when asked to extend the Marvin menu.
---

# Marvin / cheats

Marvin is the in-game debug menu (name from Gothic's own marvin mode). Before adding a parameter to
`DeveloperConfig`, check first whether it is rather an **in-game Marvin setting**.

## How Marvin works

- Lives in `Gothic-VR/Scripts/Adapters/Marvin/` (VR only). Flat mode has no equivalent as of now.
- `MarvinRootHandler` shows three tabs: **Logs** (`MarvinLogsTabHandler`, UberLogger view with severity/category
  filter), **Marvin** (`MarvinTabHandler`) and **Inspector** (`MarvinInspectorTabHandler`).
- The whole menu is only active while `MarvinService.IsMarvinMode` is true. Every player can toggle it in game
  (no flag needed): menu open → type `marvin`/`42` or click the Gothic-Unity logo 3 times → menu close
  (`MarvinService.OnMenuClosed`, event `GlobalEventDispatcher.MarvinModeChanged`).
  `DeveloperConfig.ActivateMarvinMode` is only a developer shortcut to start with it on (off in `Production.asset`).
- Inspector flow: "Choose VOB" sets `MarvinService.IsMarvinSelectionMode` and the next hand grab stores the object
  in `MarvinService.MarvinSelectionGO`; "Select Hero" picks the player controller directly. The tab then calls
  `GetComponentsInChildren<IMarvinPropertyCollector>()` on that object and renders what they return.
- To expose values, a MonoBehaviour implements `IMarvinPropertyCollector` (`Gothic.Core.Models.Marvin`, so Core
  adapters can use it too) and returns `MarvinPropertyHeader` (section title) and
  `MarvinProperty<T>(name, getter, setter, min, max)` entries. Supported: `bool`, `int`, `float`.
  Example: `VRPlayerWeaponInteraction.CollectMarvinInspectorProperties`.
- `MarvinService` (Core) only holds the selection state.
- **Marvin tab = command console** (like the original F2 console, typed on the `VirtualKeyboard`). Grammar in
  `MarvinCommandService.BuildCommands()`: `root.Add("goto").Add("waypoint").Add(value).Runs(values => ...)`,
  `.Describe()`, `.Only(GameVersion...)`. Values: `MarvinClosedSetValue` (waypoints, instances, guilds - lazy, via
  `MarvinPrefixIndex`) or `MarvinNumberValue`. A node with `Runs()` and further children = optional value.
  The actions live in `MarvinCheatDomain`. Only commands in the grammar can be typed, so add a command only
  together with its action. Document new commands in the wiki page `Player/Cheats-and-Marvin-Mode.md`.

## Marvin or DeveloperConfig?

| Fits Marvin (runtime) | Fits DeveloperConfig / GameSettings.json |
|---|---|
| Tuning value you want to feel out in VR (forces, velocities, thresholds) | Needed before or during boot (paths, mod, game version, log level) |
| Debug switch you flip right before a scenario (e.g. ZSpy logs) | Editor-only helpers, spawn/skip options |
| Attached to one object type (weapon, NPC, hero) | Anything read once at registration/startup |

Caveats:
- Values cached at startup (e.g. `VmExternalDomain.RegisterExternals`) must be re-read live, otherwise the toggle
  does nothing.
- Global settings have no "selected object" - they need a persistent collector or an entry in the Marvin tab.
- Keep the Inspector value as the start value; Marvin only changes it for the current runtime.

## Reference: original Gothic cheats and tools

- Console commands: https://wiki.worldofgothic.de/doku.php?id=diverses:konsolen-befehle
- Cheats overview: https://www.worldofgothic.de/gothic/gothic_cheats.htm
- Modding guide/tutorial: https://mondgesaenge.de/MDB/guide_tutorial.htm
- zSpy (script debug output): https://gothic-modding-community.github.io/gmc/zengin/tools/zSpy/

**There may be more settings than listed here.** Tell the user to check OpenGothic and the original Gothic 1/2
(marvin mode, console, Gothic.ini) for further candidates before inventing a new parameter.
