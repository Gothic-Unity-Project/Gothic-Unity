# ADR-0001: Headless functional test suite (CI playthrough tests)

- **Status:** Proposed
- **Date:** 2026-09-03
- **Deciders:** Gothic Unity maintainers
- **Supersedes / Superseded by:** —
- **Related:** [ADR-0002](./0002-ai-assisted-run-review.md) covers AI-assisted review of the artifacts this
  ADR produces (logs, screenshots, traces) — out of scope here.

---

## 1. Context

### 1.1 Goal

Run the actual game — boot, load a world, walk, fight, interact — unattended in CI, and get back
artifacts good enough to debug a failure without reproducing it locally:

1. **Error logs** stored per test run.
2. **Screenshots** captured at the moment an error occurs.
3. **A trace stream** of everything the test did, readable as a timeline to see where it went wrong.

### 1.2 What already exists

| Piece | Location | State |
|---|---|---|
| PlayMode test module | `Assets/Gothic-Tests/PlayMode/` (`Gothic.Tests.PlayMode.asmdef`) | Exists, 2 tests, effectively unused |
| Session-style fixture | `VRGameTest.cs` — boot once, ordered tests reuse the session | Right idea, needs hardening |
| Input simulation | `AbstractTest.cs` — `InputTestFixture` + synthetic `Keyboard`/`Mouse` | Works, very thin |
| VR simulator | `com.hurricanevrextensions.simulator` (runtime asmdef) + `DeveloperConfig.EnableVRDeviceSimulator` | **This is the enabler** |
| Keyboard control surface | `Gothic-VR/Scripts/Adapters/HVROverrides/VRPlayerInputs.cs` (`UseWASD` branches) | Move/jump/sprint/crouch/menu/grip all keyboard-reachable |
| Central event bus | `Gothic-Core/Scripts/GlobalEventDispatcher.cs` — "all! events at runtime for easy seekability" | **This is the trace source** |
| File log sink | `Gothic-Core/Scripts/Logging/FileLoggingHandler.cs` | `#if !UNITY_EDITOR` — **off in the Editor** |
| Screenshot precedent | `SaveGameService.cs:261` `ScreenCapture.CaptureScreenshotAsTexture(BothEyes)` | Reusable pattern |
| Static cache | `StaticCacheService.cs:198` → `{persistentDataPath}/Cache/{GameVersion}/` | Cacheable between CI runs |
| Build entry points | `Gothic-VR/Editor/VRBuilderActions.cs` | Used by `game-ci/unity-builder` today |

The critical insight is that **the HVR simulator removes XR from the equation**. `VRPlayerInputs.UseWASD`
routes every player action through `Keyboard.current`, so a CI run needs no headset, no OpenXR runtime and
no mock XR device — only a synthetic Input System keyboard. That is what makes this ADR feasible at all.

### 1.3 Constraints

- **Unity 6000.3.13f1** (Unity 6.3). Relevant because the new standalone Unity CLI targets Unity 6.0 LTS+.
- **No game assets in the repo.** Everything is loaded at runtime from a local Gothic 1/2 installation
  (`JsonRootLoader`, `GameSettings.json` → `Gothic1Path`). A CI machine without a Gothic install cannot
  boot the game past `GameVersionScene`. Gothic data is copyrighted and cannot be committed or published.
- **HurricaneVR is a paid asset**, injected from the private `asset-dependencies` repo (already solved in
  `test_and_build.yml`).
- **Boot is slow.** ~10 s with a warm static cache, ~45 s without. Per-test boots are unaffordable.
- **Loading is frame-skipped async**, not threaded. A stall shows up as "nothing happens forever", not as
  an exception.

### 1.4 Does Unity support this headlessly?

Yes, with one important caveat, and the tooling changed recently.

**Unity CLI** (standalone `unity` binary, released 2026-07-20; the experimental `com.unity.pipeline`
package requires Unity 6.0 LTS or newer). It replaces the `-batchmode -executeMethod` + log-scraping dance:

```
unity test <project> --mode PlayMode --output test-results.xml \
    --report-format nunit,junit --junit-output junit.xml \
    --filter "Gothic.Tests.Functional.*" --retries 1 --timeout 3600 \
    --format github -- <extra unity args>
```

- Exit codes: `0` pass, `8` tests ran and failed, `6` no verdict (compile error, crash, license, timeout),
  `2` usage error. This alone removes the log-parsing hack from CI.
- `--shard N/M` splits the suite deterministically; `--rerun-failed` re-runs the previous failures.
- `--format github` emits GitHub Actions annotations.
- `unity run <project> -- <args>` launches batch mode automatically (do **not** pass `-batchmode`/`-quit`,
  they are reserved) and forwards everything after `--`. This is the escape hatch for standalone-player test
  runs, which `unity test --mode` does not cover.
- `unity build --target … --output-path … --execute-method Gothic.VR.Editor.VRBuilderActions.PerformWindows64Build`
  can eventually replace `game-ci/unity-builder`.

**The caveat: `-nographics` is not compatible with goals 2 and 3.** Headless mode explicitly "runs
applications in batchmode without initializing the graphics device". No graphics device means no rendering,
which means no screenshots, no video, and no visibility into the entire URP/rendering class of bugs — which
for a Gothic port is a large share of them.

So the target is **unattended, not `-nographics`**: `-batchmode` **with** graphics initialised, on a machine
that has a real GPU and an interactive desktop session (Windows), or Xvfb + Mesa (Linux, software raster —
far too slow for a Gothic world).

---

## 2. Decision

**D1. Two lanes, one harness.** Introduce a runtime "functional test harness" with no editor dependencies,
plus thin bootstraps for each execution lane:
- **Lane A (now):** PlayMode tests in the Editor, driven by `unity test --mode PlayMode`.
- **Lane B (later):** the same scenarios compiled into a standalone player and run via
  `unity run … -- -runTests -testPlatform StandaloneWindows64`.

Start with A (the existing tests already work this way and Editor-only tooling such as Unity Recorder is
available); design every harness class so B needs no rewrite — only the bootstrap differs.

**D2. Drive the game through the HVR simulator keyboard surface**, not through XR device emulation. Keep
`InputTestFixture` as the injection mechanism, wrapped in a semantic driver API (`WalkForward(3f)`,
`Attack()`), never raw key codes in scenarios.

