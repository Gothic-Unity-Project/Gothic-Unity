# Action plan after the AI-change audit

- Date: 2026-11-16, repository state `main` at `ffd7f5ec8`
- Built from the four reports in this folder: `fix-clusters.md`, `architecture-core-vobs.md`,
  `architecture-core-npc.md`, `architecture-vr-adapters.md`, plus GitHub issue #521.
- The Quest 2 performance review is in `performance-quest2.md`. Its findings are the "Performance track" section
  below (ids P01-P19); the numbered items 3, 4, 5, 24 and 25 are performance items the architecture reports found.

## How this list is ordered

Most critical and least complex first. Work top to bottom.

- **Critical** means one of: the player sees wrong behaviour, it costs frame time on Quest 2, or it ends a series
  of repeated fixes.
- **Effort**: S = up to half a day, one small PR. M = one to three days, one PR with a short design. L = several
  PRs, needs the `design` skill and an ADR first.
- **Verify first** marks findings that come from code reading only and should be confirmed (in code or in game)
  before changing anything. Nothing in the reports was verified at runtime.
- "Source" names the report and finding to read for details: `fix` = fix-clusters, `vob` = architecture-core-vobs,
  `npc` = architecture-core-npc, `vr` = architecture-vr-adapters.

## Tier 1: critical and small

| # | Change | Where | Why | Effort | Source |
|---|---|---|---|---|---|
| 1 | EditMode test that builds the Reflex container and resolves every registered service | `Assets/Gothic-Tests/EditMode/`, `ReflexProjectInstaller`, `ReflexVRSceneInstaller` | Three "the game / VR scene starts again" fixes were service cycles that only fail at game start | S | fix §9 |
| 2 | EditMode test that compares `DeveloperConfig` fields with the values serialized in `Production.asset` | `Assets/Gothic-Tests/EditMode/`, `DeveloperConfig.cs` | 10 asset-only repair commits; NPC archers shipped melee-only because a flag was off in the asset | S | fix §8 |
| 3 | Enable `VRFocus` only while highlighted or locked; tell the one locked NPC directly | `Gothic-VR/Scripts/Adapters/VRFocus.cs:79, 236-270` | `LateUpdate` runs on every item, NPC and mob instance. Measure in the Old Camp on Quest before and after | S | vr 11 |
| 4 | Index `instance index -> NpcContainer` for `Hlp_GetNpc` | `NpcService.cs:320, 394-407`, `MultiTypeCacheService` | Whole-world scan with a closure on one of the most-called externals | S | npc 7 |
| 5 | Cache "is state perception" per function index; swap the perception lists in one explicit per-frame tick | `NpcAiService.cs:1476-1586` | String lookups and allocations per receiver when perceptions are pending | S | npc 9 |
| 6 | Fix the `SpawnVOBTypes` dev filter with one `IsVobTypeSpawned(type)` | `VobService.cs:385, 560, 987` | One of the three uses is inverted. **Verify first** which one | S | vob M2 |
| 7 | Replace `CallVmFunctionWithNpcGlobals` with `VmGlobalsScope`; decide the null rule once | `NpcAiService.cs:210-230`, `Domain/Vm/VmGlobalsScope.cs` | Two contradicting rules for which VM globals stay set. **Verify first** against OpenGothic | S | npc 4 |
| 8 | Real lock-pick rule in Core instead of `Random.value > 0.5f` | `VRContainerDoorPickingInteraction.cs:206-250`, new `LockPickDomain` | Placeholder break chance (marked FIXME) decides gameplay | S | vr 15 |
| 9 | Decide and fix the double reaction to a broken lock pick | `VobService.cs:106`, `PlayerService.cs:72` | Two listeners react to one event (amount and inventory). **Needs your decision** | S | vob M2 |
| 10 | Use `VrSwingSampler` for the dig spot | `VRDigSpot.cs:87` | Last leftover of the world-space swing measurement that caused false attacks | S | fix §6 |
| 11 | Remove the listeners that are never unsubscribed | `VRSwimDive.cs:108, 160`, `VRSpectatorCamera.cs:60, 81` | Handlers pile up and hold destroyed objects if the player scene is rebuilt | S | vr 17 |
| 12 | `HeroProvider` that services inject when they only need the hero | new, replaces `NpcService.GetHeroContainer()` uses in services that cycle | `NpcService` is the hub most DI cycles run through | S | fix §9 |
| 13 | Decide whether `oCTouchDamage` is a placeholder or a built VOB | `VobInitializerDomain.cs:233, 407`, `VobService.cs:398` | It is both today. **Needs your decision** | S | vob H1 |

