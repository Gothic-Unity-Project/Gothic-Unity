# Behaviour scenarios

The game's rules written as Given/When/Then scenarios and checked by the machine, inside the editor, in the running
game, without a headset. The decision behind it: `Docs/adr/2026-11-01-vr-behaviour-tests.md`.

Everything lives in `Assets/Gothic-Tests/Behaviour/` (assembly `Gothic.Tests.Behaviour`, Play Mode; its EditMode
checks and the menu in `Gothic.Tests.Behaviour.Editor`):

| Folder | What |
|---|---|
| `Features/<area>/<topic>.feature` | the scenarios, one file per decision record, named like it |
| `Steps/<Area>/` | the sentences (`[Step]` methods), by Core context: World, Player, Vobs, Npc, Dialog, Lighting, SaveGames, Common |
| `Driver/` | what steps share: putting the hero somewhere, grabbing, spawning, the keyboard and mouse, waiting, probes, the log |
| `Session/` | the one running game (`GameSession`), its config, backups, the per-scenario undo list |
| `Runner/` | parsing (the official Gherkin parser, `Plugins/`), the step registry, the one NUnit test per scenario |
| `Report/` | the run's report |

## Writing a scenario

```gherkin
@g1 @vr @area(lighting) @adr(2026-11-01-torch-hero-light)
Feature: A torch in the hand lights the world like a flame
  The burning torch in the player's hand is the one sharp light. Off by default.

  Background:
    Given the world "world.zen" is loaded
    And the hero stands at waypoint "OCR_HUT_1"
    And it is 23:00

  Scenario: With the option on, the lit torch in the hand is the hero light
    Given the Gothic.ini option "torchHeroLight" is on
    And the hero holds a "ITLSTORCH" in his right hand
    When the hero lights the torch in his right hand
    Then the hero light is on
    And the torch's own light is off
```

- Sentences come from the step catalog below. The keyword doesn't matter for binding (`And` is a step like any
  other). Names are the game's: Daedalus instances (`ITLSTORCH`, `GRD_283_Gardist`), way points, Gothic.ini keys,
  `DeveloperConfig` field names, AI states (`ZS_CATCHTHIEF`).
- Every feature starts its Background with `the world "..." is loaded`; scenarios are sorted by world, so a run loads
  each world once. Set the time of day in the Background. Every scenario starts with `UnityEngine.Random` seeded by
  its name; `the dice are seeded with <n>` makes a scenario's dependence on the dice visible.
- Bring your own NPC: `"<instance>" stands 3 m in front of the hero` spawns an instance the world doesn't place (the
  driver refuses others and lists candidates) and removes it afterwards. Don't rely on where a routine put somebody.
- Scenario Outlines work; an example row becomes its own test named `<scenario> - <column> <value>, ...`. Data tables
  and doc strings parse but no step reads them yet.
- What can't be automated (feel, comfort, haptics) is a scenario too, tagged `@manual`, written freely: it never runs
  and goes onto the headset checklist.
- English only.

### A new sentence

A public method with `[Step("regex")]` on a class in `Steps/<Area>/`. The regex matches the whole step text; its
groups become the parameters (`string`, `int`, `float`, `bool` - the first word of `(on|off)`, `(is|is not)`, ... is
true -, enums like `VrHandSide`). Return `void` or `IEnumerator` to wait across frames. Step classes get services with
`[Inject]` (after the game is ready) and the scenario's `ScenarioScope` through their constructor:

- `scope.Undo(description, action)` for every change: a held item released, a spawned NPC removed, a setting put back.
  The undos run in reverse after the scenario, pass or fail - there is no central reset. `scope.UndoOverFrames` for
  undos that need frames (letting go of a key-held grip); they run first.
- `scope.Driver<T>()` for the shared drivers (`PlaceDriver`, `HandDriver`, `NpcDriver`, `InputDriver`), one per
  scenario.
- `Wait.Until(condition, seconds, expected, describe)` instead of fixed sleeps; the failure says what was expected and
  what was there. 10 s by default, 8 s for an NPC's reaction to a perception (perception time 5 s).
- A scenario may take 120 s (`@timeout(n)` changes it); booting or loading a world doesn't count.

**Drivers translate, the game decides.** A step calls the game's services, VR ports and VR components' events, and a
Then-step reads observable state or a read-only query of the service that owns it. No child object names, no
reflection, no copied formulas or numbers from the game - if a check needs something the game doesn't offer, add a
small service method with a product reason. Nothing under `Gothic-VR/Scripts/Interaction/Backends/`; the one use of
XR Interaction Toolkit types is the input session registering its simulated devices.