**D3. Run functional lanes on a self-hosted runner that owns a Gothic installation and can render.**
Linux and Windows are both viable; the project's native dependencies are already wired for the Linux Editor.
What the runner must provide is a *graphics device* — a dedicated GPU is optional, an integrated one is
enough, and software rasterisation is a workable fallback (§3.8). GitHub-hosted runners keep the existing
compile/EditMode lane.

**D4. Sessions, not tests, are the unit of isolation.** One fixture = one boot. Ordered steps within it.
A failed step marks the rest of the session inconclusive rather than running them against corrupt state.

**D5. Test configuration is injected, never mutated.** Add a `-gothicTestConfig <name>` command-line /
env override resolved in `BootstrapService.AwakeUnity()`; ship a committed
`Resources/DeveloperConfigs/FunctionalTest.asset`. Today's tests mutate the shared `Production` asset,
which dirties a tracked file on developer machines.

**D6. Diagnostics are three cooperating recorders** writing into one run directory with one shared clock:
`LogRecorder`, `ScreenshotRecorder`, `TraceRecorder`.

**D7. The trace is auto-wired from `GlobalEventDispatcher` by reflection.** Every `static readonly
UnityEvent…` field on that class gets a listener at harness start. No per-event code, and the trace stays
correct as events are added.

**D8. Failure evidence is a "flight recorder", not a full video.** A rolling in-memory ring buffer of
low-resolution frames is flushed to disk only when a test fails, plus a full-resolution still at the
failure frame. Full-session video is opt-in for the nightly lane.

**D9. The suite is delivered in named iterations, not in build-phase order.** **V0** (harness, the three
recorders, seeded RNG, one hand-written smoke scenario) proves the pipeline first. **V2** (the driver
vocabulary, §3.2) is the load-bearing tier and is built second — but it is *designed* before V0 is written,
because both V0's own smoke scenario and the V1 recorder consume its verbs. **Seed sweeps** (§3.9) are
near-free once V2 exists. **V1** (the Editor recorder, §3.10) comes last, because it emits V2 source and so
needs the vocabulary to already be stable. The out-of-order numbering is intentional: the V-labels name
*what* each tier is, not the order it ships in (§8).

**D10. The V2 DSL is plain C#, not a string- or fluent-parsed mini-language.** `Driver.Attack(HandSide.Right)`
is a method call, not a token the harness parses at runtime. That buys compile-time checking, IDE navigation
and rename-refactoring, and removes a parser from the maintenance surface entirely. A fluent call style
(§3.2) is fine as an aesthetic choice — it is still just methods.

**D11. Tolerant waits are the only wait.** Fixed sleeps (a `wait(2)`-style verb) are not part of the
vocabulary: they pass locally and flake on a loaded CI box, or silently pass for the wrong reason once an
animation runs 300 ms longer than it did when the scenario was written. Every wait in every scenario is
`Driver.WaitUntil(condition, timeout, because:)` (§3.2).

**D12. Assertions target outcomes and invariants, never trajectories.** Scenarios assert over
`GlobalEventDispatcher` events and Daedalus VM state — never pixel content or exact coordinates. Not "the
monster ended at x = 12.4" but "the monster closed to within 2 m and landed a hit within 8 seconds": true
for either dodge direction, and it survives a retuned dodge distance. This is what lets the suite absorb
intentional gameplay changes instead of breaking on them.

**D13. Seed the RNG per session in V0; the VM's own stream is deferred.** `Random.InitState(seed)` once per
session, recorded in the manifest (§3.9) — that is the whole V0 change. It buys failure reproducibility, not
passing assertions (D12 already handles that). A known cross-talk leak between the gameplay stream and
unrelated frame-timed draws is documented but deliberately not fixed yet (§3.9).

**D14. Seed sweeps are the cheap combinatorial-coverage tier.** ~20 seeds nightly per scenario (§3.9);
exhaust this before reaching for AI-assisted review of a run (ADR-0002).

**D15. The V1 recorder emits V2 source, never a binary input blob**, plus commented assertion candidates
drawn from observed `GlobalEventDispatcher` events, via a shared input→verb map with `InputDriver` (§3.10).

**D16. Replay is out of scope.** `TraceReplayer`, `Replay/`, `TraceReader` and the "best-effort convergence"
caveat they required are cut entirely; the trace now needs transcription fidelity, not replay fidelity
(§3.5, §3.10).

---

## 3. Detailed design

### 3.1 Assemblies

```
Assets/Gothic-Testing/                       (new)
  Runtime/  Gothic.Testing.asmdef            no editor refs, define-gated GOTHIC_FUNCTIONAL_TESTS
            Harness/    TestSession, GameDriver, InputDriver, Watchdog
            Recording/  LogRecorder, ScreenshotRecorder, TraceRecorder, TraceEvent, FrameRingBuffer
  Editor/   Gothic.Testing.Editor.asmdef     Unity Recorder wiring, EnterPlaymode bootstrap,
                                              ScenarioRecorder (V1, §3.10), InputVerbMap

Assets/Gothic-Tests/
  Functional/ Gothic.Tests.Functional.asmdef  UNITY_INCLUDE_TESTS; scenarios only
  PlayMode/   Gothic.Tests.PlayMode.asmdef    existing; keep for unit-ish PlayMode tests
```

`Gothic.Testing` references `Gothic.Core` only — it stays inside the star topology and never becomes a
lateral dependency between modules. The `GOTHIC_FUNCTIONAL_TESTS` define keeps the harness out of shipping
players; the Lane B build profile sets it.

### 3.2 Driver API (what a scenario looks like)

