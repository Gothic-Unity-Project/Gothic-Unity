---
name: playtest-shortcuts
description: How Claude skips logo/intro videos and the main menu when testing Gothic-Unity in the Editor, via a local
    DeveloperConfig (SkipVideos, EnableMainMenu). Use whenever you start the game to test or reproduce something in a
    world, before using Unity MCP play mode.
---

# Playtest shortcuts

Videos and the main menu cost minutes per test run and block automated play sessions. When you test, **skip both**
(unless the task is about videos or the menu itself).

## Settings (DeveloperConfig)

| Field | Value | Effect |
|---|---|---|
| `SkipVideos` | `true` | Logo videos and every Daedalus `PlayVideo`/`PlayVideoEx` (chapter intros, G1 extro) are skipped. Default: off. |
| `PreselectGameVersion` + `GameVersion` | `true` + `Gothic1`/`Gothic2` | Skips the game version selection - without it the boot stops there, menu off or not. |
| `EnableMainMenu` | `false` | Bootstrap goes straight into a world. |
| `PreselectWorldToSpawn` | e.g. G1 `NewWorld`, G2 `NewWorld`/`Addon` | World to load when the menu is off. |
| `SpawnAtWaypoint` | waypoint / free point name | Optional spawn position. |
| `LoadFromSaveSlot` + `SaveSlotToLoad` | `true` + 1..15 | Load a save instead of a fresh world (needs `EnableMainMenu` off). |

Config is read at runtime via `GameGlobals.Config.Dev` / `ConfigService.Dev`.

## Don't commit test settings into Production.asset

`Assets/Gothic-Core/Resources/DeveloperConfigs/Production.asset` is the shipped default (menu on, videos on). Prefer
your own config:

1. `manage_scriptable_object` (UnityMCP) or Right-click > `Create > Gothic > ScriptableObjects > DeveloperConfiguration`
   in `Assets/Gothic-Core/Resources/DeveloperConfigs/`, e.g. `ClaudeTest.asset` (copy of Production works too).
2. Set `SkipVideos = true`, `EnableMainMenu = false`, plus the world fields above.
3. Assign it to `GameManager`'s **Developer Config** slot in the Bootstrap scene. This is a scene/prefab change - do it
   through UnityMCP (`manage_components`/`manage_scene`), never by hand-editing YAML, and don't commit that assignment.

If you only need a quick one-off, you may edit `Production.asset` through UnityMCP, but **revert it before
committing** (`git checkout -- Assets/Gothic-Core/Resources/DeveloperConfigs/Production.asset`). Check
`git diff --stat` for it before every commit.

Remember (see `feature-checklist`): a new field must also exist in the asset YAML, otherwise it is `false`.

## Then

Open the **Bootstrap** scene, enter play mode, and follow `unity-mcp-debug` for reading the console. Don't recompile
during play mode.
