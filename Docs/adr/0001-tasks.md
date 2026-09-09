# ADR-0001 — Task checklist

Companion to [`0001-headless-functional-test-suite.md`](./0001-headless-functional-test-suite.md).
Section references (§) point into that ADR. Where useful, items are also tagged with the delivery
iteration from D9: **V0** (harness, recorders, seeded RNG, one smoke scenario) → **V2** (the driver
vocabulary / DSL) → **Seed sweeps** → **V1** (the Editor recorder).

**Legend:** 🔴 blocker — nothing downstream works until it is done · 🟡 degrades a feature · ⚪ plain work
· ❓ needs a decision before it can be started.

Groups are listed in dependency order; within a group, items are roughly ordered too.

---

## Prerequisites — existing code (§7)

- [x] 🔴 Fix `AbstractTest.GetConfiguration()` — it loads `Resources.Load<DeveloperConfig>("GameConfigurations/Production")`
      but the asset lives at `Resources/DeveloperConfigs/Production.asset`. Returns `null`, next line throws.
- [x] 🔴 Add a `-gothicTestConfig <name>` command-line / env override, resolved in `BootstrapService.AwakeUnity()`,
      so tests select a config instead of mutating the shared `Production.asset` (§D5).
- [x] ⚪ Commit `Assets/Gothic-Core/Resources/DeveloperConfigs/FunctionalTest.asset` — simulator on, main menu off,
      NPCs on, deterministic start time, `SpeedUpLoading` on.
- [x] 🟡 Extract `FileLoggingLogger` out of the `#if !UNITY_EDITOR` block in `FileLoggingHandler.cs` into its own
      file, so the Editor lane can reuse the shipping log format (§3.3, one class per file).
- [x] 🟡 Move the `VRGameTest._alreadySetUp` guard into a shared `FunctionalTest` base class with explicit
      session semantics (§D4).
- [x] ✅ Decided: **VR + simulator**, per the ADR's recommendation — it is what ships, and Flat is incomplete.
      Encoded in `FunctionalTest.asset` (`GameControls = VR`, `EnableVRDeviceSimulator = true`). Both player scenes
      are named `Player`, so `Constants.ScenePlayer` stays correct if Flat is ever added as a second target.

## Harness — `Assets/Gothic-Testing/Runtime` (§3.1, §3.2, §3.9)

- [x] ⚪ **V0** Create `Gothic.Testing.asmdef` (references `Gothic.Core` only; gated by `GOTHIC_FUNCTIONAL_TESTS`)
      and `Gothic.Testing.Editor.asmdef`.
- [x] ⚪ **V0** `TestSession` — owns the run directory, the shared clock, `Step(name)` returning `IDisposable`.
- [x] ⚪ **V0** `InputDriver` — wraps `InputTestFixture`; press/hold/release with frame-accurate timing.
- [x] ⚪ **V0** `GameDriver` — minimal verb set for the smoke scenario: `WalkForward`, `Turn`, `WaitForScene`,
      `WaitUntil(predicate, timeout, because)`.
- [ ] ⚪ **V2** `GameDriver` — complete the verb set: `WalkBack/Strafe`, `Jump`, `Sprint`, `Crouch`, `OpenMenu`,
      `GrabWithBothHands`, `DrawWeapon`, `Attack`, `Interact`, `WaitForEvent(UnityEvent, timeout)` (§3.2, D10).
      `WaitUntil` stays the only wait primitive — no fixed-delay verb is added (D11).
- [ ] ⚪ **V2** `Teleport(waypoint)` shortcut so scenarios don't spend a minute walking (§3.2).
- [x] 🟡 **V0** `Watchdog` — fail the step cleanly when no bus event and no player movement occur for N seconds,
      instead of letting the CLI timeout kill the process and the artifacts with it (§3.6). **Downgraded from 🔴
      after implementation** — see the note below; it is a load-phase fast-fail, not the thing standing between
      us and undiagnosable failures.
- [x] ⚪ **V0** Set `Time.captureDeltaTime = 1f/30f` per session — capture pacing and CI-machine-speed
      independence, not replay determinism (§3.5).
- [x] 🔴 **V0** Seed `Random.InitState(seed)` per session and record the seed in `manifest.json` (§3.9, D13).