```csharp
public class CombatScenario : FunctionalTest
{
    protected override DeveloperConfigOverride Config => new()
    {
        ConfigName    = "FunctionalTest",
        World         = WorldToSpawn.World,
        SpawnWaypoint = "OC_MAIN_ENTRANCE",
        EnableNpcs    = true,
        EnableCombatSystem = true
    };

    [UnityTest, Order(10)]
    public IEnumerator PlayerCanReachTheOrcArena()
    {
        using (Session.Step("walk-to-arena"))
        {
            yield return Driver.WalkForward(seconds: 6f);
            yield return Driver.WaitUntil(() => Driver.PlayerDistanceTo("OC_ARENA") < 5f,
                timeout: 20f, because: "player should reach the arena");
        }
    }

    [UnityTest, Order(20)]
    public IEnumerator PlayerHitRegistersOnNpc()
    {
        NpcContainer target = null;
        using (Session.Step("attack-npc"))
        {
            GlobalEventDispatcher.FightHit.AddListener((attacker, victim, _) => target = victim);

            yield return Driver.DrawWeapon();
            yield return Driver.Attack(HandSide.Right);
            yield return Driver.WaitUntil(() => target != null, timeout: 5f,
                because: "a swing inside the DEF_OPT_FRAME window should connect");
        }
    }
}
```

`Session.Step(...)` returns an `IDisposable` that writes `step.begin`/`step.end` trace events, tags every
screenshot taken inside it, and — on failure — names the flushed replay clip. Scenarios never touch
`Keyboard`, file paths, or recorders.

`GameDriver` verbs, all yielding and all timeout-guarded:
`WalkForward/Back/Strafe`, `Turn(degrees)`, `Jump`, `Sprint`, `Crouch`, `OpenMenu`, `GrabWithBothHands`,
`DrawWeapon`, `Attack(HandSide)`, `Interact`, `WaitForScene(name)`, `WaitUntil(predicate, timeout, because)`,
`WaitForEvent(UnityEvent, timeout)`, `Teleport(waypoint)` (Marvin-mode style shortcut, so a combat scenario
does not spend 60 s walking).

**This is the whole V2 DSL (D9, D10):** ordinary C# methods called directly from NUnit test methods, no
string grammar and no runtime parser to maintain. `WaitUntil(predicate, timeout, because)` is deliberately
the *only* wait verb — there is no fixed-delay verb in the vocabulary, because a `wait(2)` in a scenario
either flakes under CI load or passes for the wrong reason the day an animation runs 300 ms longer (D11).

**Assertions read state and events, never trajectories (D12).** `PlayerHitRegistersOnNpc` above does not
assert a swing timestamp or an NPC's end position — it asserts that a `FightHit` fired within a timeout. The
discipline: prefer "the monster closed to within 2 m and landed a hit within 8 seconds" over "the monster
ended at x = 12.4" — the first is true for either dodge direction and survives a retuned dodge distance, the
second breaks on the next balance pass.

### 3.3 Feature 1 — Error logs

- **`LogRecorder`** implements `UberLogger.ILogger` and is registered at session start. Reuse the existing
  `FileLoggingLogger` formatting by extracting that nested class out of the `#if !UNITY_EDITOR` block in
  `FileLoggingHandler.cs` into its own file (one class per file, per the style guide) so the Editor lane
  produces byte-identical logs to a shipped build.
- Additionally subscribe to `Application.logMessageReceivedThreaded` to catch native / engine-side errors
  that never pass through UberLogger.
- Output: `<run>/<scenario>/log.txt` (all severities, all `LogCat` categories) and `errors.txt`
  (Error/Exception only, with callstacks). Both attached to the NUnit XML via `TestContext.AddTestAttachment`
  so failures are readable straight from the CI test report.
- **Failure policy — the part that decides whether this suite is usable:** a Gothic port legitimately logs
  many errors (unimplemented Daedalus externals, missing meshes). `LogAssert`'s default "any `Debug.LogError`
  fails the test" would make every run red. Instead:
  - Maintain `Docs/adr/known-issues/functional-tests-baseline.txt` — normalised error signatures that are
    known and accepted.
  - A test fails on any error **not** in the baseline, and the run reports the delta both ways (new errors
    → fail; baseline entries that no longer fire → suggest removing the line).
  - Exceptions and `LogCat.ZenKit` errors are never baselined.

### 3.4 Feature 2 — Screenshots at errors

`ScreenshotRecorder` renders `Camera.main` into a fixed 1280×720 `RenderTexture` and reads it back, rather
than calling `ScreenCapture.CaptureScreenshot`. Reasons: resolution is deterministic regardless of the
batch-mode window size; it works whether or not XR is active (the simulator means it usually is not, so
`StereoScreenCaptureMode` is not applicable); and the readback happens on a frame boundary we control
(`yield return new WaitForEndOfFrame()`).

Triggers:

| Trigger | Detail |
|---|---|
| First error/exception log | Rate-limited: max 5 per step, 2 s cooldown, max 40 per session |
| Test failure | In `[UnityTearDown]`, when `TestContext.CurrentContext.Result.Outcome` is not `Success` |
| Step boundary | One frame per `Session.Step` begin and end — a free storyboard of the playthrough |
| Watchdog trip | See §3.6 |
| Explicit | `Driver.Screenshot("after-first-swing")` |

Files land in `<run>/<scenario>/shots/<seq>_<frame>_<reason>.png`, and each shot writes a `screenshot`
trace event carrying its filename, so the timeline and the images cross-reference.

### 3.5 Feature 3 — Trace stream

**Format.** One JSON object per line (JSONL) — append-only, survives a hard crash mid-write, greppable,
and streamable into a viewer later.

```jsonl
{"seq":1,"t":12.400,"frame":744,"fixed":372,"gameTime":"08:03","type":"step.begin","name":"walk-to-arena"}
{"seq":2,"t":12.417,"frame":745,"fixed":372,"type":"input.key","key":"W","state":"down"}
{"seq":3,"t":12.500,"frame":750,"type":"event","name":"WorldSceneLoaded","args":[]}
{"seq":4,"t":13.000,"frame":780,"type":"sample","pos":[1204.1,32.0,882.7],"rot":[0,131.0,0],"hp":100}
{"seq":5,"t":13.100,"frame":786,"type":"log","severity":"Error","cat":"Animations","msg":"..."}
{"seq":6,"t":13.117,"frame":787,"type":"screenshot","file":"shots/0003_787_error.png"}
```

**Sources, merged on one clock:**
1. Step begin/end.
2. Every simulated input event (the driver emits them; nothing is inferred).
3. **Every `GlobalEventDispatcher` event, wired automatically:**