`FeatureCatalogTest` (EditMode, no game start) fails when a feature file doesn't parse, a step of an automated
scenario binds to no or to several `[Step]` methods, a `[Step]` is used nowhere (or mark it `IsReserved = true` with a
reason), a feature has no `@area`, an `@adr` names no record, two scenarios share a name, or the catalog below is out
of date. Run the EditMode tests before starting the game.

## Tags

| Tag | Meaning |
|---|---|
| `@g1`, `@g2` | for that game version only (none: both). A run has one version - Production's `GameVersion`; the other's scenarios are skipped and go onto the checklist |
| `@vr`, `@flat` | platform. `@flat` alone is skipped until the Flat module runs |
| `@area(<context>)` | required: the Core context, category `area:<context>` for a per-area run |
| `@adr(<record>)` | the decision record the feature checks; the report links it |
| `@input` | plays the desktop VR simulator with a test keyboard and mouse; runs only in an input session (below) |
| `@visual` | samples pixels (`Probe.Brightness`); the probe images go into the report |
| `@slow` | saves and loads, or changes the world; sorted last |
| `@fresh-world` | sorted last; the world reload before it isn't built yet (no scenario needed it) |
| `@manual` | never runs, goes onto the headset checklist |
| `@known-bug` | the scenario shows a bug that is known and not fixed. A failure is inconclusive for the Test Runner (the run stays green) and listed apart in the report; a pass is reported as "known bug gone?" - remove the tag. Say which bug in a comment above it |
| `@timeout(<s>)` | the scenario's time limit instead of 120 s |

Tags become Test Runner categories; a tag with a value becomes `name:value` (`@area(npc)` is `area:npc`).

## Sessions

The first scenario of a play session boots the game like the editor does: the Bootstrap scene with a copy of
`DeveloperConfigs/Production` made in code (straight into the world, VR with the desktop simulator, everything
spawned) handed in through `BootstrapAdapter.ConfigOverride`. The session is ready when the world is loaded and the
surroundings of the spawn are built (6-30 s). The scenarios after it play in the same world; another world is loaded as
a new game.