Four notes on what the harness commit decided, so the next item does not re-litigate them:

- **Define gate.** The asmdefs constrain on `UNITY_INCLUDE_TESTS` **and** `GOTHIC_FUNCTIONAL_TESTS` (asmdef
  constraints are ANDed). `GOTHIC_FUNCTIONAL_TESTS` was added to the Standalone and Android scripting defines,
  so the Editor lane and a Lane B player build compile the harness; `UNITY_INCLUDE_TESTS` is what actually keeps
  it out of a shipping player, and it is also required because `Unity.InputSystem.TestFramework` is test-only.
- **One reflection binding, not two.** `Watchdog` needs "did any bus event fire" and the trace needs the same
  events, so the D7 reflection lives in `GlobalEventObserver`. `TraceEventBinder` subscribes to that observer
  rather than reflecting over `GlobalEventDispatcher` a second time — binding twice doubles every trace line.
- **Timeouts are captured-time, not wall-clock.** Every wait and the watchdog read `SessionClock.ElapsedSeconds`,
  which advances one `Time.captureDeltaTime` step per frame. Timeouts therefore mean the same number of frames
  on a developer machine and on a loaded CI box. A frozen process is the CLI `--timeout`'s job, not the
  watchdog's (§3.6).
- **The watchdog only watches loading.** `GameTimeService.TimeTick()` fires `GameTimeSecondChangeCallback` every
  ~0.07s of real time, started by `WorldSceneLoaded` and stopped by `LoadingSceneLoaded`. So once a world is up
  the bus has a ~14Hz heartbeat and the watchdog can never trip; its live window is boot, pre-caching and the
  load phase, where game time is stopped and events are bursty rather than periodic. That is the failure §3.6
  actually names (a silent frame-skipped loader), but it is narrower than §3.6 reads.
  Two consequences, both deliberate:
  - **Do not blocklist the `GameTime*` events to "fix" this.** It would make the watchdog live during gameplay
    and immediately flaky: a long dialog produces no bus events at all (`AI_Output` is Daedalus, the deferred
    tier), so healthy scenarios would fail. That is the flakiness D11 exists to prevent.
  - **§3.6's CLI-timeout argument is weaker than written.** Every point where the harness yields is already
    inside a timeout-guarded wait which fails cleanly with artifacts intact, so the watchdog is a fast-fail and
    a stall timestamp, not the only thing between us and a killed process. It earns its place because this
    project's logs are noisy by design (the §3.3 baseline exists for that reason), so "an error was logged"
    does not say *which* line meant "and then it stopped" — `watchdog.stall` does.

## Interaction gate — first end-to-end scenario (§3.2)

> Runs as soon as the first driver verbs exist, before the rest of the vocabulary is built out. Its job is
> not coverage — it is to prove that simulated **hand movement and button interaction** actually drive the
> game. That is the riskiest assumption in this ADR: locomotion through the WASD path is the easy half, and
> nothing in `VRPlayerInputs` covers hands. **If this gate cannot be made to pass, stop and reassess the
> approach before building anything further.**

- [ ] 🔴 **V0** `Gate.NewGameToDiegoDialog` — one scenario, five steps: main menu appears → click a menu
      entry to start a new game → world finishes loading → look up → open dialog with Diego and select `END`.
- [ ] 🔴 **V0** `InputDriver` hand control — pose the simulated VR hands via `HVRHandsSimulator`. The
      `UseWASD` branches in `VRPlayerInputs` drive locomotion only; hands are a separate input surface.
- [ ] 🔴 **V0** `InputDriver` head control — look direction via `HVRBodySimulator`, for the "look up" step.
- [ ] 🔴 **V0** `Driver.PointAt(target)` + `Driver.Trigger(HandSide)` — the hover-then-trigger pair that both
      the main menu and `VRDialog` depend on (`UIEvents` hover enter, not a screen-space click).
- [ ] ⚪ **V0** `Driver.ClickMenuEntry(label)` — resolve a menu entry by its `TMP_Text`, point, trigger.
- [ ] ⚪ **V0** `Driver.SelectDialogOption(text)` — same mechanism against `VRDialog`'s dialog items.
- [ ] ⚪ Commit a second config (e.g. `FunctionalTestMenu.asset`) with `EnableMainMenu = true` and
      `EnableNpcs = true` — the smoke config deliberately skips the menu, and this gate needs it.