```csharp
private void SubscribeToAllGlobalEvents()
{
    var fields = typeof(GlobalEventDispatcher)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => typeof(UnityEventBase).IsAssignableFrom(f.FieldType));

    foreach (var field in fields)
    {
        // UnityEvent<T…> AddListener is reached via a small generic dispatch helper;
        // the closure captures the field name as the trace event name.
        TraceEventBinder.Bind(field.Name, (UnityEventBase)field.GetValue(null), Record);
    }
}
```
   This is why D7 matters: the bus is already the project's single seam for "everything that happens", and
   reflection keeps the trace complete as new events are added.
4. Player state samples every 10 fixed frames (position, rotation, HP, walk mode, current animation).
5. Log lines at Warning and above, so logs and events share one ordering.

**Replay is out of scope (D16).** `TraceReplayer`, `Replay/` and `TraceReader` are cut from the design — the
recorder's deliverable is V2 source (§3.10), not a replayable trace, so nothing downstream needs to re-drive
the game from recorded input. This is a real simplification, not just a smaller task list: the trace now
needs **transcription fidelity** — capturing what the player *meant*, verb by verb — rather than **replay
fidelity** — reproducing frame-exact input timing, which the Daedalus VM's own state and the frame-skipped
async loader would have fought at every step anyway.

`Time.captureDeltaTime = 1f/30f` for the whole session is kept, but for a different reason than before: it
makes capture pacing and screenshot/step timing independent of the CI machine's speed, not — as it would
have needed to be for replay — a guarantee of frame-accurate re-simulation.

The raw input stream is still recorded, retained for two narrow jobs where the oracle is not "did the right
thing happen": **crash reproduction** (re-feed the same inputs to reach the same crash) and **performance
regression** (same inputs, compare frame times across builds). Neither needs the game to reach the same
game-state outcome, only the same inputs at the same cadence.

**Video (D8).** `FrameRingBuffer` keeps the last ~30 s at 640×360 / 10 fps in memory (~4 000 frames ≈ 100 MB
raw, or encode incrementally). On failure it flushes to PNGs and CI runs `ffmpeg` to produce
`<scenario>/replay.mp4`. The nightly lane may instead run `com.unity.recorder` (`RecorderController` API,
Editor-only, works in batch mode when graphics are initialised) for a full-session capture.

### 3.6 Watchdog and timeouts

The most likely failure for a frame-skipped async loader is a silent stall: no exception, no error line, just
nothing happening. Four mechanisms cover the failure space between them, and it is worth being precise about
which one covers what — an earlier draft of this section credited the watchdog with all four jobs.

- **Tolerant waits are the primary net.** Every point where a scenario hands control back to the game is inside
  a `WaitUntil(predicate, timeout, because:)` (D11). It fails cleanly, in-process, with the artifacts intact —
  the `because` string, the last 20 trace events and a screenshot. This is what ends a stalled test.
- **In-process watchdog — a load-phase fast-fail.** If no `GlobalEventDispatcher` event and no player movement
  occur for N seconds (default 30, configurable per step; the boot before the first step has its own, larger
  budget), it captures a screenshot, dumps the trace, writes a `watchdog.stall` event and fails the step.

  Its live window is narrower than it looks, and deliberately so. `GameTimeService.TimeTick()` invokes
  `GameTimeSecondChangeCallback` roughly every 0.07 s of real time, started by `WorldSceneLoaded` and stopped
  by `LoadingSceneLoaded` — so once a world is up the bus carries a ~14 Hz heartbeat and the watchdog cannot
  trip at all. It is live during boot, pre-caching and loading, where game time is stopped and bus traffic is
  bursty (a `CreateNpc` per NPC, scene lifecycle events) so a gap in it means something.

  Suppressing the watchdog during gameplay is the heartbeat doing useful work, not a defect to route around: a
  long dialog produces no bus events whatsoever — `AI_Output` is Daedalus, the deferred tier of §3.11 — so a
  watchdog which ignored the `GameTime*` events would fail perfectly healthy scenarios, which is the flakiness
  D11 exists to prevent.

  What it adds over the wait timeout is therefore narrow but real: it fails a dead load in seconds instead of
  at the wait's full budget, and it timestamps *when* the game went quiet. The second half matters in this
  project specifically, because a Gothic port legitimately logs errors constantly (§3.3) — "an error was
  logged" does not tell you which line meant "and then it stopped"; `watchdog.stall` does.
- **Guards cover gameplay-level wrongness** (§3.11), not the watchdog. Falling through the world, a `NaN` in a
  transform and a frame-time cliff all happen while game time keeps ticking, so the bus heartbeat keeps the
  watchdog quiet throughout. These are per-frame invariants and belong in that catalogue.
- **The CLI `--timeout` is the outer safety net**, set generously (e.g. 3× the expected session time). It is
  the only thing that catches a process which stops rendering entirely, since every in-process detector needs
  frames to tick. It kills the process and takes the artifacts with it, which is why the mechanisms above exist
  — but note they are what keeps the run off this path, not the watchdog alone.

### 3.7 Run artifact layout

```
Artifacts/functional/<utc-timestamp>-<git-sha>/
  manifest.json          git sha, Unity version, config name, Gothic install fingerprint, seed, machine
  results.xml            NUnit
  junit.xml
  summary.md             step-by-step pass/fail with inline screenshot links
  <scenario>/
    log.txt  errors.txt  trace.jsonl  replay.mp4
    shots/*.png
```

### 3.8 Runner platform and graphics

**Linux is a first-class option for the runner.** The `com.gothic.core.binaries` package already ships and
enables `Dependencies/linux_x86/libzenkitcapi.so` and `libdmusic.so` for both *Editor (OS: Linux, x86_64)*
and *Linux64*. `Assets/XR/Settings/` defines loaders only for Standalone (Windows) and Android, so a Linux
Editor starts with XR simply absent — which is exactly the state the simulator lane wants. Lane A therefore
needs no Linux player build at all. (The `Dependencies/README.md` still describes the Linux `.so` as
"not used" and warns about OpenXR on Linux; that note predates the `linux_x86/` folder and should be
corrected.)

**A dedicated GPU is not required — a graphics device is.** These are different things, and conflating them
is the easiest way to design this suite into a corner:

| Option | Setup cost | Rendering | Suitable for |
|---|---|---|---|
| `-nographics` | none | **none** — no graphics device is created | Lane 1 only. Forecloses features 2 and 3. |
| `xvfb-run` / virtual framebuffer | none | Mesa **llvmpipe**, software | Smoke lane, at a cost. Xvfb offers no DRI, so GL falls back to software *regardless of the GPU present*. |
| Real X session on an **integrated** GPU | small: a forced/virtual output (a dummy display adapter is the least clever and most reliable route) plus autologin | full hardware acceleration | **Recommended.** Removes the throughput problem outright. |
| **Dedicated** GPU | hardware | full hardware acceleration, headroom for higher capture resolution and the nightly video lane | Nice to have, not required. |

Order-of-magnitude for the software-raster path: with `Time.captureDeltaTime = 1f/30f`, a 30 s scripted
scenario is 900 frames. A Gothic world under llvmpipe on a modern many-core CPU runs at single-digit fps, so
that scenario costs several minutes of wall clock. Survivable for a smoke lane, too slow for the full
nightly suite — which is why the hardware-accelerated X session is the recommendation rather than an
optimisation.

Because world loading and pre-caching are CPU- and IO-bound rather than GPU-bound, core count, RAM and NVMe
throughput matter more to total runtime than the GPU does. A modest machine with a real graphics device
beats a fast one running `-nographics`, because the latter cannot produce the artifacts at all.

**Linux-specific items to verify early:**

- **Distribution packaging.** On distributions that are not FHS-compliant (NixOS and similar), the Unity
  Editor and the standalone `unity` binary need an FHS wrapper or a dynamic-loader shim before they will
  start. Solved, but it is the first thing that will fail.
- **Case sensitivity.** A Gothic installation carries mixed-case paths (`Data/`, `_work/DATA/`) and Linux
  filesystems are case-sensitive by default. If ZenKit's directory scan trips on this, mount the Gothic
  directory case-insensitively (ext4 `casefold`, or a `ciopfs` overlay). Cheap to test on day one and
  expensive to discover once the runner is provisioned (§8, phase 7).
- **Container access to the GPU.** If the runner is containerised for isolation (§6), the container needs
  `/dev/dri` passed through, or it silently drops back to software rendering.
- **Lane B on Linux** would need a `StandaloneLinux64` build method in `VRBuilderActions`, which currently
  defines only Windows64, Pico and Quest. Lane A needs nothing.

### 3.9 RNG, seeding and seed sweeps

Gothic branches randomly at runtime — a monster's dodge direction, dialogue variation, loot rolls — and
every one of those decisions funnels through one Daedalus external. `VmExternalDomain.cs:113` registers
`Hlp_Random`; the implementation at `VmExternalDomain.cs:506` is:

```csharp
public int Hlp_Random(int n0)
{
    var rand = Random.Range(0, n0 - 1);
    return LogInstantExternal(nameof(Hlp_Random), rand, n0);
}
```

That is one global, seedable `UnityEngine.Random` stream behind every probabilistic Daedalus decision (D13).

**Seeding.** `Random.InitState(seed)` once per session, in V0, with the seed written to `manifest.json`
(§3.7). Seeding is not what makes tolerant assertions pass — D12's outcome/invariant style already handles
either branch of a coin flip — it is what makes a **specific failing run reproducible**: given last night's
seed, replay the same branch decisions and get the same red result locally.

**A known leak, documented and deliberately not fixed yet.** `UnityEngine.Random` is one process-wide
stream, and gameplay is not its only consumer: `AbstractMorphAnimation` draws from it for NPC blinking, and
`SoundHandler` / `SoundDaytimeHandler` draw from it for ambient sound delays, both on frame timers. If the
async scene loader's per-frame time budget is wall-clock-based rather than frame-count-based, the number of
draws consumed during loading varies run to run, which offsets every gameplay draw that follows. The fix is
to give the VM its own `System.Random` (or a dedicated `Random.State`) so gameplay and cosmetic draws stop
sharing a stream. **Defer it** — only do this if measurement shows that replaying a recorded seed does not
reproduce the original failure. Treat that as something to measure later, not something to predict now.

**A real bug, found in passing, unrelated to testing:** `Random.Range(int, int)` is max-exclusive, so
`Random.Range(0, n0 - 1)` returns `0 .. n0-2` while Gothic's own `Hlp_Random(n)` contract is `0 .. n-1` — the
top option of every random choice in the game never fires. Fixable today, and tracked as its own item in the
companion task list, independent of this ADR's scope.

**Seed sweeps (D14).** Once V2's vocabulary exists, running one scenario under ~20 fixed seeds nightly is
close to free: same scenario, same assertions, a different `Random.InitState` each run. Nineteen green and
one red is both a genuine edge case and the exact seed that reproduces it. This is cheap, deterministic,
property-based coverage of Gothic's branching behaviour — exhaust it before reaching for AI-assisted review
of a run (ADR-0002).

### 3.10 Recorder (V1)

An Editor feature: hit record, play the game through the HVR simulator keyboard surface as normal, stop,
save. Two things make it worth building rather than only hand-writing every scenario:

**It emits V2 source, not an input blob (D15).** The recorder's output is a draft `FunctionalTest` subclass
— real, compilable C# using the same `Driver.*` verbs a hand-written scenario would use — never a binary or
JSON input-replay artifact. A raw input log asserts nothing, is unreadable once it breaks, and is
invalidated by every intentional gameplay tweak; that is precisely how these suites lose the team's trust
over time. The generated draft is a starting point: a human reviews it, trims it, and adds assertions before
it is committed.

**It records assertion candidates.** While recording, the harness listens to `GlobalEventDispatcher` the
same way `TraceRecorder` does (§3.5, D7) and emits the events it saw as commented suggestions next to the
call that plausibly caused them:

```csharp
Driver.Attack(HandSide.Right);
// observed @ +0.4s: FightHit(player → Scavenger_01, hp 42→31)
```

This is the point of building a recorder at all: the hard part of writing a functional scenario by hand is
knowing what you are *allowed* to assert on, and the recorder already watched it happen.

