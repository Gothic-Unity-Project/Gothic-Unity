# Split assessment: `experimental/game-completion-candidate-vibecoded-borobongo`

Goal: turn the branch into a series of **reviewable, VR-testable PRs** against `main`.

---

## 0. Status tracker (live, read this first)

Last update: 2026-10-08, by Claude (orchestrator session). Current phase: **Wave 1 ported, reviewed by AI, waiting
for human VR test + code review** (see [Wave 1: how to test and review](#wave-1-how-to-test-and-review)).

**For a new session picking this up:** read this section, then [`branch-split/SLICE-RULES.md`](branch-split/SLICE-RULES.md)
(workflow, quality bar, flag policy, locations). Check `git -C <worktree> log --oneline origin/main..` to see what a
slice already has. The source branch is never modified.

Legend (handover table: ✅ = already done): ⬜ not started · 🔧 porting · ✅ ported + compiles (awaiting human VR test & review) · 🧪 in human test ·
🟢 merged to `main` · ⏸ on hold

### Infrastructure

| | Item | Where |
|---|---|---|
| ✅ | Commit → slice map, all 251 commits | `Gothic-workspace/_split-tools/slice_map.txt` |
| ✅ | Line ownership per file (git blame) | `_split-tools/ownership.tsv`, `_split-tools/blame_slices.py` |
| ✅ | Compile check without Unity (`dotnet build` on regenerated csproj, all Gothic assemblies incl. Flat) | `_split-tools/compile_check.py <worktree>`; verified OK on `main` and on the source branch |
| ✅ | Porting rules, quality bar, flag policy, PR template | `Docs/branch-split/SLICE-RULES.md` |
| ✅ | Read-only source worktree | `Gothic-workspace/GU-split-00-source` (detached at `04006809`) |
| ✅ | Integration branch `split/integration` (worktree `GU-split-integration`): all Wave 1 slices incl. review fixes merged, conflicts resolved, compile check | `RESULT: OK`. `main` → integration: 148 files, +8.3k/−0.4k. Note: the "oracle diff" to the source grows slightly again because review fixes intentionally improve on the source |
| ✅ | Independent review pass (fresh reviewer per slice), findings fixed by the porting session as extra commits | 01–07 reviewed and fixed. No blockers found. Each `pr/NN-*.md` has a "review round" note |
| ✅ | Conflict resolver helper (keeps BOM/CRLF) | `_split-tools/resolve.py` |

### Slices

| # | Slice | Status | Branch / worktree (`Gothic-workspace/…`) | PR description | Notes |
|---|---|---|---|---|---|
| 01 | Engine robustness fixes | 🧪 pushed (`origin/split/01-robustness`), PR to be created; GitHub-ready description: `pr/01-robustness.github.md` | `split/01-robustness` · `GU-split-01-robustness` | `branch-split/pr/01-robustness.md` | 15 commits, 22 files, +331/−124, no flags |
| 02 | Mods, game selection & audio | ✅ AI-reviewed | `split/02-mods` · `GU-split-02-mods` | `branch-split/pr/02-mods.md` | 6 commits, 42 files, +964/−133. Open: foreign-save guard, NVorbis location, per-mod menu names |
| 03 | Dev tools (Marvin) | ✅ AI-reviewed | `split/03-devtools` · `GU-split-03-devtools` | `branch-split/pr/03-devtools.md` | 4 commits, 6 files, +302/−2. Deviation: time skip crosses midnight |
| 04 | Dialogs, subtitles, HUD | ✅ AI-reviewed | `split/04-dialogs-ui` · `GU-split-04-dialogs-ui` | `branch-split/pr/04-dialogs-ui.md` | 6 commits, 33 files, +1058/−45. Attribute writes now update vob **and** instance |
| 05 | World VOBs: movers, triggers, MOBSI | ✅ AI-reviewed | `split/05-world-vobs` · `GU-split-05-world-vobs` | `branch-split/pr/05-world-vobs.md` | 12 commits, 42 files, +2031/−88. **New vs. source:** mover keyframes + Gothic speed units (`MoverSpeedMultiplier` 3.48 → 1) |
| 06 | Lighting & day/night | ✅ AI-reviewed | `split/06-lighting` · `GU-split-06-lighting` | `branch-split/pr/06-lighting.md` | 3 commits, 10 files, +390/−21. Quest test needed |
| 07 | Bink video | ✅ AI-reviewed | `split/07-bink` · `GU-split-07-bink` | `branch-split/pr/07-bink.md` | 3 commits, 32 files, +3270 (codec port ~2.2k). Quest test + license decision |
| 08 | NPC death & knockout | ⬜ | | | Wave 2 |
| 09 | NPC melee AI | ⬜ | | | Wave 2 |
| 10 | Target switching, party, berserk | ⬜ | | | Wave 2 |
| 11 | NPC perception & routines | ⬜ | | | Wave 3 |
| 12 | NPC movement | ⬜ | | | Wave 3 |
| 13 | VR ranged weapons | ⬜ | | | Wave 3 |
| 14 | VR items in hand | ⬜ | | | Wave 3 |
| 15 | Inventory owners + NPC loot | ⬜ | | | Wave 3 |
| 16 | Trade counter | ⬜ | | | Wave 4 |
| 17 | Stack splitting + backpack extras | ⬜ | | | Wave 4 |
| 18 | Equipment & VR hero body | ⬜ | | | Wave 4 |
| 19 | Particles | ⬜ | | | Wave 4 |
| 20 | Magic | ⬜ | | | Wave 4 |
| 21 | Save/Load | ⏸ | | | Decision §6.1 open |

### Decisions taken during execution (veto welcome)

| Date | Decision | Why |
|---|---|---|
| 2026-10-08 | **Flag policy:** keep feature / VR-design / escape-hatch flags and tunables; drop flags that only guard an engine-parity bug fix (keep the "on" path). Details in SLICE-RULES. | You asked for maintainability. Production has all fix flags on, so the code without them is the tested path, minus dead branches. |
| 2026-10-08 | **Small maintainability fixes during porting are allowed** (lines >120, one type per file, static singletons → DI, dead/debug code). Structural refactors stay separate follow-ups. | Human code review gets clean code without changing tested behavior. |
| 2026-10-08 | Wave 1 slices branch from `origin/main` (`e91bc09c`) independently. Conflicts between them are resolved once in `split/integration`. | They're parallel and mostly independent. |
| 2026-10-08 | Nothing is pushed, no PRs opened, the source branch is untouched. | Outward-facing. Waits for your OK. |
| 2026-10-11 | Push / PR only per slice on explicit OK (01 approved). Commits keep `Co-Authored-By: Claude`, **no `Claude-Session` link** (also not in PR texts). | Maintainer's decision; the repo is public. |

### Wave 1: how to test and review

**Everything below is AI-ported and AI-reviewed, but untested in Unity/VR.** Compile checks pass for every branch,
including Flat. Your test and code review are the real gate.

#### Setup (once, ~30–60 min for the first Unity import)

1. Unity Hub → *Add project from disk* → `Gothic-workspace/GU-split-integration` (contains all of Wave 1). It already
   has the `Assets/HurricaneVR` junction and your `GameSettings.dev.json`. The first import builds its own `Library/`.
2. Assign your local DeveloperConfig to `GameManager` in the Bootstrap scene. **Don't commit the scene.** For the
   Marvin tests, turn on `PreselectMarvinMode` and the cheats (see test 2 below).
3. Have ready: G1, G2 NotR, and optionally Mroczne Tajemnice (G1 mod) and G2 New Balance (Ogg dubbing).
4. Keep the Uber Console open (`Gothic > Debug > Uber Console`). Most tests also check that **no new exceptions or
   log spam** appear.

#### Strategy

- **Test once on `split/integration`**, in the order below. One Unity import covers all 7 PRs.
- If something fails, open that slice's own worktree (`GU-split-NN-*`) and check whether it fails there too:
  - fails there too → it's the slice itself;
  - only fails on integration → it's an interaction between slices. Tell me which; the merge commits on
    `split/integration` show every conflict resolution.
- **Code review per PR branch** (`git diff origin/main...split/NN-*`), in the same order. Each `pr/NN-*.md` has a
  "Review notes" section with deviations from the source, judgement calls and known risks.
- Mark each row in the checklist below with ✅ / ❌ (plus a short note). I (or a next session) pick it up from there.

#### Test order and what to test

Why this order:
- **01 first**: it's the baseline. If boot, dialogs or fights are broken, every other test is noise.
- **03 second**: the cheats (time skip, teleport, give items, spawn) are the tools for testing 04–07.
- **Bigger or riskier features last.** The cross-slice checks only make sense once each part works.

| # | Slice | ~Time | What to test (full steps in `pr/NN-*.md` → "How to test") | Watch especially for | Result |
|---|---|---|---|---|---|
| 1 | **01 Robustness** | 10 min | G1 new game boots; talk to Diego (voice, subtitles, choice box returns); watch faces of talking NPCs; fight a scavenger (combo/hit timing); drink a potion (+50 heals, doesn't *set* HP to 50); lock pick lying next to a lock; status menu rows | No new exceptions; no `KeyNotFoundException` spam from faces | ⬜ |
| 2 | **03 Dev tools** | 10 min | Marvin tab with all cheats on: Level +5 after drinking a strength potion (bonus must stay), guild buttons in G1 **and** G2, time skip across midnight, teleport to a waypoint, spawn `WOLF`, give items | Guild in G2 correct; potion bonus not lost | ⬜ |
| 3 | **02 Mods & game selection** | 15 min | Vanilla G1 + G2 with `ModPath` empty behave as before (boot, talk, save menu). G2 main menu → New Game. Then Mroczne Tajemnice via `EnableMod`/`ModPath`/`ModIni`; G2 New Balance Ogg voices. Optional: a build with `GameSettings.json` `"GameVersion": "Gothic2"` | **Vanilla music still plays** (dmusic loading changed); separate cache folder per game+mod | ⬜ |
| 4 | **04 Dialogs & HUD** | 15 min | Walk around during a dialog (held within ~5 m; walking away during choices ends it); dialog box/subtitles next to a wall stay visible; kill a speaker mid-line (subtitle disappears); "New log entry" HUD + sound; seated NPC talks; health bars of enemies and yourself; chapter screen waits for the jingle | **Status menu after damage/potion/teacher: values must not jump** (attributes are now written to vob + instance); hands visible above the dialog box | ⬜ |
| 5 | **05 World VOBs** | 20–30 min | Quick: chest lid/bench grab (no warnings), key opens a locked chest, doors block you. Play: G1 Old Camp gate winch, G2 lever gate, shrine prayer (MOBSI dialog), alchemy table without/with flask, beds in G2 and MT, NPCs on benches. Longer: G2 X mark dig, level change then lever, G1 chapter 3 graveyard gate, Sleeper temple puzzles | **Mover speed and paths are new code** (Gothic units, `MoverSpeedMultiplier` 1 instead of 3.48, all keyframes): gates should take a few seconds, not be instant or take a minute. `VRWheelInteraction` was dropped as unused; wheels go through mob grab | ⬜ |
| 6 | **06 Lighting** | 10 min | Saturas' cave / any G2 cave lit by torches; Khorinis at night (time skip): outdoors dark blue, huts lit, fires light the ground; back to noon; walk through G2 city (pool hands lights out by distance) | Frame rate vs. `main`; `DayNightLightStrength` = 0 → always day | ⬜ |
| 7 | **07 Bink video** | 10 min | G1 intro / chapter video plays in the cinema with sound in sync; skip with trigger/A/X; an MP4 next to the `.bik` wins; change the world during a video; several videos in a row | After a world change mid-video the world isn't black; no memory growth (Profiler) | ⬜ |
| 8 | **Cross-slice** (integration only) | 10 min | Status menu after a teacher + potion + damage (01+04); seated NPC dialog (04+05); alchemy table missing-item text shows as HUD (04 `Print` + 05 MOBSI); a mod voice line that is Ogg *and* a vanilla WAV (01+02 `SoundDomain`); video + screen message in one session (04+07) | Anything that works on the slice branch but not here | ⬜ |
| 9 | **Mobile** (Quest/Pico, needs a build) | 20 min | 06: caves lit, fires near you in Khorinis, `SHADER_API_MOBILE` (512 slots) is the variant used; 07: a video plays (decoder thread, YUV shader); general frame rate | Not done on the source branch either | ⬜ |

#### Code review: where to look hardest

| PR | Focus |
|---|---|
| 01 | Quick: one fix per commit. `SoundDomain` WAV chunk walk, `Npc_ChangeAttribute` delta + clamp |
| 02 | `ConfigService` (game/mod selection, cache suffix), `ResourceCacheService` (dmusic loading changed for vanilla too), NVorbis plugin meta, per-mod menu names (commented) |
| 03 | `MarvinTabHandler` only. Dev-only code, all cheats off in Production |
| 04 | `VRUiOnTop` (render queues, shared materials), `VRScreenMessages` now owned by `VRContextInteractionService` (was a static), `NpcService.SyncHeroInstanceToVob` + the instance writes in `ExtNpcChangeAttribute`/`FightService` |
| 05 | Largest PR, go commit by commit: `MoverAdapter` (**rewritten after review**, not the play-tested source version), trigger dispatch in `VobService`, `Trigger*Handler`s, MOBSI in `DialogService`, `VRDigSpot` |
| 06 | `StationaryLightsService` pool; the `NormalW` "outdoor" flag convention (`WorldMeshBuilder` ↔ `Lit-World.shader`) |
| 07 | `BinkPlayer` (decoder thread, `Close()`), `VRCinema` + `VRCinemaService`. Skim the codec files (`Bink/*`, ported). Licensing in the PR |

#### After testing: merging to `main`

Nothing is pushed yet. Once you're OK with a slice, I push its branch and open the PR (with `pr/NN-*.md` as the
description) in the order 01 → 07. Each later PR then needs a rebase onto the new `main`. The conflicts are known and
already resolved once in `split/integration`:
- 02 ↔ 01: `SoundDomain`, `Output`, `NpcHelperService`
- 03/04 ↔ 01: `NpcService`
- 05 ↔ 04: `DialogService`, `DeveloperConfig`, `Production.asset`, `VRFocus`
- 07 ↔ 04: `VmExternalDomain`, `VRContextInteractionService`, `GraphicsSettings`

### Handover notes for later slices

Things a ported slice left out on purpose that a later slice must add (in the later slice's own style):

| For slice | From | What to add |
|---|---|---|
| 14 | 07 | `adc2067a` "endings quit after the credits": the source's static `VRCinema.QuitAfterVideos` + `ScriptExitGame` event must become instance members of `VRCinemaService` / `VRCinema` (statics no longer exist). |
| 13 | 07 | Bow-collider part of `9504e2c9` in `AbstractMeshBuilder`. |
| 20 | 03 | Summon part of `aebea3c9`: `summonedBy` parameter of the runtime spawn, `NpcService.SpawnNpcRuntime` wrapper, `Wld_SpawnNpcRange`. |
| 11 | 03 | `NpcRoutineService.RecalculateAllNpcRoutines` (time skip doesn't call it in 03). |
| 08 | 01 | `523c277d` (`Vm.InitInstance` in `Wld_InsertNpc`, needs `IsZkInstanceInitialized` + attribute sync `cd88a122`; for dead-on-spawn NPCs like Nek). |
| ✅ 05 | 01 | `TryUnlockWithKey` key-unlock part of `6334e567`. |
| 09 | 01 | `HasComboWindowOpened` property of `dcb2f21d` (frame-param fix itself is in 01). |
| 18 | 01 | `02bf2761` (script-equipped items / `on_equip`): `main` never runs `on_equip`, so it belongs with equipment. |
| 21 | 02 | `UNITYSAVE.json` check in `SaveMenu` (on `main` it would hide every save), `SavesFolderPath`, slot activation, save init in `BootstrapService`. |
| 20 | 02 | `BootstrapDomain` `VfxVm`. |
| 12 | 02 | NavMesh init in `BootstrapService`. |
| 04, 13 | 02 | New `StatusMenu` / `VRCrossbow` code must use `ContextGameVersionService.IsGothic2()` (the game-version check that slice 02 introduces), not the config. |
| 14 or 20 (first one) | 06 | Reserved dynamic light slot: `DynamicLightIndex`, `UpdateDynamicLight()`, `ClearDynamicLight()`, `StationaryLight.Refresh()`; pool starts at `cachedCount + 1`. See `pr/06-lighting.md`. |
| 11 | 06 | `RoomService` sector-cell parameters in `WorldMeshBuilder.ExtractChunks`. |
| 09 | 04 | `AI_OutputSVM_Overlay` + overlay handling in Output/OutputSvm, `NpcDialogStopDistance`/GoToNpc, dialog invulnerability in `FightService`. |
| 09/10 | 04 | Re-add the seated-NPC block when porting the expanded `ExtNpcClearAiQueue`. |
| 14 | 04 | `ScriptRemovedInvItems` event, `VRSocket.IsPlayerSocket`, held-item removal in `VRWeaponService`, `EnableScriptRemovesHeldItems`. |
| 20 | 04 | `HideHealthBar` is dead (internal setter never called) → drop it. |
| ✅ 05 | 01 | Morph-mesh warn-once fix exists in both 01 (`97a3a370`) and 05 (`f965d8f5`) → drop it from 05. |
| 21 | 05 | `SuppressVobTriggerDispatch` guard of `081e0c1b` (only matters with `RebindNpcAliases`). |
| 09 | 05 | `UseMob` state propagation + `TriggerLinkedMover` (use `TryGetMovers`, plural); combat tuning of `a696f99b`. |
| ✅ 04 | 05 | `Print` external (needs 04's `PrintScreen`); `SetBodyState` in `UseMob` where the source read `EnableMobSeatFix`. |
| 11 | 05 | `PERC_ASSESSUSEMOB` hook in `VRFocus.OnGrabbed`; mob owner log in `VobService`. |
| 13 | 05 | Don't port `EnsureOwnVobLoader` again (already in 05). |
| integration | 04 + 05 | 05 swaps `npcContainer.Go` → `GetDialogGo` in `DialogService`; `Print` registration inside the `EnableScreenMessages` block. `GetHeroContainer` null-safety identical to 01. |
| integration | 01 + 02 | Adjacent hunks in `SoundDomain` (WAV fix vs. Ogg). |
| integration | 03 + 04 | `NpcService.SyncHeroInstanceToVob()` exists in both → keep one copy. |

### Log

- 2026-10-08: Infrastructure built, Wave 1 worktrees created, Wave 1 porting started (one session per slice).
- 2026-10-08: 01, 02, 03, 04, 06, 07 ported (✅). 05 still running at that point.
- 2026-10-08: 05 interrupted by a rate limit after 4 commits (movers, triggers, morph fix, MOBSI); resumed. Integration
  of 01–04, 06, 07 done and compiling. The integration merges only resolve conflicts; PRs to `main` are still opened
  from the individual `split/NN-*` branches. Conflicts resolved: `SoundDomain` (Ogg sniff before RIFF walk),
  `NpcHelperService` + `NpcService` (comment only), `VmExternalDomain` (Print + Video externals side by side),
  `VRContextInteractionService` (cinema + screen messages), `GraphicsSettings` (both shaders).
- 2026-10-08: Review fixes done for all of Wave 1 and merged into `split/integration` (compiles). Notable: 04 makes
  C# attribute writes update vob + instance (the status menu sync was unsafe without it); 05 rewrote mover timing
  to Gothic units + per-keyframe paths (`MoverSpeedMultiplier` 3.48 → 1); `Print` external moved to 04 (gap between 04
  and 05). Every slice branch compiles on its own. **Phase 1 done on the AI side → waiting for human test/review.**
  **Next session:** read the test checklist results in "Wave 1: how to test and review". Fix ❌ rows on the slice
  branch (new commits), re-merge into `split/integration`, compile. Push / open PRs only with the maintainer's OK.
  Wave 2 (08 → 09 → 10, sequential) can start from `split/integration` or from `main` once Wave 1 is merged; ask the
  maintainer which base they prefer.
- 2026-10-11: Slice 01 pushed (maintainer's OK). Then, at the maintainer's request, the `Claude-Session:` trailer was
  removed from all commits of all `split/*` branches (messages only, trees verified identical; `Co-Authored-By` kept)
  and `split/01-robustness` was force-pushed (`35a66a16` → `123ae10c`). **All commit hashes on `split/*` changed.**
  Older hashes in this file and in `pr/*.md` that refer to split commits are pre-rewrite. Map:
  `_split-tools/rewrite-map-2026-10-11.txt`. Hashes of the source branch are unchanged.
- 2026-10-11: At the maintainer's request, `Co-Authored-By: BoroBongo <borobongo@gmail.com>` (author of the source
  branch) was added to every commit of all `split/*` branches (messages only, trees verified identical);
  `split/01-robustness` force-pushed again (`123ae10c` → `208b07e3`). Hash map: `_split-tools/rewrite-map-2026-10-11b.txt`.
  New slice commits should carry both co-author lines for now: **BoroBongo first, Claude second**.
- 2026-10-11: Co-author order changed to BoroBongo first, Claude second on all `split/*` commits (trees identical);
  `split/01-robustness` force-pushed (`208b07e3` → `d694951e`). Hash map: `_split-tools/rewrite-map-2026-10-11c.txt`.
- Open questions for the maintainer: see each `pr/NN-*.md` (02: foreign-save guard, NVorbis location, per-mod menu
  names; 03: cheats default-on in the Lab config?; 05: OK with the mover speed change to Gothic units?; 06:
  `StationaryLightsService` registered twice in `ReflexProjectInstaller` on `main`; 07: Bink licensing, Quest).

---

*The original analysis follows. Section numbers are referenced from the tracker.*

| | |
|---|---|
| Merge base with `main` | `e91bc09c` (`main` already merged in) |
| Commits | 264 (251 non-merge), 2026-06-14 → 2026-10-05 |
| Diff vs. `main` | 306 files, +30,112 / −1,101 |
| Existing review docs | `Docs/branch-review/` (12 theme pages written by the author, plus an architecture check and a test guide) |

---

## 1. What makes this branch hard to split

### 1.1 Commits run in date order, not by theme

The history is a **timeline**, not a stack of features. Some examples:

- June commits create NPC melee (`ed02071c`). October commits change the same code again
  (`887af70a`, `84f47c83`, `c1a4c1ad`).
- One commit often covers several themes. `a063883c` covers the Shrink spell, `AI_UseItem` potions, summons running,
  mana refunds **and** world particle vobs. `00f95e72` covers sneaking, portal rooms, room perception and mob-use
  perception.
- About 15 commits are "revert" or "move X out" commits (`cb3d5b54`, `e9fb80bf`, `37c1edd6`, `63236617`, …). They
  cancel earlier commits, so replaying the history would replay those dead ends too.

### 1.2 A few files are shared by nearly every theme (conflict hotspots)

| File | Commits touching it | Themes involved |
|---|---|---|
| `Models/Config/DeveloperConfig.cs` | **59** | all |
| `Services/Npc/NpcAiService.cs` | 27 | combat, perception, movement, magic, summons |
| `Domain/Vm/VmExternalDomain.cs` | 27 | all Daedalus externals (combat, docs, rooms, video, triggers, magic) |
| `Services/Npc/FightService.cs` | 26 | combat, magic, perception, summons |
| `Services/Npc/NpcService.cs` / `Adapters/Npc/AiHandler.cs` | 20 / 20 | NPC everything, save/load |
| `Services/Vobs/VobService.cs` | 18 | movers, triggers, rooms, save/load |
| `Domain/Npc/Actions/AnimationActions/AttackPlayAni.cs` | 17 | melee, ranged, magic, navmesh, water |
| `Domain/Npc/NpcInitializerDomain.cs` | 14 | save/load, death, model scale |
| `VRMouth.cs`, `VRFocus.cs`, `VRVobItem.cs`, `VRBackpack.cs` | 9–13 each | VR items, trade, loot, perception |
| `GlobalEventDispatcher.cs`, `ReflexProjectInstaller.cs`, `Production.asset` | 6–9 each | registration and config "append" points |

**Conclusion:** cherry-picking commits per theme won't work. Each pick would conflict in the files above, and many
picks would pull in code from other themes. Splitting by commit range (for example "June", "July", "September")
gives PRs that are neither one theme nor testable.

### 1.3 The good news

- Almost every new behavior sits behind a **`DeveloperConfig` flag** (116 new fields). That gives a natural
  "feature boundary" to cut along.
- Many new parts are **new files** (62 new C# files). They move cleanly as whole files: Bink, triggers, trade, navmesh,
  water, jump/fall, hero body, rune caster, transform puppet, doc viewer, stack splitter, room service.
- New externals are registered via `RegisterIfDeclared`. Each slice can therefore add only its own externals to
  `VmExternalDomain` without breaking the others.
- The **final state** of the branch has been play-tested by the author. That gives a fixed target to rebuild toward
  (see §4.4).

---

## 2. Dependencies between components (checked in the code)

These references between new components decide the order:

```
VRRuneCaster (magic) ──► VRProjectile (ranged)          VRSpellTransformPuppet ──► NpcWaterService (movement)
        │──────────────► StationaryLightsService (light) NpcJumpFall ───────► NpcNavMeshService (movement)
        │──────────────► ParticleService (particles)     AttackPlayAni ─────► NpcNavMeshService, NpcWaterService
        │──────────────► VRSpellTransformService              HeroPerceptionService ◄── VRFocus, lock picking
        └──────────────► VRHeroBody (hero body)          RoomService ◄── WorldMeshBuilder, VmExternalDomain
TradeSession / VRBackpack ──► IInventoryOwner (NPC loot)  MoverAdapter ◄── UseMob, VRWheelInteraction, VRPlayerService
FightService ──► ParticleService (hit/blood FX)          VRTorch ──► StationaryLightsService
```

Main points:

- **NPC combat core** is the base for perception, ranged, magic, movement (chases) and NPC loot (knockout).
  It is the **critical path**.
- **Magic comes last among the gameplay slices.** It pulls in ranged, lights, particles, transformations and the
  hero body.
- **Bink video, lighting/day-night, mods/audio, world VOBs, dialogs/UI** barely depend on the combat code. They
  can start in parallel right away.

---

## 3. Proposed split (21 PRs in 5 waves)

Sizes are rough (added + removed lines in hand-written C#, excluding `.meta` files and generated data).
Each PR **adds only its own `DeveloperConfig` fields and `Production.asset` values**.

### Wave 1: independent of the NPC core (can all run in parallel from `main`)

| # | PR | Content | Main files | Key commits | ≈ size | VR test |
|---|---|---|---|---|---|---|
| **1** | **Engine robustness fixes** | Null guards, WAV 8-bit + chunk parsing, `.Frames` not `.Slots`, morph `FirstOrDefault`/KeyNotFound, menu item gaps, `Npc_ChangeAttribute` delta + no STR→DEX clamp, precache skips broken worlds, hero-init race | many small hunks | `98a928a1` `669f194d` `dcb2f21d` `1e38022f` `f4e273c8` `c5c23786` `3c6f6df0` `7b480ab8` `1513a5b7` `08d95db5` `60488ed6` `7d67054e` | 600 | Vanilla G1/G2 boot, play 10 min, no regressions |
| **2** | **Mods, game selection & audio** | Mod loading (mod root, OU.BIN/CSL, ModIni, per-ini archives), JSON mod switching, `GameVersion` one-build, Ogg Vorbis (NVorbis), ZSpy via GameSettings, `EnableMusic`, extended Daedalus classes, language detection, Ikarus/LeGo Trialoge stubs, G2 menu fixes (new game, slots 16–20) | `ConfigService`, `ResourceCacheService`, `SoundDomain`, `MusicDomain`, `JsonRoot*`, `BootstrapDomain`, menus, `Plugins/NVorbis` | `5eb534aa` `fca4712c` `33c5f200` `55ce4bcc` `bc632361` `0d220118` `803816ee` `77d74e77` `8d8f5aba` `50c003a4` `a39b128c` | 900 + NVorbis | Vanilla G1, G2, Mroczne Tajemnice, a G2 Ogg mod |
| **3** | **Dev tools (Marvin panel)** | Cheat buttons, minimize, waypoint teleport, level/guild/time skip, item cheats. Land early because **every later PR uses it to test**. Combat cheats (one-hit kill) go into PR 8. | `MarvinTabHandler`, `Menu-MarvinMode.prefab`, `VRMenuCheatAdapter` | `bb6e7c4d` `5959c1ad` `8ea94468` `517c49f5` `4378ade1` | 450 | Each button once |
| **4** | **Dialogs, subtitles, HUD** | Dialog free movement / dialog bubble, UI-on-top shader, subtitles not cut by walls (12 m), screen messages (`PrintScreen`), important-info order, SVM overlay/unknown SVMs, seated dialogs, HP/status bars live, StatusMenu talents/protection, chapter jingle | `DialogService`, `Output*.cs`, `VRUiOnTop`, `GothicUIOnTop.shader`, `VRScreenMessages`, `VRSubtitles`, `StatusBarAdapter`, `StatusMenu` | `ee4dbc2a` `d109a6aa` `cd10002c` `7d95812a` `2d308ea2` `f3968086` `59661daf` `3bc6ca69` `ae923da4` `8e6b8f44` | 1,100 | Talk, walk away, kill a speaker mid-line, open the status menu |
| **5** | **World VOBs: movers, triggers, MOBSI** | `MoverAdapter`, wheel/winch, full trigger system (list, script, zone, codemaster, message filter, world start, untouch), cycle cap, MOBSI dialogs (shrine, alchemy, rune table, bookstand), beds, doors excluded from `UseMob`, keys for containers/doors, mob seats | `MoverAdapter`, `Trigger*Handler`, `VobService`, `VobInitializerDomain`, `VRWheelInteraction`, `VRPlayerService`, `UseMob`, lock picking | `75bd89fd` `a1a8f2a2` `e37c8952` `b773c4c0` `9636179d` `05be2e1a` `f44ca0ac` `74302081` `a696f99b` (key part) `2fb20421` | 2,000 | G1 Old Camp gate winch, G2 lever gate, pray at a shrine, sleep in a bed |
| **6** | **Lighting & day/night** | Torch-lit caves (static lights in lightmapped areas), shared light pool (1023/512), night tint from sky `polyColor` | `StationaryLightsService`, `StationaryLight`, `WorldMeshBuilder` (lighting part), `SkyService`, shaders | `4982a263` `d07e1c4d` | 450 | Saturas' cave, Khorinis at night. **Quest check needed** |
| **7** | **Bink video in a VR cinema** | C# Bink decoder (OpenGothic/FFmpeg port), YUV shader, `VRCinema`, decoder thread, audio sync | `Bink/*`, `BinkPlayer`, `VRCinema`, `GothicBinkYUV.shader` | `d688f290` `9504e2c9` `9f984e2c` `2de7f2dc` | 2,700 (mostly ported codec) | G1 intro / `PlayVideo` from a script. **License review (LGPL) as its own topic** |

### Wave 2: NPC core (critical path, done one after another)

| # | PR | Content | Key commits | ≈ size | VR test |
|---|---|---|---|---|---|
| **8** | **NPC death & knockout** | `ZS_Dead` (XP, loot init, quest vars, dead-on-spawn), engine unconscious rule, hero knockout + recovery, stand-up, drop held weapon, immortal flag, combat cheats | `77f6808c` `44f29848` `ab56c400` `9fa477b4` `03d6284c` `4bb7903c` `881dc2bd` `887af70a` (rule part) | 900 | Beat a citizen (KO) vs. a bandit (dies); get knocked out yourself |
| **9** | **NPC melee AI** | `AI_Attack` combos (`DEF_WINDOW`), hit at `DEF_OPT_FRAME`, draw/sheath animations + sounds + two-handers on the back, combat context, `Npc_GetNextTarget`, chase give-up/`$RUNCOWARD`, finishing moves, approach spread, NPC weapon hitbox, monsters' bites | `ed02071c` `6ebb9d56` `59e39ebe` `e5dfc6ce` `c1a4c1ad` `1770b593` `50d634e2` `84f47c83` `a4d92755` | 2,000 | Fight a wolf, an orc, a guard; flee and watch the chase give up |
| **10** | **Target switching, party, berserk** | Non-hero attackers, NPC↔NPC separation, bystander rules, summons/party never steal aggro, `AIV_EnemyOverride`, berserk + guards stopping it | `59ba4bb6` `c272029e` `05571acd` `05500b62` `f4700e25` `fe5777c3` `7fef08fd` `035ca617` `1f73a5a6` | 900 | Lure a wolf into a camp; G1 berserk |

### Wave 3: built on the NPC core (parallel after PR 10)

| # | PR | Content | Key commits | ≈ size | VR test |
|---|---|---|---|---|---|
| **11** | **NPC perception & routines** | Passive perc broadcast, perceptions without the hero nearby, `ASSESSBODY`/`MURDER`/`DEFEAT`, hero sneak body state, `RoomService` + `PERC_ASSESSENTERROOM` + portal guild externals, `ASSESSUSEMOB`, routine change resets perceptions, culled NPCs move to their routine WP, FreePoint locking, reactions to the hero's drawn weapon | `b459f4f7` `00f95e72` `dc4788dc` `ed809d8a` `1e18996d` `5efcb33a` `bfb28b3d` `d7d99a77` `0ac83519` `6bd758e0` | 1,500 | Murder in front of witnesses; enter a hut; sneak behind a guard |
| **12** | **NPC movement** | Runtime NavMesh chases, wading/swimming, falling + fall damage, ledge climbing, ladders, wall sliding, `AI_Flee`, skeletons not floating | `768c676d` `150d31c2` `b4380dfe` `79896b75` `28191858` | 1,900 | Run over a cliff, lure a wolf into water, Old Camp tower ladder |
| **13** | **VR ranged weapons** | Bow with a real string, crossbow (bolts, one hand at 60 %), arrows staying in the world, bow colliders, NPC ranged combat (stays **off** in Production) | `c0299809` `d575c3b3` `9504e2c9` (colliders) `bf4969b0` `b7371ff9` `65a4ed33` `050b2c34` | 1,300 | Shoot at a scavenger; G1 crossbow |
| **14** | **VR items in hand** | Food stacks, eating only from hand, pouches with two hands (`VRItemUser`), `Doc_*` books/letters/maps with the hero arrow, torches, smoking joints, speed potions / quick-win externals, script inventory events | `4df98e0a` `bdda2d2a` `8e22c152` `49e8c697` `c62882ff` `1fb8180a` `76f2eace` `ea038cb0` `f49f47e6` `b0703e75` `adc2067a` `b1d68e70` | 1,300 | Eat, read a letter, open the G1 map, light a torch, smoke |
| **15** | **Inventory owners + NPC loot** | `IInventoryOwner` abstraction, NPC loot backpack / sockets (`NpcLootMode`), timeout, first-category open | `dbf74e14` `0f5feebe` `56596cc5` `934b0305` (loot part) `7a682222` | 750 | Knock out an NPC and loot him |

### Wave 4: depends on several Wave 3 PRs

| # | PR | Needs | Content | ≈ size |
|---|---|---|---|---|
| **16** | **Trade counter** | 15 | `TradePricing` (domain + **existing unit tests**), `TradeSession/Offer`, `TradeService`, `VRTradeCounter/Goods`, engine prices, anti-theft | 1,000 |
| **17** | **Stack splitting + backpack extras** | 15 | `VRStackSplitter` + `StackSplitService`, item details popup, backpack vacuum (the "TODO TEST ME" flags; decide whether they ship), quick slots keep scale | 1,300 |
| **18** | **Equipment & VR hero body** | 9, 14 | Equip armor/amulets/rings, protection from scripts, NPCs change armor at runtime, helmets, color-only parts, `VRHeroBody` + animator + two-bone IK | 1,700 |
| **19** | **Particles** | 9 | `VobPfxMeshBuilder`, `ParticleService`, `PfxShapeScaler`, animation PFX, `Wld_PlayEffect`, blood, full-rate loops, smoke alpha fix | 600 |
| **20** | **Magic** | 6, 13, 18, 19 | `VRRuneCaster` (charging, invest levels, throwing, Light spell), NPC casters, spell hit, `Spell_ProcessMana`/`time_per_mana`/refunds, summons (spawn, run, ignore master), Shrink/model scale, `AI_UseItem`, teleport keeps held items, then **transformations** (`VRSpellTransformPuppet`, `VRSpellTransformService`; needs 12 for water). If too big, split into 20a casting/NPC casters/summons and 20b transformations. | 2,500 |

### Wave 5: last

| # | PR | Why last |
|---|---|---|
| **21** | **Save/Load (WIP, flag off)** | Reads the state of almost every other feature (equipment, model scale, trade reservations, chests, FreePoints). It is off in Production and still WIP. Merging it last means it saves the final data model once, not 20 changing ones. It could also stay on an experimental branch. |

Project settings (`ProjectSettings/*`, tags, always-included shaders, physics layers) are **not their own PR**. They go
into the PR that needs them: UI tags + `GothicUIOnTop` → PR 4, Bink shader → PR 7, player↔chest/door collision → PR 5,
`VRBuilderActions` platform switch → PR 2.

### Dependency graph

```
main ─┬─ 1 robustness ─┐
      ├─ 2 mods/audio   │   (Wave 1 runs in parallel; 1 should land first because the others build on its guards)
      ├─ 3 dev tools    │
      ├─ 4 dialogs/UI   │
      ├─ 5 world VOBs   │
      ├─ 6 lighting ────┼──────────────────────────────────────────┐
      └─ 7 bink         │                                          │
                        ▼                                          │
              8 death/KO ─► 9 melee ─► 10 targeting                │
                                         │                         │
         ┌───────────┬──────────┬────────┼──────────┐              │
         ▼           ▼          ▼        ▼          ▼              │
     11 perception 12 movement 13 ranged 14 items 15 loot          │
                        │          │       │        ├─► 16 trade    │
                        │          │       │        └─► 17 stacks   │
                        │          │       └──► 18 equip/body       │
                        │          │              │   19 particles ◄┘(9)
                        └──────────┴──────────────┴───────┴──► 20 magic ─► 21 save/load
```

---

## 4. Why this split

1. **One PR = one thing a tester can try in VR in 10–15 minutes.** Each row has a test scenario taken from
   `Docs/branch-review/14-how-to-test.md`. A PR that can't be tested on its own can't be reviewed on its own.
2. **Easy wins first.** Wave 1 is ~8k lines that barely touch the NPC core: robustness fixes, mods, Bink, lighting,
   triggers. They build trust in the process quickly and shrink the remaining diff without risk.
3. **The critical path is made explicit.** Combat is split into three PRs (rules → AI → targeting). Death/knockout
   rules are small and follow the engine, so they're easy to review against OpenGothic. The big AI part
   comes after.
4. **Opinion and fact are kept apart** (the author's own distinction in the review README). Engine behavior
   (perceptions, KO rule, mana, externals) can be reviewed against OpenGothic and the scripts. VR design choices
   (trade counter, stack splitting, spell charging, hero body) need a design discussion. Mixing them in one PR stalls
   the reviewable part.
5. **Licensing and dependency decisions get their own PRs.** Bink (LGPL codec port) and NVorbis (committed directly
   instead of through the binary repo) each sit in one PR, so maintainers decide on them separately.
6. **Risky or unfinished parts are isolated.** Save/load (WIP, off), NPC ranged (off), the "TODO TEST ME" backpack
   features and mobile-sensitive work (light pool, NavMesh bake, Bink thread) are in PRs that can be held back
   without blocking the rest.
7. **Sizes stay reviewable.** Most PRs are 500–2,000 lines. The bigger ones are mostly new files (Bink codec,
   `VRRuneCaster`), which are easier to review than scattered edits.
8. **Refactoring is kept separate.** The architecture page lists god classes (`NpcAiService`, `FightService`,
   `VmExternalDomain`) and fat adapters (`VRRuneCaster`, `VRHeroBody`, …). **Don't refactor during the split.** Port
   the code as it is so behavior matches what was play-tested. Refactor in follow-up PRs once each feature is on
   `main`. Exception: when the next PR in a slice would grow a god class further, the slice may add a **new**
   partial class or service file instead (for example `VmExternalDomain.Doc.cs`). That reduces conflicts between
   parallel slices.

---

## 5. How to do it with AI (several Claude worktrees)

### 5.1 Core technique: rebuild from the final state, don't replay history

For each slice:

1. Create a branch from `main` (or from the PR it depends on).
2. **Copy the slice's new files in whole** from the source branch (`git checkout <src> -- <files>`).
3. For **shared files**, take only this slice's hunks from `git diff main...<src> -- <file>`. Use the slice's flags,
   externals and events as anchors.
4. Remove or stub references to later slices so the code compiles. Example: `VRRuneCaster` → `VRHeroBody` before
   PR 18 exists. Leave a `// TODO(split: PR 18)` marker at each such spot.
5. Add only this slice's `DeveloperConfig` fields, `GlobalEventDispatcher` events, Reflex registrations and
   `Production.asset` values.

This avoids replaying 251 interleaved commits, reverts and later fixes. Each slice gets the **final, play-tested
version** of its code.

### 5.2 Setup: a "slice manifest" first (one session, me as orchestrator)

Before any worktree starts, create one **manifest** (`Docs/branch-split/manifest.md` or a JSON file) and have it
reviewed:

- **Commit → slice map** for all 251 commits. Commits that cover several themes get several slices, with notes on
  which hunks belong where.
- **File ownership:** for each slice, the files it owns in whole and the files it changes only in part (hunks).
- **Flag ownership:** each of the 116 `DeveloperConfig` fields assigned to exactly one slice.
- **Symbol ownership:** each new external, event and service assigned to one slice.

The manifest is the contract between parallel agents. It prevents two worktrees from both porting the same hunk, or
neither of them porting it. A human check of the manifest is cheap: one sitting.

### 5.3 Worktree layout

```
Gothic-workspace/
  Gothic-Unity/                 ← source branch (read-only reference) + orchestrator session
  GU-split-01-robustness/       ← git worktree add ../GU-split-01-robustness -b split/01-robustness origin/main
  GU-split-02-mods/
  GU-split-05-world-vobs/
  ...
```

One Claude Code session per worktree (`claude` started inside it, or `EnterWorktree` from a session). Run **one
wave at a time**: Wave 1 = 7 parallel sessions. Later waves branch from the merged result of the wave before
(or from the PR branch they depend on, as stacked PRs).

**Unity-specific setup per worktree**, needed before anything compiles:

- `Assets/HurricaneVR/` isn't in git. Copy it or link it with a junction from the main checkout.
- The `ZenKitCS/` symlink and `Assets/StreamingAssets/GameSettings.dev.json` (gitignored) need to be in place.
- Each worktree needs its own `Library/`. The first Unity import is slow, so plan for it once per worktree. Reuse
  worktrees across waves (`git switch -c split/11-perception origin/split-integration`) instead of creating new ones.
- Quick compile check without the editor: open the worktree in Unity once so it writes the `.csproj` files. After
  that, an agent can run `dotnet build Gothic.Core.csproj` (and VR) after each step. Otherwise use a batch-mode
  compile:
  `Unity.exe -batchmode -quit -projectPath <wt> -logFile <wt>/compile.log`.
  Flat mode: compile `Gothic.Flat` too (the review notes it was never built).

### 5.4 Prompt template per slice session

```
You are porting slice <NN – name> from branch experimental/game-completion-candidate-vibecoded-borobongo
(read-only, at ../Gothic-Unity) onto this worktree (based on <parent>).
Contract: Docs/branch-split/manifest.md, section <NN>. Port ONLY what it lists.
Rules:
- Whole files: git checkout <src> -- <file>. Partial files: take only hunks for this slice.
- Do not refactor or "improve"; the source is play-tested. Keep behavior identical.
- References to later slices: remove/stub and mark `// TODO(split: <slice>)`.
- Add only this slice's DeveloperConfig fields, events, DI registrations, Production.asset values.
- Compile Gothic.Core, Gothic.VR, Gothic.Flat after each step; fix only compile errors you caused.
- Finish with: PR description (what/why/flags/how to test, from Docs/branch-review/<page>.md),
  list of TODO(split) markers, list of hunks you were unsure about.
```

### 5.5 Built-in checks for AI work

1. **Oracle diff.** After each merged wave, run
   `git diff split-integration experimental/...-borobongo --stat`. It should only shrink, and only by the slices just
   merged. At the end it must be empty, apart from what was deliberately dropped (docs, reverts). This is the main
   check that nothing got lost or duplicated.
2. **Reviewer agent per PR.** A second fresh session checks the slice against the manifest:
   - Is anything missing?
   - Did code from another slice leak in?
   - Are all flags default-off unless the manifest says otherwise?

   Use `/code-review` on the PR, or a short checklist prompt.
3. **TODO(split) marker count.** It should be zero once the target slice lands. A `grep` in CI or by the
   orchestrator finds anything left behind.
4. **Unit tests.** `TradePricingTests` move with PR 16. Pure domain parts (KO rule, trade pricing, mana math) are
   good candidates for agents to add PlayMode tests to during the split, because those tests need no VR.
5. **Human gate: the VR play test.** The AI can't do it. Each PR gets the 10–15 minute scenario from §3, run by a
   person with a headset, before merge. That is the real bottleneck, so the waves are sized to match how many
   testers you have, not how many agents you can run.

### 5.6 Who merges

- The **orchestrator session** (or you) owns the integration branch and resolves the predictable conflicts. These
  are mostly appends: `DeveloperConfig`, `GlobalEventDispatcher`, `ReflexProjectInstaller`, `Production.asset`,
  `VmExternalDomain` registrations. A shared grouping (one `[Header("…")]` block per slice in `DeveloperConfig`,
  in PR order) keeps those conflicts trivial.
- Merge Wave 1 PRs into `main` one by one, rebase the open ones, then start the next wave.

### 5.7 Rough effort

| Step | Who | Effort |
|---|---|---|
| Manifest (commit/file/flag/symbol map) | 1 session + human check | ½–1 day |
| Wave 1 (7 slices) | 7 parallel sessions + orchestrator | 1–2 days porting, then review/testing |
| Waves 2–5 (14 slices) | 1–5 sessions per wave | the same per wave; Wave 2 is one after another |
| VR tests | humans | ~20 × 15 min plus re-tests, **the real limit** |
| Follow-up refactors (god classes, fat adapters, flag cleanup) | separate PRs after merge | later |

Multi-agent runs (for example a `Workflow` with one agent per slice and one verifier agent per slice) are possible.
Use them only for the manifest step or a single wave, and with clear limits. The manifest and the oracle diff matter
more than how many agents run at once.

---

## 6. Decisions maintainers should make before starting

1. **Save/load:** ship as PR 21 behind a flag, or leave it on the experimental branch?
2. **NPC ranged combat:** it's off in Production. Ship it off, or test it and turn it on in PR 13?
3. **"TODO TEST ME" features** (item details popup, backpack vacuum): in or out of PR 17?
4. **Bink (LGPL port) and NVorbis (in repo instead of the binary repo):** OK as they are?
5. **Flag policy:** keep 116 flags during the split (recommended, it's safer) and clean up after, or promote stable
   flags to always-on per PR?
6. **Mobile (Quest/Pico):** which PRs need a device test before merge? At least 6 (lights), 7 (Bink) and 12
   (NavMesh).