- [ ] ⚪ Assert on state, not pixels: `VRDialog.CurrentDialogOptionTexts` for what is on screen,
      `GameStateService.Dialogs.CurrentOptions` for VM state, `WorldSceneLoaded` for the load step (D12).
- [ ] ❓ Confirm the NPC and start waypoint. Diego at the Gothic 1 opening is the obvious candidate — he is
      scripted to approach the player, so the gate needs no walking to reach him.
- [ ] 🟡 Measure the gate's end-to-end duration; it sets the floor for every later scenario timeout and for
      the Lane 2 budget (§4.1).

## Actuator & observation catalogue (§3.11)

> Built in the build order §3.11 gives, which is not the order the catalogue tables list. Semantic verbs are
> implemented on top of state observations, so these are one workstream, not two.

- [ ] ⚪ **V0** Lifecycle observations — make `PlayerSceneLoaded`, `WorldSceneLoaded`, `MainMenuSceneLoaded`,
      `ZenKitBootstrapped` and `LoadGameStart` awaitable. Free: D7's reflection binding already covers them.
- [ ] 🔴 **V0** State observations, minimal — player pose, HP, active scene, loaded world; queryable every
      frame. The first `WaitUntil` in the first scenario cannot be written without it.
- [ ] ⚪ **V0** Raw and physical actuators — delivered by the interaction gate above.
- [ ] ⚪ **V2** Gameplay observations — `FightHit`, `FightWindow*`, `SetHeroAsTarget`, `LockPickCombo*`,
      `MusicZone*`, `LevelChangeTriggered`, `CreateNpc`, the `GameTime*` callbacks.
- [ ] ⚪ **V2** State observations, in full — nearby NPCs with distance / AI state / HP / hostility, inventory,
      current animation, game time. Read by `WaitUntil` predicates and by ADR-0002's trace tier.
- [ ] ⚪ **V2** Semantic actuators — resolve targets through state observations, never world coordinates.
      Enforce in review: scenarios use semantic verbs unless the physicality is what is under test.
- [ ] ⚪ **V2** Guards — continuous per-frame invariants: no unbaselined error, player Y within world bounds,
      no `NaN` in a player/NPC transform, frame time under budget. **This is where "fell through the world" is
      caught**, not the watchdog: game time keeps ticking while the player falls, so the bus heartbeat keeps the
      watchdog quiet (see the harness notes above).
- [ ] 🟡 Diagnostics observations — performance counters (frame time, GC, draw calls) into the trace, so the
      raw-input perf-regression case has something to compare against.

### Arrange actuators (§3.11)

- [ ] ⚪ **V2** `SetGameTime` / `AdvanceGameTime` on top of `GameTimeService` and
      `DeveloperConfig.StartTimeHour` — NPC daily routines are untestable without it.
- [ ] ⚪ **V2** `SetSeed`, `LoadSaveSlot`, `SpawnAt(waypoint)`, `GiveItem`, `SetHealth`.
- [ ] 🔴 **V2** Phase enforcement — arrange verbs are reachable only during the fixture's arrange phase and
      throw once the act phase has begun. This is an API gate, not a documented convention: without it a
      scenario will reach for `SetHealth` mid-act and quietly stop testing anything.

### Daedalus observations — deferred (§3.11)

> Deferred out of the initial scope by decision. Nothing before the first combat or AI-routine scenario
> needs it, and it is the most expensive item in the catalogue. Left here so the design notes survive.

- [ ] ⚪ Introduce a local `Register<…>(name, handler)` helper in `VmExternalDomain` that wraps each handler
      in a trace call, then mechanically rewrite the 142 `vm.RegisterExternal<…>` call sites.
- [ ] ⚪ Emit the high-value externals first behind a `LogCat` filter (`AI_Output`, `AI_StartState`,
      `Ai_Attack`, perception and target calls). Tracing all 142 unfiltered drowns the trace.

## Recording & artifacts (§3.3–§3.5, §3.7, §3.9, §3.10)