**Input → verb mapping.** Turning key presses back into driver calls is the inverse of what `InputDriver`
does going forward — a human presses `W`, the recorder must emit `Driver.WalkForward(...)`. Both directions
are derived from one shared mapping table so the recorder and the driver cannot drift apart; adding a verb
means adding one table row, not two independent switch statements.

### 3.11 Actuator and observation catalogue

The driver vocabulary (D10) is built from two separate catalogues, not one list of "inputs and sensors".
**Actuators** are what a scenario *does*; **observations** are what it *sees*. They are kept apart because
their consumers differ: the V1 recorder transcribes actuators and merely annotates observations (§3.10);
ADR-0002's oracle reads observations and never touches actuators; and the two need different plumbing —
actuators need hold/release timing and an inverse mapping, observations need timestamping and a
push-versus-pull contract.

Both catalogues are named rather than numbered. The names are the vocabulary the team will actually use in
review ("that should be a semantic verb, not a physical one"), and the build order below carries the
sequence that numbering would otherwise imply.

#### Actuators — what a scenario can do

Grouped by *fidelity*, not by device, because fidelity is what determines whether a scenario survives a
UI change.

| Level | Examples | Used by |
|---|---|---|
| **Raw** | key down/up, mouse delta, hand transform set, controller button state | `InputDriver` internals and the recorder. **Never a scenario.** |
| **Physical** | `WalkForward`, `Turn`, `LookUp`, `MoveHandTo(pose)`, `Trigger(hand)`, `Grip(hand)` | Scenarios, only where the physicality is the thing under test |
| **Semantic** | `ClickMenuEntry(label)`, `SelectDialogOption(text)`, `DrawWeapon`, `Attack`, `Interact(vob)`, `Teleport(waypoint)` | The default for scenarios |
| **Arrange** | `SetGameTime`, `SetSeed`, `LoadSaveSlot`, `SpawnAt(waypoint)`, `GiveItem`, `SetHealth` | The arrange phase only — **never the act phase** |

**Prefer semantic verbs; drop to physical only when the physical interaction is what is being tested.**
`PointAt(worldPos)` plus `Trigger` breaks the moment a dialog panel moves five centimetres;
`SelectDialogOption("END")` does not. The interaction gate is the deliberate exception: it is written at the
physical level precisely to prove that level works, and every scenario after it is written at the semantic
level.

**Arrange verbs exist because arranging state by cheating is legitimate and acting by cheating is not.**
The distinction has to be enforced by the API rather than by convention — a scenario that reaches for
`SetHealth` mid-act because it is convenient has quietly stopped testing anything, and no reviewer will
reliably catch it. Arrange verbs are therefore reachable only from the fixture's arrange phase and throw if
called once the act phase has begun.

Game time earns its place in this group for a Gothic-specific reason: NPC behaviour is driven by daily
routines, so an NPC-behaviour scenario is untestable without controlling the clock.
`DeveloperConfig.StartTimeHour` / `TimeSpeedMultiplier` and `GameTimeService` are the existing seams.

#### Observations — what a scenario can see

The primary split is **push** (events, ordered, timestamped, must be subscribed before they fire) versus
**pull** (state, queryable at any frame). Both are needed for different jobs: an assertion that `FightHit`
fired needs push; a `WaitUntil` predicate needs pull. Daedalus and engine sources appear in both, so the
source is a *label* on an observation rather than a tier of its own.

| Tier | Kind | Source | Status |
|---|---|---|---|
| **Lifecycle** | push | `GlobalEventDispatcher`: `PlayerSceneLoaded`, `ZenKitBootstrapped`, `MainMenuSceneLoaded`, `LoadingSceneLoaded`, `WorldSceneLoaded`, `LoadGameStart` | exists |
| **Gameplay** | push | `GlobalEventDispatcher`: `FightHit`, `FightWindow*`, `SetHeroAsTarget`, `LockPickCombo*`, `MusicZone*`, `LevelChangeTriggered`, `CreateNpc`, `GameTime*ChangeCallback` | exists |
| **State** | pull | player pose / HP / walk mode / equipped weapon; nearby NPCs with distance, AI state, HP, hostility; inventory; current animation; game time; active scene and world | partial |
| **Frame** | pull | `ScreenshotRecorder` readback, plus cheap derived signals (mean luminance, frozen-frame). Consumed mainly by ADR-0002 | V0 |
| **Diagnostics** | both | UberLogger lines by severity and `LogCat`; performance counters (frame time, GC, draw calls) | partial |
| **Daedalus** | push | `VmExternalDomain` externals: `AI_Output`, `AI_StartState`, `Ai_Attack`, `AI_GotoNpc`, `AI_ProcessInfos`, perception and target calls | **deferred** |

**The Daedalus tier is deferred out of this ADR's initial scope.** It is the only tier that does not exist
at all, it is the most expensive to build, and nothing before the first combat or AI-routine scenario
actually needs it. Revisit when such a scenario is written.

The implementation is recorded here so the deferral does not lose it:
`VmExternalDomain.RegisterExternals()` makes 142 direct `vm.RegisterExternal<…>(name, handler)` calls plus
one `RegisterExternalDefault(DefaultExternal)`. There is no single chokepoint today — but introducing one
local `Register<…>(name, handler)` helper that wraps the handler in a trace call, then mechanically
rewriting those 142 call sites, instruments the entire Daedalus surface in a single change.
`LogInstantExternal` and `DeveloperConfig.EnableZSpyInstantLogs` are partial precedents for the same seam.
When it is built, emit the high-value subset first behind a `LogCat` filter — `AI_Output`, `AI_StartState`,
`Ai_Attack` and the perception/target calls — because tracing all 142 unfiltered would drown the trace.

#### Guards — continuous invariants

Distinct from both catalogues: assertions that run every frame for the whole session, independent of what
any scenario asserts.

- no error logged that is not in the baseline (§3.3)
- player Y position within world bounds — catches fall-through-world deterministically
- no `NaN` in a player or NPC transform
- frame time under budget, so a performance cliff fails loudly instead of silently

Guards are cheap, always on, and are the deterministic counterpart to ADR-0002's oracle: whatever a guard
can catch should never be delegated to a model.

#### Build order