An **input session** is a play session the input system of which belongs to the test: the Input System's
`InputTestFixture` replaces every device with a test keyboard and mouse before the game boots, and registers XR
Interaction Toolkit's simulated headset and controller layouts again (the fixture's fresh input system lacks them).
The simulator reads the test keyboard and mouse like real ones; physical input doesn't reach the game meanwhile. The
first scenario decides the kind of session: an `@input` scenario boots an input session, a plain one a plain session.
An `@input` scenario in a plain session is skipped (the fixture can't be set up under a running game), so input
scenarios run on their own: category `input`, or the menu's input run.

Disk state a run changes is backed up when the game is ready and restored at the end of the run, and at the next
start if a run crashed: the developer's Gothic.ini (the Gothic.ini steps write the real file) and the save slots
**14 and 15**, which are reserved for the scenarios (`the game is saved and loaded again` uses 15). The backup sits in
`Logs/BehaviourTests/backup/` with a manifest written last; without the manifest nothing is touched.

## Running

Before each Play Mode run, let the editor compile (refresh): an editor set to enter play mode without a domain reload
keeps the game's static event listeners from the last play session, and the third session in a row hangs at boot (a
`VRContextInteractionService` listener hits a destroyed camera - a game bug, not the runner's). The menu runs do this
by themselves.

- **Test Runner window**, Play Mode tab: filter by name (`torch-hero-light /`) or category (`area:npc`, `input`, ...).
- **Menu** `Gothic > Tests`: `Run behaviour scenarios (fast)` (everything but `input`, `visual`, `slow`, `manual`),
  `(input)` (the input session), `(all)` (fast, then input in a second play session), `Write manual checklist` (no game
  start; the checklist of a fast run of Production's version into `Logs/BehaviourTests/checklist.md`), `Write step
  catalog` (into this file), `Open last report` (writes the report from `results.jsonl` first if the run crashed).
  Each run reloads the scripts first.
- **Unity MCP**: `run_tests(mode="PlayMode", assembly_names=["Gothic.Tests.Behaviour"], category_names=[...],
  init_timeout=120000)`, then `get_test_job(job_id, wait_timeout=60)`. Categories are regular expressions and `!`
  excludes: the fast run is `["!^input$", "!^visual$", "!^slow$", "!^manual$"]`, the input session `["^input$"]`. The
  totals don't count inconclusive results (known bugs among them) - read `report.md`. With `include_failed_tests` the
  answer is ~300 KB; save it and read it with `jq`. Never recompile while a run is in play mode.
- **Command line**: not tried yet. It needs the Hub editor closed - two editors on one `Library` folder broke the
  compile list once. The command to try (no `-nographics`: the probes render; no `-quit`):

  ```
  systemd-run --user --scope -p MemoryMax=12G ~/Unity/Hub/Editor/6000.3.13f1/Editor/Unity \
    -projectPath /home/todi/Gothic-Unity -batchmode -runTests -testPlatform PlayMode \
    -assemblyNames Gothic.Tests.Behaviour -testCategory "!^input$;!^manual$" \
    -testResults Logs/BehaviourTests/last.xml -logFile Logs/BehaviourTests/last.log
  ```

  The open question is whether the XR plug-ins start cleanly in batch mode without a headset.

Times (G1, `world.zen` pre-cached, this machine): boot 6 s, most scenarios 2-5 s, an NPC reaction 9 s (the 8 s
wait), the save and load scenario 15-30 s. The fast run of the first features takes about 2.5 minutes, the input
session half a minute. Gothic 2 is a second run with a Gothic 2 Production config.

The daily path: a feature's or an area's category while working on it (under 2 minutes), the fast run before a PR,
the full run including `input`, `visual` and `slow` when something big changed.

## Reports

Each run (one play session) writes `Logs/BehaviourTests/<yyyy-mm-dd-HHmm>/` (`Logs/` is git-ignored):

- `results.jsonl`: one line per scenario, written as it ends, so a crash loses at most the running one. Fields:
  `Test`, `FeatureFile`, `Feature`, `Scenario`, `Line`, `Tags`, `World`, `GameVersion`, `StartedAt`, `Status`
  (`passed`, `failed`, `known-bug`, `inconclusive`, `skipped`, `aborted`), `IsKnownBug`, `FailingStep`, `Message`,
  `Seconds`, `LoadSeconds`, `MemoryBefore`/`MemoryAfter` (`Profiler.GetTotalAllocatedMemoryLong`), `Images`,
  `WarningCount`, `ErrorCount`, `LastWarnings`. The report and a dev-site page read this format.
- `shots/`: a screenshot of the game view when a scenario fails, before its undo. `probes/`: the images the brightness
  probe looked at.
- `report.md`: a table per feature (passed, failed, known bug, inconclusive, skipped, time), a "Needs a look" list
  (failures first, then known bugs that are gone, inconclusive ones, known bugs still there, skipped), and per feature
  every scenario with its status, time, memory change and warning count; for each one that didn't pass: the step, the
  message, the screenshot and the last warnings and errors it logged.
- `checklist.md`: the headset checklist for the features of the run - every `@manual` scenario and every one skipped
  for its tag (`@input` without an input session, the other game version, `@flat`), as Given/When/Then lines with
  the way point, time, Gothic.ini options and developer switches to set up and the Marvin commands to get there.

## Step catalog

<!-- step catalog: written by Gothic > Tests > Write step catalog - don't edit by hand -->

Placeholders: `<text>` any text (in quotes in the step), `<n>` a whole number, `<number>` a number like 1.5,
`(a|b)` one of the words. Keywords (Given, When, Then, And, But) don't matter.

#### Common

| Step | Method |
|---|---|
| `the log contains "<text>" exactly <n> time(s) within <n> s` | LogSteps.TheLogContainsExactlyWithin |
| `the log contains "<text>" within <n> s` | LogSteps.TheLogContainsWithin |

#### Dialog

| Step | Method |
|---|---|
| `"<text>" starts a dialog with the hero` | DialogSteps.StartsADialogWithTheHero |
| `a dialog is (open\|not open)` | DialogSteps.ADialogIs |
| `the hero ends the dialog` | DialogSteps.TheHeroEndsTheDialog |

#### Lighting

| Step | Method |
|---|---|
| `the ground <number> m in front of the hero is at least <n>% brighter than "<text>"` | TorchSteps.TheGroundIsBrighterThan |
| `the ground <number> m in front of the hero is remembered as "<text>"` | TorchSteps.TheGroundIsRememberedAs |
| `the hero light is (on\|off)` | TorchSteps.TheHeroLightIs |
| `the hero light is (on\|off) within <n> s` | TorchSteps.TheHeroLightIsWithin |
| `the hero lights the torch in his (left\|right) hand` | TorchSteps.TheHeroLightsTheTorch |
| `the hero puts out the torch in his (left\|right) hand` | TorchSteps.TheHeroPutsOutTheTorch |
| `the torch in his (left\|right) hand is (burning\|out\|burnt)` | TorchSteps.TheTorchInHisHandIs |
| `the torch in his (left\|right) hand is the hero torch` | TorchSteps.TheTorchInHisHandIsTheHeroTorch |
| `the torch's own light is (on\|off)` | TorchSteps.TheTorchsOwnLightIs |

#### Npc

| Step | Method |
|---|---|
| `"<text>" guards the items around him` | NpcSteps.GuardsTheItemsAroundHim |
| `"<text>" is <number> m from the hero, give or take <number> m` | NpcSteps.IsMetersFromTheHero |
| `"<text>" is built where he was put, give or take <n> cm` | NpcSteps.IsBuiltWhereHeWasPut |
| `"<text>" is in state "<text>" within <n> s` | NpcSteps.IsInStateWithin |
| `"<text>" is not in state "<text>" for <n> s` | NpcSteps.IsNotInStateFor |
| `"<text>" stands <number> m in front of the hero` | NpcSteps.StandsInFrontOfTheHero |

#### Player

| Step | Method |
|---|---|
| `the hero (can\|cannot) pick locks` | HeroSteps.TheHeroCanPickLocks |
| `the hero (is\|is not) able to walk` | InputSteps.TheHeroIsAbleToWalk |
| `the hero crouches` | InputSteps.TheHeroCrouches |
| `the hero has walked at least <number> m forward` | InputSteps.TheHeroHasWalkedForward |
| `the hero holds a/an "<text>" in his (left\|right) hand` | HandSteps.TheHeroHolds |
| `the hero lets go of the item in his (left\|right) hand` | HandSteps.TheHeroLetsGo |
| `the hero pulls the trigger of his (left\|right) hand` | InputSteps.TheHeroPullsTheTrigger |
| `the hero walks forward for <number> s` | InputSteps.TheHeroWalksForward |
| `the hero's (strength\|dexterity\|hit points\|mana) is <n>` | HeroSteps.TheHerosAttributeIs |
| `the hero's talent "<text>" has skill <n>` | HeroSteps.TheHerosTalentHasSkill |
| `the hero's talent "<text>" has value <n>` | HeroSteps.TheHerosTalentHasValue |

#### SaveGames

| Step | Method |
|---|---|
| `the game is saved and loaded again` | SaveGameSteps.TheGameIsSavedAndLoadedAgain |

#### Vobs

| Step | Method |
|---|---|
| `a/an "<text>" lies <number> m in front of the hero` | ItemSteps.ItemLiesInFrontOfTheHero |
| `every "<text>" fire within <n> m is (burning\|out)` | FireSteps.EveryFireIs |
| `every "<text>" fire within <n> m is (burning\|out) within <n> s` | FireSteps.EveryFireIsWithin |
| `the "<text>" in front of the hero belongs to "<text>"` | ItemSteps.TheItemBelongsTo |
| `the flame of every fire within <n> m sits within <n> cm of its slot` | FireSteps.TheFlameOfEveryFireSitsOnItsSlot |
| `the hero grips the "<text>" lying in front of him with his (left\|right) hand` | ItemSteps.TheHeroGripsTheItemLyingInFrontOfHim |
| `the hero takes a/an "<text>" out of his backpack` | ItemSteps.TheHeroTakesAnItemOutOfHisBackpack |
| `the hero takes the "<text>" lying in front of him` | ItemSteps.TheHeroTakesTheItemLyingInFrontOfHim |
| `the lock pick breaks on <n>% of <n> wrong turns, give or take <n>` | LockPickSteps.TheLockPickBreaksOn |

#### World

| Step | Method |
|---|---|
| `<n> game minute(s) pass(es)` | WorldSteps.GameMinutesPass |
| `it is <hh>:<mm>` | WorldSteps.ItIs |
| `the Gothic.ini option "<text>" is (on\|off\|<n>)` | SettingsSteps.TheGothicIniOptionIs |
| `the developer switch "<text>" is (on\|off)` | SettingsSteps.TheDeveloperSwitchIs |
| `the dice are seeded with <n>` | WorldSteps.TheDiceAreSeededWith |
| `the hero stands at waypoint "<text>"` | WorldSteps.TheHeroStandsAtWayPoint |
| `the world "<text>" is loaded` | WorldSteps.TheWorldIsLoaded |

<!-- end of step catalog -->

## Maintenance

- After adding or changing a `[Step]`, run `Gothic > Tests > Write step catalog`; `FeatureCatalogTest` fails until the
  catalog above matches the code.
- The Gherkin parser (`Gherkin` 42.0.1 and `Cucumber.Messages` 34.2.0 from NuGet, MIT, Editor only) sits in
  `Plugins/` under a `.gitignore` exception; not decided yet: keep them in the repository, or fetch them from NuGet
  on demand like the other packages.
- Not built yet: the world reload of `@fresh-world`, a `a screenshot "..." is taken` step, the command-line run (see
  Running), a Gothic 2 run from the menu.