- [ ] ⚪ **V0** `LogRecorder` — `UberLogger.ILogger` sink writing `log.txt` and `errors.txt`.
- [ ] ⚪ **V0** Subscribe `Application.logMessageReceivedThreaded` for engine-side errors that bypass UberLogger.
- [ ] ⚪ **V0** Attach both log files to the NUnit XML via `TestContext.AddTestAttachment`.
- [ ] 🔴 **V0** Error baseline: `known-issues/functional-tests-baseline.txt`, signature normalisation, and delta
      reporting in both directions. Without this every run is red (§3.3).
- [ ] ⚪ **V0** `ScreenshotRecorder` — camera → fixed 1280×720 RenderTexture → readback at end of frame.
- [ ] ⚪ **V0** Screenshot triggers: first error per step (rate-limited), test failure in `[UnityTearDown]`, step
      boundaries, watchdog trip, explicit call.
- [ ] ⚪ **V0** `TraceRecorder` — JSONL writer with the shared sequence/frame/time header.
- [ ] ⚪ **V0** `TraceEventBinder` — reflect over `GlobalEventDispatcher`'s static `UnityEvent` fields and bind a
      listener to each (§D7).
- [ ] ⚪ **V0** Player state sampler — position, rotation, HP, walk mode, current animation every 10 fixed frames.
- [ ] ⚪ **V0** `manifest.json` — git SHA, Unity version, config name, Gothic install fingerprint, seed, machine.
      Mostly done: `Recording/RunManifest` writes all of it except the **Gothic install fingerprint**, and is
      rewritten on every session start/end so a killed run still leaves one behind.
- [ ] ⚪ **V0** `summary.md` generator — step-by-step pass/fail with inline screenshot links.
- [ ] ⚪ **V0** `FrameRingBuffer` — ~30 s at 640×360 / 10 fps in memory, flushed to PNGs on failure only (§D8).
- [ ] ⚪ **V0** ffmpeg step to turn a flushed buffer into `replay.mp4`.
- [ ] ⚪ Optional full-session capture via `com.unity.recorder` for the nightly lane.
- [ ] ⚪ Retain the raw input stream for two narrow uses only — crash reproduction and performance-regression
      frame-time comparison — now that general replay is out of scope (§3.5, D16).
- [ ] ⚪ **V1** `ScenarioRecorder` — Editor record/play/stop/save feature emitting a draft `FunctionalTest`
      subclass as V2 C# source, never a binary/opaque input blob (§3.10, D15).
- [ ] ⚪ **V1** Assertion-candidate capture — while recording, reuse `TraceEventBinder` to listen to
      `GlobalEventDispatcher` and emit observed events as commented suggestions next to the call that
      plausibly caused them, e.g. `// observed @ +0.4s: FightHit(...)` (§3.10).
- [ ] ⚪ **V1** Input→verb map — one shared table driving both `ScenarioRecorder`'s key-to-verb translation
      and `InputDriver`'s verb-to-key translation, so the two cannot drift apart (§3.10).

## Tests — `Assets/Gothic-Tests/Functional` (§3.2, §3.9)

- [ ] ⚪ `Gothic.Tests.Functional.asmdef` (`UNITY_INCLUDE_TESTS`), referencing `Gothic.Testing`.
- [ ] ⚪ `FunctionalTest` base fixture — session setup, config override, teardown artifact flush.
- [ ] ⚪ `Gate.NewGameToDiegoDialog` — already built as the interaction gate above; move it into this
      assembly once the gate passes, and keep it in the Lane 2 suite as the interaction regression.
- [ ] ⚪ **V0** `Smoke.BootAndWalk` — boot → new game → world loaded → walk 5 m → no new errors. The Lane 2 payload.
- [ ] ⚪ **V2** `MovementScenario` — walk, turn, jump, sprint, crouch; assert against waypoint positions.
- [ ] ⚪ **V2** `CombatScenario` — draw weapon, swing, assert `FightHit` fires (exercises the `DEF_OPT_FRAME` window).
- [ ] ⚪ **V2** `WorldLoadingScenario` — load each shipped world, assert no new errors and a plausible VOB count.
- [ ] ⚪ **V2** `SaveLoadScenario` — save to a slot, reload, assert player state round-trips.
- [ ] ⚪ **V2** `DialogScenario` — broaden beyond the gate: multiple topics, nested options, aborting mid-dialog.
- [ ] ⚪ **Seed sweeps** Nightly seed-sweep runner — one scenario × ~20 fixed seeds, one `manifest.json` per
      seed; a seed that fails reproduces locally from its recorded value (§3.9, D14).