The catalogues are not independent — **semantic verbs are implemented on top of state observations**
(resolving "the entry labelled New Game" means reading the menu first), so they cannot be built as parallel
workstreams.

| # | Item | Why here |
|---|---|---|
| 1 | Lifecycle + minimal State (player pose, active scene) | The first `WaitUntil` in the first scenario needs both |
| 2 | Raw + Physical actuators | The interaction gate |
| 3 | Gameplay observations | Free — already on the bus, and D7's reflection binding picks them up wholesale |
| 4 | State, in full | Prerequisite for the next row |
| 5 | Semantic actuators | Depends on State to resolve targets |
| 6 | Guards + Diagnostics | Cheap, and they raise the value of every scenario already written |
| 7 | Arrange actuators | Needed once scenarios stop starting from a fresh world |

The Daedalus tier follows only when a scenario requires it.

---

## 4. CI pipeline

### 4.1 Lanes

| Lane | Trigger | Runner | Scope | Budget |
|---|---|---|---|---|
| 1 — Compile & unit | every PR | `ubuntu-latest`, GitHub-hosted | compile w/ and w/o HVR, EditMode tests. No game data needed. | < 10 min |
| 2 — Smoke playthrough | PR to `main`, or `pipeline-functional-test` label | self-hosted, rendering (§3.8) | boot → new game → world loaded → walk 5 m → no new errors | ~5 min |
| 3 — Full functional | nightly | self-hosted, rendering (§3.8) | all scenarios × Gothic 1 and Gothic 2, sharded, video on | ~45 min |
| 4 — Player build | release | self-hosted | Lane B: scenarios against the built Windows player | on demand |

### 4.2 Lane 2 sketch

```yaml
name: Functional Tests
on:
  pull_request:
    types: [opened, synchronize, labeled]

jobs:
  functional:
    if: github.event.pull_request.head.repo.full_name == github.repository
    runs-on: [self-hosted, gothic-data, rendering]
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v4
        with: { clean: true }

      - name: Inject HurricaneVR
        uses: actions/checkout@v4
        with:
          repository: Gothic-Unity-Project/asset-dependencies
          path: Checkout/AssetRepo
          token: ${{ secrets.PAT_ASSET_REPO_CHECKOUT }}
      - run: Move-Item Checkout/AssetRepo/HVR/${{ env.HVRversion }} Assets/HurricaneVR

      - name: Point the game at the runner's Gothic installation
        run: Copy-Item $env:GOTHIC_CI_SETTINGS Assets/StreamingAssets/GameSettings.dev.json

      - name: Restore static cache
        uses: actions/cache@v4
        with:
          path: ${{ env.LOCALAPPDATA }}\..\LocalLow\Gothic-Unity-Project\Gothic-Unity\Cache
          key: staticcache-${{ hashFiles('Assets/Gothic-Core/Scripts/Const/Constants.cs') }}

      - name: Run functional smoke suite
        run: >
          unity test . --mode PlayMode
          --filter "Gothic.Tests.Functional.Smoke.*"
          --output Artifacts/results.xml --report-format nunit,junit
          --junit-output Artifacts/junit.xml
          --retries 1 --timeout 1500 --format github
          -- -gothicTestConfig FunctionalTest -gothicGameVersion Gothic1

      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: functional-artifacts
          path: Artifacts/
          retention-days: 14
```

Note the absence of `-nographics`, and the `if:` guard on the job — see §6.

---

## 5. Alternatives considered

**A. GitHub-hosted Linux + `game-ci/unity-test-runner` + Xvfb.** Zero infrastructure to own, and it is the
path most Unity projects take. Rejected as the primary lane — but the rejection rests on **hosting the game
data**, not on Linux: a GitHub-hosted runner starts from nothing each run, so it would need the ~1.5 GB
Gothic installation fetched from a source the project would have to host itself, with a copyright question
attached. That is a hosting problem, independent of OS, and independent of the graphics options now covered
in §3.8 (which resolve the software-rasterisation speed concern this option used to be rejected on too).
Kept for Lane 1, where no game data or rendering is required.

A **self-hosted Linux runner does not have the hosting problem** — the Gothic installation stays resident on
disk between runs, and the project's native dependencies are already packaged and enabled for the Linux
Editor (§3.8). It is therefore a first-class choice for Lanes 2 and 3 under D3, not a fallback: Windows and
Linux are chosen per-runner on operational grounds (existing hardware, licensing, maintenance familiarity),
not because either is a technical second choice.

**B. `-nographics` everywhere.** Cheapest and most portable. Rejected: it structurally forecloses goals 2
and 3 and hides the largest bug class in the project.

**C. Standalone-player tests only (skip the Editor lane).** More faithful — it exercises the
`#if !UNITY_EDITOR` paths, including the real `FileLoggingHandler`. Rejected as the *starting* point: it
requires a full build per iteration, blocks the use of Unity Recorder, and forces the existing tests to be
rewritten (`EditorApplication.EnterPlaymode`, `UnityEditor` imports) before anything runs at all. Adopted as
Lane B once the harness is stable.

**D. Record and replay real player sessions instead of scripting scenarios.** Attractive — it would turn
playtesting into regression tests directly, no authored DSL required. Rejected, permanently rather than
provisionally: the Daedalus VM's own state and the frame-skipped async loader mean a recorded session
diverges from a replayed one regardless of `Time.captureDeltaTime` or seeding (§3.5, §3.9, D16). The V1
recorder (§3.10) keeps the useful half of this idea — turn a played session into a starting artifact —
without the replay guarantee this alternative would have needed.

**E. Unity's Automated QA package.** Unmaintained and not aligned with Unity 6 / the new CLI; its recorded
UI-driven model does not fit a VR project driven through HVR.

---

## 6. Consequences

**Positive**
- Regressions in boot, world loading, movement and combat are caught before release rather than by players.
- The trace + screenshot bundle makes a CI failure debuggable without a local reproduction — which matters
  for a distributed volunteer team on Discord.
- The `GlobalEventDispatcher` reflection trace doubles as a live debugging tool outside CI.
- Unity CLI exit codes remove the log-scraping fragility from the current build pipeline; `unity build` can
  later replace `game-ci/unity-builder` for the same reason.