## Performance track (Quest 2)

Source: `performance-quest2.md`. It is a static review: nothing was measured, so every impact below is an
estimate. The track runs next to the tiers and has its own order, highest expected frame-time gain per effort
first. Target: 72 Hz, about 13.8 ms per frame for CPU and GPU together.

**Step 0, before any change: take a baseline on a real Quest 2.** The report has a measurement plan with 8 scenes
and target values. Without it no item here can be confirmed, and several may turn out smaller than they read.

**Step 1, settings that could not be inspected.** Check these by hand in the Editor, because each decides whether
a finding below is real: the SRP Batcher switch, depth and opaque texture, depth priming and post-processing on
the renderer, the OpenXR settings (render mode, foveation, late latching), the culling distances in
`Production.asset`, and whether the `Light` on `zCVobLight.prefab` is enabled.

| Id | Change | Where | What it costs today | Effort | Impact | Confirm with |
|---|---|---|---|---|---|---|
| P01 | Stop animating and skinning NPCs that are not visible | `AbstractMeshBuilder.cs:311` (`updateWhenOffscreen = true`), `AnimationSystem.cs:338` (`AlwaysAnimate`) | Every NPC within 50 m animates and skins every frame, visible or not; NPC culling is by distance only | S | high | A/B toggle in the Old Camp, CPU frame time and skinning marker |
| P03 | Foveated rendering, eye texture scale and refresh rate for Quest | no code sets them; `Packages/manifest.json` has plain OpenXR without a Meta package | Full-resolution rendering in the periphery on a fill-rate-limited GPU. **Verify first**: the OpenXR asset was not inspected | S | high | GPU frame time in OVR Metrics Tool |
| P02 | Make `Lit/World` SRP Batcher compatible; share materials instead of `new Material` per renderer | `Lit-World.shader:34` (`_FocusBrightness` outside `UnityPerMaterial`) | Very likely one draw call setup per renderer on the render thread. **Verify first** in the shader Inspector or Frame Debugger | S | high if confirmed | Frame Debugger: SRP batch count |
| P08 | Fixed timestep matched to the display; lower the maximum allowed timestep | `ProjectSettings/TimeManager.asset` (0.02 s and 0.333 s) | 50 Hz physics against 72 Hz display gives uneven hand physics; up to 16 physics steps after one hitch | S | medium | Physics marker after a streaming hitch |
| 3 | `VRFocus` only enabled when needed | see Tier 1 | | S | medium | |
| 4, 5 | `Hlp_GetNpc` index, passive perception caching | see Tier 1 | | S | medium | |
| P11 | No vertex array allocation per frame in morph animation | `VobAnimateMorph` | GC pressure, hitches | S | low-medium | GC alloc per frame in the profiler |
| P12 | Check the log level before building the message; no stack trace for normal logs | 522 `Logger.Log` call sites | String formatting and stack traces on hot paths | S-M | medium | GC alloc per frame with logging on and off |
| P09 | Prewarm shaders; build the `*-Dynamic` highlight materials ahead of the first hover | `DynamicMaterialService.cs:126-160`, no variant collection | Hitch on first use of a shader and on the first hover | S-M | medium | Frame spikes on first hover and first PFX |
| P04 | Streaming budget that respects the frame: no forced minimum when the budget is used up | `FrameSkipperService.cs:21`, `VobService.cs:63, 239` (same as item 24) | Budget is 50 % of a frame, plus at least 10 VOBs and one full `InitNpc` per frame regardless | M | high | Walk from the start to the Old Camp, worst frame times |
| P10 | Cheaper lazy VOB init | `StationaryLight.cs:274` (allocating `OverlapSphere` per light), non-convex `MeshCollider` cooking on the main thread, `new Material` per sub mesh | Hitches while walking through the world | M | medium | Same walk, spikes per VOB type |
| P07 | Distance tiers for awake NPCs (AI, animation, root collision, head look) | `AiHandler.Update`, `AnimationSystem.Update`, `RootCollisionHandler` | Full cost per awake NPC per frame, about 50 NPCs in the Old Camp | M | medium-high | CPU time per NPC count |
| P05 | Texture arrays in a format Quest keeps compressed (ASTC); drop the CPU copy | `TextureCacheService.cs:255-306` | DXT1 is stored as RGBA32 on Quest (8 times the memory); every slice tiled up to 512x512 | M | high (memory) | Texture memory in the memory profiler |
| P06 | Cheaper stationary lights in `Lit/World` | `Lit-World.shader` (up to 16+16 lights per pixel, 512-entry arrays) | Per-pixel light loops, worst at night and in camps | M | medium-high | GPU time at night in the Old Camp |
| P15, P14 | Stagger perception scans; don't recalculate all ~990 routines every game minute | NPC perception and routine services | About N squared over awake NPCs; a spike every game minute | M | medium | Spike once per game minute |
| P18, P19 | Fewer canvases and components per NPC and item; register XRI interactables only when near | VR `oCNpc.prefab` (3 canvases, 4 TMP texts), `oCItem.prefab` (name canvas) | Per-instance overhead on hundreds of objects | M | medium | Canvas and XR Interaction Manager markers |
| P13 | Release VOBs that are far away; runtime meshes not CPU-readable | `VobService.DestroyVob` (not implemented) | Memory only grows while playing | M-L | medium (memory) | Memory after 30 minutes of play |
| P16, P17 | Foliage, water and PFX overdraw; real-time `Light` per light VOB | shaders, `zCVobLight.prefab` | Not judged in depth. **Verify first** | ? | ? | Overdraw view, light count |