## Runner — infrastructure (§3.8, §6)

- [ ] ❓ Decide who hosts the self-hosted runner, and confirm the team accepts PR-triggered jobs on it.
- [ ] 🔴 Provision the runner: Unity 6000.3.13f1, the `unity` CLI, HVR licence, Gothic 1 (and 2) installations.
- [ ] 🔴 Give the runner a graphics device — do **not** use `-nographics`. Hardware-accelerated session preferred
      (a real desktop session on Windows, a real X session on Linux); software rasterisation is the fallback
      for the smoke lane only (§3.8).
- [ ] 🟡 On a non-FHS Linux distribution, wrap the Editor and the `unity` binary (FHS env or dynamic-loader shim).
- [ ] 🔴 Verify a Gothic installation loads from a case-sensitive filesystem; if not, mount it case-insensitively.
      Test this on day one — cheap now, expensive later.
- [ ] ⚪ If the runner is containerised, pass `/dev/dri` through, or it silently drops to software rendering.
- [ ] ⚪ Unity licence activation on a headless machine (`unity auth login` or the existing CI secrets).
- [ ] 🔴 Security: same-repo head guard, maintainer-applied label, no `pull_request_target`, ephemeral job isolation.
- [ ] ⚪ Runner labels agreed and applied (`gothic-data`, `rendering`, …) so `runs-on` stays declarative.
- [ ] ⚪ Persist and key the static cache (`{persistentDataPath}/Cache/{GameVersion}/`) between runs.

## CI — workflows (§4)

- [ ] ⚪ Lane 1: extend the existing PR workflow with EditMode tests via `unity test --mode EditMode`.
- [ ] ⚪ Lane 2: `functional_test.yml` — smoke suite on PR to `main` or on label, artifacts uploaded on `always()`.
- [ ] ⚪ Lane 3: nightly full suite, `--shard`, Gothic 1 and 2, video enabled, plus the seed-sweep pass (§3.9).
- [ ] ⚪ Lane 4: Lane B standalone-player run via `unity run -- -runTests -testPlatform …` at release time.
- [ ] ⚪ Add a `StandaloneLinux64` build method to `VRBuilderActions` if Lane B is to run on Linux.
- [ ] ⚪ Map CLI exit codes to job status (0 pass / 8 failed / 6 no verdict / 2 usage) and stop parsing logs.
- [ ] ⚪ Publish the JUnit report as a PR check; keep the artifact bundle for 14 days.
- [ ] ⚪ Evaluate replacing `game-ci/unity-builder` with `unity build` in the existing build workflow.

## Docs

- [x] ⚪ Fix the stale `GameConfigurations/` path in `CLAUDE.md` (it is `DeveloperConfigs/`).
- [ ] ⚪ Correct `com.gothic.core.binaries/Dependencies/README.md` — it still calls the Linux `.so` "not used",
      which the enabled `linux_x86/` plugin settings contradict.
- [ ] ⚪ Fix the `Hlp_Random` off-by-one in `VmExternalDomain.cs:506` — `Random.Range(0, n0 - 1)` should be
      `Random.Range(0, n0)`; the top option of every random Daedalus choice currently never fires. A real bug,
      unrelated to this ADR, found while writing §3.9.
- [ ] ⚪ Cross-link ADR-0002 (AI-assisted review of these artifacts) from this ADR's "Related" line, and from
      ADR-0002 back to this one.
- [ ] ⚪ Wiki → *Resources & Tools → Build Pipelines*: link this ADR and describe the four lanes.
- [ ] ⚪ Wiki → *Get Started → Developer Setup*: how to run a functional scenario locally in the Test Runner.
- [ ] ⚪ Write down how to read a failure bundle: `summary.md` first, then `errors.txt`, then `trace.jsonl`.
- [ ] ⚪ Document the error-baseline workflow — when to add a signature, when to remove one.
- [ ] ⚪ Set this ADR's status to Accepted once the runner decision is made.