**Negative / costs**
- **A self-hosted runner must be owned and maintained**: Unity 6.3, an HVR licence, Gothic 1 and 2
  installations, and a graphics device with an interactive session behind it (§3.8) — a GPU is not required,
  only the device is. On Windows, batch mode needs a real desktop session: a runner installed as a Windows
  *service* will fail to create a D3D device. On Linux, the equivalent is a real X session backed by
  hardware- or software-accelerated GL, not a headless service unit.
- **Self-hosted runners on a public repository are a real security exposure.** A fork PR must never execute
  on it. Mitigations, in order of preference: gate on `github.event.pull_request.head.repo.full_name ==
  github.repository` (as above), require the `pipeline-functional-test` label applied by a maintainer, never
  use `pull_request_target`, and prefer an ephemeral VM or container per job.
- Functional tests are inherently flakier than unit tests. The baseline file, `--retries`, and the watchdog
  are the mitigations; expect ongoing maintenance of the baseline.
- Artifact volume: budget ~50–150 MB per failing scenario with video. Retention 14 days, video only on
  failure or nightly.

**Neutral**
- The harness adds an assembly to the project, define-gated out of shipping builds.

---

## 7. Prerequisites (found while writing this ADR)

These block or degrade the plan and should be fixed first; each is small.

1. **`AbstractTest.GetConfiguration()` loads a path that does not exist.** It requests
   `Resources.Load<DeveloperConfig>("GameConfigurations/Production")`, but the asset lives at
   `Assets/Gothic-Core/Resources/DeveloperConfigs/Production.asset`. The load returns `null`, so
   `config.EnableVRDeviceSimulator = true` throws — the existing tests cannot pass as written.
   `CLAUDE.md` repeats the same stale `GameConfigurations/` path and should be corrected with it.
2. **`FileLoggingHandler` is compiled out in the Editor** (`#if !UNITY_EDITOR`). Lane A therefore produces
   no file log today. Extract `FileLoggingLogger` so the harness can reuse it (§3.3).
3. **No way to select a `DeveloperConfig` without editing an asset.** Add the `-gothicTestConfig` override
   (D5) so tests stop mutating tracked `Production.asset`.
4. **`VRGameTest._alreadySetUp`** guards a `[UnitySetUp]` that runs per test. Move the pattern into the
   shared `FunctionalTest` base class with explicit session semantics (D4).
5. **`FlatContextBootstrap` / `Constants.ScenePlayer`** — decide whether functional tests target VR+simulator
   or Flat controls. VR+simulator is recommended: it is what ships, and Flat is not yet complete.

---

## 8. Implementation plan

Phases run in order; the **Iteration** column is the V-label from D9, which does not match the phase number
one-to-one — V0 spans phases 1–3 (harness + all three recorders + smoke), V2 is built after V0 proves the
pipeline, seed sweeps piggyback on V2, and V1 comes last because it emits V2 source.

| Phase | Iteration | Deliverable | Exit criterion |
|---|---|---|---|
| 0 | — | Prerequisites §7 fixed | Existing `VRGameTest` passes locally in the Test Runner |
| 1 | V0 | `Gothic.Testing` runtime harness: `TestSession`, `InputDriver`, `GameDriver` (minimal verb set), `Watchdog`, `LogRecorder`, seeded `Random.InitState` (§3.9) | `Smoke.BootAndWalk` passes locally with `log.txt` produced |
| 2 | V0 | `ScreenshotRecorder` + baseline error policy | A deliberately broken build produces a screenshot at the failing frame |
| 3 | V0 | `TraceRecorder` incl. `GlobalEventDispatcher` reflection binding + `summary.md` | `trace.jsonl` reconstructs a full playthrough timeline |
| 4 | V2 | Full `GameDriver` vocabulary (§3.2): every verb, `WaitUntil` as the only wait, event/VM-state assertion style | Existing scenarios use only the full vocabulary; no fixed-delay waits remain anywhere |
| 5 | Seed sweeps | Nightly seed-sweep runner: one scenario × ~20 seeds, per-seed manifest (§3.9) | A seeded failure reproduces locally from its recorded seed |
| 6 | V1 | `ScenarioRecorder` (record/play/stop/save) emitting V2 source + commented assertion candidates; shared input→verb map (§3.10) | Recording a manual playthrough produces a compilable draft scenario |
| 7 | infra | Self-hosted runner provisioned; Lane 2 workflow green | Smoke lane runs on a PR in under 10 minutes |
| 8 | infra | `FrameRingBuffer` + ffmpeg replay clip; Lane 3 nightly, sharded, G1 + G2 | Nightly report with video on failure |
| 9 | infra | Lane B standalone-player runs | Functional suite runs unmodified against the built player |

---

## 9. Open questions

1. **Who hosts the self-hosted runner** (Windows or Linux, §3.8), and is the team comfortable with
   infrastructure outside GitHub's own hosting executing PR-triggered jobs under the guard in §6?
2. **Gothic 1 only, or both versions** in the nightly lane? Both doubles runtime and Gothic-2-specific
   scenarios do not exist yet.
3. **Is a Gothic *demo* installation** (freely redistributable) sufficient for smoke coverage? If so, Lane 2
   could move to GitHub-hosted runners and the whole self-hosted question narrows to Lane 3.
4. **Should this ADR live in the repo or the wiki?** It is filed here because it versions with the code that
   implements it; the wiki's *Resources & Tools → Build Pipelines* page should link to it.

---

## 10. References

- [Meet the Unity CLI](https://unity.com/blog/meet-the-unity-cli) — announcement, `com.unity.pipeline`, Unity 6.0 LTS+
- [Unity CLI build/run/test reference](https://github.com/Unity-Technologies/skills/blob/main/skills/unity-cli/references/build-run-test.md) — flags and exit codes
- [Unity Manual — Desktop headless mode](https://docs.unity3d.com/6000.5/Documentation/Manual/desktop-headless-mode.html) — `-batchmode -nographics` semantics
- [Test Framework — running tests from the command line](https://docs.unity3d.com/Packages/com.unity.test-framework@1.1/manual/reference-command-line.html)
- [GameCI test runner](https://game.ci/docs/github/test-runner/) — current-generation alternative, used by Lane 1