Two side observations from the report that are possible bugs, not performance: the renderer has no Decal
feature, so the `DecalProjector`s from `VobDecalMeshBuilder` probably do not draw at all; and an
`OcclusionCullingTool` exists in the Editor folder, but nothing applies occlusion data at runtime.

## Tier 2: critical and medium

| # | Change | Where | Why | Effort | Source |
|---|---|---|---|---|---|
| 14 | One state component for a VR item (`World`, `Held`, `Socketed`, `Ghost`) that applies layer, ignore pairs and transparency | `VRVobItem.cs:117-196, 458-513`, `VRPlayerService.cs:482-520`, `VRBackpack` | 16 fixes in a chain where each fix broke the neighbour. Also deletes the 1 s timer and the dead `IsColliderOverlapping` with its wrong layer mask | M | fix §4 |
| 15 | Read hero and NPC stats from the Daedalus instance everywhere; write the VOB only on save | `NpcService.cs:515` and its 4 callers, `VRRuneCaster.cs:525-617` | 5 fixes and one revert; the removed sync method is back. **Verify first**: readers of `vob.GetAttribute` were counted, not read | M | fix §3 |
| 16 | One "body built" step for NPCs (pose from state, look, hitbox, weapons) | `NpcInitializerDomain`, `AiHandler.cs:61-84` | Each aspect was fixed separately when a body is built late or rebuilt (about 22 fixes with #29) | M | fix §2 |
| 17 | `VrOptionsService` with one declarative option list; menu rows built from it | `VRContextMenuService.cs:301-690`, `VRContextInteractionService.cs:258-292`, `VRConstants.IniNames`, about 20 consumers | Every option is written in 3-5 places; explains the churn in the 3 hottest VR files. Also fixes inconsistent defaults | M | vr 2 |
| 18 | "Gothic.ini loaded" event; move the remaining direct ini reads to the null-safe helpers | `ConfigService.cs:29-40`, 23 direct reads in 10 files | Options read before a game is picked; each consumer has its own guard. Do it together with #17 | S-M | fix §9 |
| 19 | One `WaterLevelDomain` for NPCs, the hero and the transform puppet | `VRSwimDive.cs:190-376`, `NpcWaterService.cs:70-195`, `VRSpellTransformPuppet.cs:267-287` | Three rules can disagree about whether the hero swims; scripts react to `BS_SWIM` | M | vr 5 |
| 20 | `AmbientInfoDomain` owned by `DialogService`, without the Gothic 1 guild table | `NpcService.cs:786-874` | Gothic 2 guilds get no ambient infos. **Verify first** in a Gothic 2 playtest | M | npc 5 |
| 21 | One world-list method in `VobService` for loose world items, then `WorldItemService` with an item list | `VobService.cs:616, 1013`, `VRBackpack.cs:786-788`, `VRStackSplitter.cs:619-620`, `VRFavoritesRing.cs:766` | The same save rule exists in 4 places (item saved twice or not at all); world-item searches loop over all VOBs | S, then M | vr 3a, vob H3.2 |
| 22 | `VobTypeTable`: one table for prefab, load mode and flags per VOB type | `VobInitializerDomain.cs:83, 316, 364, 402, 429`, `VobService.cs:74, 332, 440, 695, 719`, `VobContainer.cs:51`, `GameStateService.cs:137`, `ZenKitExtension.cs:515` | About 13 per-type switches in 5 files that already disagree. Needs #13 decided | M | vob H1 |
| 23 | One passive-perception range rule; hero broadcasts through the deferred bus; extract `NpcPerceptionService` | `NpcAiService.cs:80-240, 1413-1703` | Two paths with different range rules; first and cleanest cut out of the 1715-line class | M | npc 1, 4 |
| 24 | Adaptive forced minimum of lazy VOB builds per frame; pre-cache the light/fire indoor answer | `VobService.cs:63, 239`, `StationaryLightsService.cs:576` | At least 10 builds per frame even when the frame budget is used up. **Needs a Quest 2 measurement** | M | vob M3 |
| 25 | Mob user count and loops instead of LINQ in the mob lookup | `VobService.cs:499-521, 1080-1133` | Cost grows with mobs times NPCs on routine paths | S-M | vob M3 |

## Tier 3: critical and large

Each of these needs the `design` skill and an ADR before coding.

| # | Change | Where | Why | Effort | Source |
|---|---|---|---|---|---|
| 26 | Hero spell protocol into Core (`HeroSpellCastService`), then `SpellHit` as a direct call | `VRRuneCaster.cs:212-766`, `SpellHitDomain.cs:94` | About 550 lines of Gothic rules in a VR adapter; four "mana snaps back" fixes. Do after #15 | L (2 PRs) | vr 1, 12 |
| 27 | Flatten the VOB tree: every VOB gets its own container and loader | `VobInitializerDomain.cs:1019-1035`, `VobService.cs:391-440` | The same `InvalidCastException` was fixed twice, 5 weeks apart; 9 fixes. Touches culling and save games. Do after #22 | L | fix §1 |
| 28 | Split the rest of `NpcAiService` (`AiQueueClearDomain`, `AiStartStateDomain`, `NpcStateTimeDomain`); separate the engine API from the `Ext*` script methods | `NpcAiService.cs` | Most-changed file with the most fixes (30). Do after #23 | L (3 PRs) | npc 1, 3 |
| 29 | Body interface on `NpcContainer` that VR implements for the hero, with a null object for unspawned NPCs | 54 `IsHero(` checks and 165 null checks in Core | Largest payoff in removed guards, largest change. Do after #16 | L | fix §2 |

## Tier 4: useful and small

These don't fix wrong behaviour, but each is cheap and makes the larger items smaller.

| # | Change | Where | Effort | Source |
|---|---|---|---|---|
| 30 | Remove `IVrBackend` (issue #521), see the section below | 6 files in `Gothic-VR/Scripts` | S | #521 |
| 31 | Delete dead code: `DestroyVob`, `GetSoundClip`, `ObjectRoutines`, unused injections, make `CreateVobNpc` private | `VobService.cs:51, 282-285`, `VobInitializerDomain.cs:847`, `NpcAiService.cs:36-38`, `NpcHelperService.cs:35` | S | vob H3.4, npc 12 |
| 32 | Remove the cheap architecture baseline lines 28, 29, 31, 50, 51, 52, 56 | `ArchitectureBaseline.txt` and the files it names | S | vob §4, npc §4 |
| 33 | Shared Core helpers: `GetItemSymbolName()`, `WeaponStateExtension`, one hero check, one `IsInCurrentDialog`, `IsSeat()`, a Daedalus constant helper | 11 + 20 + 40 + 3 call sites, replaced as files get touched | S to create | vob M2, npc 8 |
| 34 | Shared VR helpers: cached `VobContainer` on `VrInteractable`, `TryGetHoldingSide`, item kind checks (`IsScroll`, `IsRune`, `IsTorch`), `IVrBackpackRegistry` | 78 + 5 + 2 call sites | S each | vr 13, 6 |
| 35 | Move the simulator keys into the simulator backend | 10 adapters reading `Keyboard.current` / `Mouse.current` | S-M | vr 10 |
| 36 | Move misplaced jobs: `TA_Min` to `NpcRoutineService`, `Mdl_*` to `NpcLookService`, lock-pick listeners to `MobLockService` | `NpcService.cs:269-307, 620-691`, `VobService.cs:106-113` | S each | npc 5, vob H3.4 |
| 37 | Hero item requirement checks out of `FightService` | `FightService.cs:595-684` | S | npc 6 |
| 38 | Use `IVrSfxPlayer` instead of `AudioSource.PlayClipAtPoint`; `Resources.Load` through `ResourceCacheService` | 7 + 3 VR adapters | S | vr 18, 13e |
| 39 | VR tuning constants out of Core NPC services | `NpcAiService.cs:995-1013, 1157-1161` | S | npc 10 |
| 40 | Write `Docs/vobs.md` (load modes, trigger order, child placement) | new | S | vob L1 |
| 41 | Process: tune VR layout values in the headset through the Marvin menu before committing | 26 blind layout fixes | S | fix §7 |

## Tier 5: useful, medium to large

| # | Change | Where | Effort | Source |
|---|---|---|---|---|
| 42 | `IVobTriggerTarget` / `IVobHandler` interfaces, loading flags on `VobContainer`, then `VobTriggerService` | `VobService.cs:740-951`, `VobLoader`, 6 handlers | M | vob H4, H3.1 |
| 43 | `VrSocketPage` shared by backpack, loot and trade; then split the backpack's trade, loot and page parts | `VRBackpack.cs:843-1000`, `VRNpcLoot.cs:404-494`, `VRTradeCounter.cs:551-633` | M + M | vr 9, 3 |
| 44 | Pickpocket robbery session into Core `PickpocketService`, G1/G2 difference as a registered mode | `VRPickpocket.cs:479-694` | M | vr 4 |
| 45 | `VrItemBehaviourService` instead of the central switch in `VRVobItem.OnGrabbed` | `VRVobItem.cs:114-399` | M | vr 6 |
| 46 | Player features onto the VR player prefab (through Unity MCP); split `VRContextInteractionService` | `VRContextInteractionService.cs:143-240` | M | vr 7 |
| 47 | Trade counter prefab; trade-end rule in `TradeService` | `VRTradeCounter.cs:166-183, 343-443` | M | vr 14 |
| 48 | `NpcHandSlotDomain`, `NpcVoiceDomain`, hero rig provider | `NpcService.cs:492-672, 897-968`, `FightService.cs:514-540, 686-818` | M | npc 11, §2 |
| 49 | VOB builders per family out of `InitVob` | `VobInitializerDomain.cs:69-327` | L (3-4 PRs) | vob H2 |
| 50 | `VobProperties2` family down to 3 classes in `Models/` | `Adapters/Properties/Vobs/*` | M | vob M4 |
| 51 | `NpcProperties` to `Models/`; adapter ports; state machine out of `AiHandler` | `Adapters/Properties/NpcProperties.cs`, `AiHandler.cs` | L | npc 2 |
| 52 | Split `AnimationSystem` (track player, event domain, root motion) | `Adapters/Animations/AnimationSystem.cs` | L (3 PRs) | vob §6 |
| 53 | Puppet animation domain; seams in `VRFavoritesRing` and `VrPhysicsHand` | VR adapters | M each | vr 16, 19 |

## Issue #521: remove `IVrBackend`

The issue asks to remove the abstraction that let HVR and XRI live side by side, now that HVR is gone.

What exists today:

- `IVrBackend` (`Interaction/Ports/IVrBackend.cs`) has one implementation, `XriBackend`. It is used in 6 files:
  `ReflexVRSceneInstaller.cs:23-25`, `XriSocketBridge.cs:29`, `XriInteractableBridge.cs:33`, `VrLadderExit.cs:28`,
  `VrHinge.cs:78`, `VRContextInteractionService.cs:50, 177-180`.
- Around it sit the "does this belong to the active backend" checks, which are always true now:
  `IsActive` on `IVrInteractableBackend`, `IVrSocketBackend`, `IVrHinge` and `IVrSocketContainer`, and the loops
  that search for the active one (`VrInteractable.cs:105-114`, `VrSocketSlot.cs:464`, `VrHinges.cs:22`,
  `VRBackpackVacuum.cs:115`).

Two possible scopes:

1. **Narrow (recommended, S).** Delete `IVrBackend`, bind the XRI implementations directly in
   `ReflexVRSceneInstaller`, pick the player by its rig without `OwnsRig`, and remove the `IsActive` checks and
   search loops. Game code keeps talking to the ports (`IVrInput`, `IVrRig`, `IVrHaptics`, ...).
2. **Wide (L, not recommended).** Also remove the ports and let game code call XRI types directly. This reverses
   the rule in `CLAUDE.md` ("Game code talks to the ports, never to XRI types"), and the VR audit found that
   boundary clean and worth keeping.

Notes for the narrow scope:

- The expected runtime gain is small: the active-backend searches are cached per component. The real gain is less
  code and one less concept.
- Check the shared prefabs for leftover per-backend bridge or container components before removing the loops.
  Prefab inspection and changes go through Unity MCP.
- It removes a decision (a pluggable backend), so the PR needs a short ADR that replaces the relevant part of
  `Docs/HVR-replacement/`.
- The issue text says "speed up lookup costs". If that refers to something beyond the `IsActive` searches, the
  scope should be confirmed with the issue author.

## Open decisions

| Decision | Blocks |
|---|---|
| Is `oCTouchDamage` a placeholder or a built VOB? | #13, #22 |
| When a lock pick breaks, reduce the world VOB amount, the inventory item, or both? | #9 |
| Is the "doors use VR hinge physics" rule meant for Flat too? | vob M1 |
| Was the forced minimum of 10 VOB builds per frame measured on Quest 2 during play? | #24 |
| May thin Daedalus externals live in the `Vm*ExternalDomain` directly? (ADR) | #28 |
| Does "services don't use adapters" apply inside Gothic-VR? 16 files break it and the doc is silent | vr 8 |
| Are plain worker classes owned by an adapter allowed? | vr 20 |
| Do both dialog hold modes stay? Each doubles the playtest surface | fix §5 |
| Issue #521: narrow or wide scope? | #30 |

## Not covered by this plan

- Measured performance data. The performance track is a code and settings review; the OpenXR settings, the SRP
  Batcher switch and the renderer asset could not be inspected.
- Areas the reports counted but did not analyse: VR hands and rig (33 fixes), favorites ring, spells, audio,
  animation, caches and textures.
- `DialogService`, most `Domain/Npc` class bodies, prefab, scene and asset diffs.
- The architecture check itself was not run (no `dotnet` on this machine).
