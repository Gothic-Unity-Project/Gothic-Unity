# Action plan after the AI-change audit

- Date: 2026-11-16, repository state `main` at `ffd7f5ec8`
- Built from the five reports in this folder: `fix-clusters.md`, `architecture-core-vobs.md`,
  `architecture-core-npc.md`, `architecture-vr-adapters.md` and `performance-quest2.md`, plus GitHub issue #521.
- Performance for Quest 2 is part of the same list. Those items carry `perf` in the Source column.
- `settings-inspection.md` holds the render, OpenXR and quality settings as read from the Editor on 2026-11-16
  (source `settings`).

## How this list is ordered

Most critical and least complex first. Work top to bottom.

- **Critical** means one of: the player sees wrong behaviour, it costs frame time or memory on Quest 2, or it ends
  a series of repeated fixes.
- **Effort**: S = up to half a day, one small PR. M = one to three days, one PR with a short design. L = several
  PRs, needs the `design` skill and an ADR first.
- **Verify first** marks findings that come from code reading only and should be confirmed (in code, in the
  Editor or in game) before changing anything. Nothing in the reports was verified at runtime.
- **Performance items are estimates.** The performance review was static: nothing was measured. Items 1 and 2
  come first for that reason. The Quest 2 target is 72 Hz, about 13.8 ms per frame for CPU and GPU together.
- "Source" names the report and finding to read for details: `fix` = fix-clusters, `vob` = architecture-core-vobs,
  `npc` = architecture-core-npc, `vr` = architecture-vr-adapters, `perf` = performance-quest2.

## Tier 1: critical and small

| # | Done | Change | Where | Why | Effort | Source |
|---|---|---|---|---|---|---|
| 1 |  | Take a performance baseline on a real Quest 2 | the 8 scenes and target values of the measurement plan in `performance-quest2.md` | Without it no performance item below can be confirmed, and some may turn out smaller than they read | S | perf §6 |
| 2 | ✅ | **Done 2026-11-16**: settings the review could not inspect, see `settings-inspection.md` | URP assets, renderer, OpenXR for Android, VR camera, `Production.asset`, `zCVobLight.prefab` | Result: no hidden full-screen passes (depth/opaque texture, depth priming, post-processing all off); items 4, 5 and 5a confirmed | S | settings |
| 3 |  | Stop animating and skinning NPCs that are not visible | `AbstractMeshBuilder.cs:311` (`updateWhenOffscreen = true`), `AnimationSystem.cs:338` (`AlwaysAnimate`) | Every NPC within 50 m animates and skins every frame, visible or not; NPC culling is by distance only. Confirm with an A/B toggle in the Old Camp | S | perf P01 |
| 4 |  | Enable foveated rendering for Quest and set a level in code; A/B MSAA 2x and render scale | OpenXR settings for Android: Foveated Rendering feature is disabled, API is `Legacy`; no code sets a level | **Confirmed by the settings.** Full-resolution rendering in the periphery on a fill-rate-limited GPU. Confirm the gain with GPU frame time in OVR Metrics Tool | S | perf P03, settings |
| 5 |  | Make `Lit/World` SRP Batcher compatible; share materials instead of `new Material` per renderer | `Lit-World.shader:34` (`_FocusBrightness` outside the `UnityPerMaterial` block at lines 71-78) | **Confirmed by the shader source**: the SRP Batcher is on, but this shader can't use it, so each renderer gets its own draw call setup. Check the other custom shaders too | S | perf P02, settings |
| 5a |  | Disable the Unity `Light` component on light VOBs (keep it as data only) | `zCVobLight.prefab` (enabled real-time point light), `StationaryLight.cs` | **Confirmed by the settings**: the world shaders do their own lighting and never read URP's lights, so every light VOB costs light culling for almost nothing. **Verify first** what `Lit-Misc` (the one shader that reads them) is used for | S | perf P17, settings |
| 6 |  | EditMode test that builds the Reflex container and resolves every registered service | `Assets/Gothic-Tests/EditMode/`, `ReflexProjectInstaller`, `ReflexVRSceneInstaller` | Three "the game / VR scene starts again" fixes were service cycles that only fail at game start | S | fix §9 |
| 7 |  | EditMode test that compares `DeveloperConfig` fields with the values serialized in `Production.asset` | `Assets/Gothic-Tests/EditMode/`, `DeveloperConfig.cs` | 10 asset-only repair commits; NPC archers shipped melee-only because a flag was off in the asset | S | fix §8 |
| 8 |  | Fixed timestep matched to the display; lower the maximum allowed timestep | `ProjectSettings/TimeManager.asset` (0.02 s and 0.333 s) | 50 Hz physics against a 72 Hz display gives uneven hand physics; up to 16 physics steps run after one hitch | S | perf P08 |
| 9 |  | Enable `VRFocus` only while highlighted or locked; tell the one locked NPC directly | `Gothic-VR/Scripts/Adapters/VRFocus.cs:79, 236-270` | `LateUpdate` runs on every item, NPC and mob instance | S | vr 11 |
| 10 |  | Index `instance index -> NpcContainer` for `Hlp_GetNpc` | `NpcService.cs:320, 394-407`, `MultiTypeCacheService` | Whole-world scan with a closure on one of the most-called externals | S | npc 7 |
| 11 |  | Cache "is state perception" per function index; swap the perception lists in one explicit per-frame tick | `NpcAiService.cs:1476-1586` | String lookups and allocations per receiver when perceptions are pending | S | npc 9 |
| 12 |  | No vertex array allocation per frame in morph animation | `VobAnimateMorph` | Garbage every frame leads to GC hitches | S | perf P11 |
| 13 |  | Fix the `SpawnVOBTypes` dev filter with one `IsVobTypeSpawned(type)` | `VobService.cs:385, 560, 987` | One of the three uses is inverted. **Verify first** which one | S | vob M2 |
| 14 |  | Replace `CallVmFunctionWithNpcGlobals` with `VmGlobalsScope`; decide the null rule once | `NpcAiService.cs:210-230`, `Domain/Vm/VmGlobalsScope.cs` | Two contradicting rules for which VM globals stay set. **Verify first** against OpenGothic | S | npc 4 |
| 15 |  | Real lock-pick rule in Core instead of `Random.value > 0.5f` | `VRContainerDoorPickingInteraction.cs:206-250`, new `LockPickDomain` | Placeholder break chance (marked FIXME) decides gameplay | S | vr 15 |
| 16 |  | Decide and fix the double reaction to a broken lock pick | `VobService.cs:106`, `PlayerService.cs:72` | Two listeners react to one event (amount and inventory). **Needs your decision** | S | vob M2 |
| 17 |  | Check whether decals draw at all | URP renderer (no Decal feature), `VobDecalMeshBuilder` | The `DecalProjector`s probably render nothing. **Verify first** in game | S | perf, side note |
| 18 |  | Use `VrSwingSampler` for the dig spot | `VRDigSpot.cs:87` | Last leftover of the world-space swing measurement that caused false attacks | S | fix §6 |
| 19 |  | Remove the listeners that are never unsubscribed | `VRSwimDive.cs:108, 160`, `VRSpectatorCamera.cs:60, 81` | Handlers pile up and hold destroyed objects if the player scene is rebuilt | S | vr 17 |
| 20 |  | `HeroProvider` that services inject when they only need the hero | new, replaces `NpcService.GetHeroContainer()` uses in services that cycle | `NpcService` is the hub most DI cycles run through | S | fix §9 |
| 21 |  | Decide whether `oCTouchDamage` is a placeholder or a built VOB | `VobInitializerDomain.cs:233, 407`, `VobService.cs:398` | It is both today. **Needs your decision** | S | vob H1 |

## Tier 2: critical and medium

| # | Done | Change | Where | Why | Effort | Source |
|---|---|---|---|---|---|---|
| 22 |  | Streaming budget that respects the frame: no forced minimum when the budget is used up; pre-cache the light/fire indoor answer | `FrameSkipperService.cs:21`, `VobService.cs:63, 239`, `StationaryLightsService.cs:576` | Budget is 50 % of a frame, plus at least 10 VOBs and one full `InitNpc` per frame regardless. Confirm with the worst frame times on the walk from the start to the Old Camp | M | perf P04, vob M3 |
| 23 |  | Distance tiers for awake NPCs (AI, animation, root collision, head look) | `AiHandler.Update`, `AnimationSystem.Update`, `RootCollisionHandler` | Full cost per awake NPC per frame, about 50 NPCs in the Old Camp | M | perf P07 |
| 24 |  | Texture arrays in a format Quest keeps compressed (ASTC); drop the CPU copy | `TextureCacheService.cs:255-306` | DXT1 is stored as RGBA32 on Quest (8 times the memory); every slice is tiled up to 512x512. Confirm in the memory profiler | M | perf P05 |
| 25 |  | Cheaper stationary lights in `Lit/World` | `Lit-World.shader` (up to 16+16 lights per pixel, 512-entry arrays) | Per-pixel light loops, worst at night and in camps. Confirm with GPU time at night in the Old Camp | M | perf P06 |
| 26 |  | One state component for a VR item (`World`, `Held`, `Socketed`, `Ghost`) that applies layer, ignore pairs and transparency | `VRVobItem.cs:117-196, 458-513`, `VRPlayerService.cs:482-520`, `VRBackpack` | 16 fixes in a chain where each fix broke the neighbour. Also deletes the 1 s timer and the dead `IsColliderOverlapping` with its wrong layer mask | M | fix §4 |
| 27 |  | Read hero and NPC stats from the Daedalus instance everywhere; write the VOB only on save | `NpcService.cs:515` and its 4 callers, `VRRuneCaster.cs:525-617` | 5 fixes and one revert; the removed sync method is back. **Verify first**: readers of `vob.GetAttribute` were counted, not read | M | fix §3 |
| 28 |  | One "body built" step for NPCs (pose from state, look, hitbox, weapons) | `NpcInitializerDomain`, `AiHandler.cs:61-84` | Each aspect was fixed separately when a body is built late or rebuilt (about 22 fixes with #45) | M | fix §2 |
| 29 |  | `VrOptionsService` with one declarative option list; menu rows built from it | `VRContextMenuService.cs:301-690`, `VRContextInteractionService.cs:258-292`, `VRConstants.IniNames`, about 20 consumers | Every option is written in 3-5 places; explains the churn in the 3 hottest VR files. Also fixes inconsistent defaults | M | vr 2 |
| 30 |  | "Gothic.ini loaded" event; move the remaining direct ini reads to the null-safe helpers | `ConfigService.cs:29-40`, 23 direct reads in 10 files | Options read before a game is picked; each consumer has its own guard. Do it together with #29 | S-M | fix §9 |
| 31 |  | Cheaper lazy VOB init | `StationaryLight.cs:274` (allocating `OverlapSphere` per light), non-convex `MeshCollider` cooking on the main thread, `new Material` per sub mesh | Hitches while walking through the world | M | perf P10 |
| 32 |  | Prewarm shaders; build the `*-Dynamic` highlight materials ahead of the first hover | `DynamicMaterialService.cs:126-160`, no variant collection | Hitch on first use of a shader and on the first hover | S-M | perf P09 |
| 33 |  | Check the log level before building the message; no stack trace for normal logs | 522 `Logger.Log` call sites | String formatting and stack traces on hot paths | S-M | perf P12 |
| 34 |  | One `WaterLevelDomain` for NPCs, the hero and the transform puppet | `VRSwimDive.cs:190-376`, `NpcWaterService.cs:70-195`, `VRSpellTransformPuppet.cs:267-287` | Three rules can disagree about whether the hero swims; scripts react to `BS_SWIM` | M | vr 5 |
| 35 |  | `AmbientInfoDomain` owned by `DialogService`, without the Gothic 1 guild table | `NpcService.cs:786-874` | Gothic 2 guilds get no ambient infos. **Verify first** in a Gothic 2 playtest | M | npc 5 |
| 36 |  | One world-list method in `VobService` for loose world items, then `WorldItemService` with an item list | `VobService.cs:616, 1013`, `VRBackpack.cs:786-788`, `VRStackSplitter.cs:619-620`, `VRFavoritesRing.cs:766` | The same save rule exists in 4 places (item saved twice or not at all); world-item searches loop over all VOBs | S, then M | vr 3a, vob H3.2 |
| 37 |  | `VobTypeTable`: one table for prefab, load mode and flags per VOB type | `VobInitializerDomain.cs:83, 316, 364, 402, 429`, `VobService.cs:74, 332, 440, 695, 719`, `VobContainer.cs:51`, `GameStateService.cs:137`, `ZenKitExtension.cs:515` | About 13 per-type switches in 5 files that already disagree. Needs #21 decided | M | vob H1 |
| 38 |  | One passive-perception range rule; hero broadcasts through the deferred bus; extract `NpcPerceptionService` | `NpcAiService.cs:80-240, 1413-1703` | Two paths with different range rules; first and cleanest cut out of the 1715-line class | M | npc 1, 4 |
| 39 |  | Stagger perception scans; don't recalculate all ~990 routines every game minute | NPC perception and routine services | About N squared over awake NPCs; a spike every game minute. Easier after #38 | M | perf P15, P14 |
| 40 |  | Mob user count and loops instead of LINQ in the mob lookup | `VobService.cs:499-521, 1080-1133` | Cost grows with mobs times NPCs on routine paths | S-M | vob M3 |
| 41 |  | Fewer canvases and components per NPC and item; register XRI interactables only when near | VR `oCNpc.prefab` (3 canvases, 4 TMP texts), `oCItem.prefab` (name canvas), through Unity MCP | Per-instance overhead on hundreds of objects | M | perf P18, P19 |

## Tier 3: critical and large

Each of these needs the `design` skill and an ADR before coding.

| # | Done | Change | Where | Why | Effort | Source |
|---|---|---|---|---|---|---|
| 42 |  | Hero spell protocol into Core (`HeroSpellCastService`), then `SpellHit` as a direct call | `VRRuneCaster.cs:212-766`, `SpellHitDomain.cs:94` | About 550 lines of Gothic rules in a VR adapter; four "mana snaps back" fixes. Do after #27 | L (2 PRs) | vr 1, 12 |
| 43 |  | Flatten the VOB tree: every VOB gets its own container and loader | `VobInitializerDomain.cs:1019-1035`, `VobService.cs:391-440` | The same `InvalidCastException` was fixed twice, 5 weeks apart; 9 fixes. Touches culling and save games. Do after #37 | L | fix §1 |
| 44 |  | Split the rest of `NpcAiService` (`AiQueueClearDomain`, `AiStartStateDomain`, `NpcStateTimeDomain`); separate the engine API from the `Ext*` script methods | `NpcAiService.cs` | Most-changed file with the most fixes (30). Do after #38 | L (3 PRs) | npc 1, 3 |
| 45 |  | Body interface on `NpcContainer` that VR implements for the hero, with a null object for unspawned NPCs | 54 `IsHero(` checks and 165 null checks in Core | Largest payoff in removed guards, largest change. Do after #28 | L | fix §2 |
| 46 |  | Release VOBs that are far away; runtime meshes not CPU-readable | `VobService.DestroyVob` (not implemented) | Memory only grows while playing. Confirm with memory after 30 minutes of play. Easier after #43 | M-L | perf P13 |

## Tier 4: useful and small

These don't fix wrong behaviour, but each is cheap and makes the larger items smaller.

| # | Done | Change | Where | Effort | Source |
|---|---|---|---|---|---|
| 47 | ✅ | **Done 2026-11-16** (narrow scope, `Xri` prefix kept; ADR `2026-11-16-remove-vr-backend-abstraction.md`): remove `IVrBackend` (issue #521), see the section below | 6 files in `Gothic-VR/Scripts` | S | #521 |
| 48 |  | Delete dead code: `GetSoundClip`, `ObjectRoutines`, unused injections, make `CreateVobNpc` private. `DestroyVob` stays until #46 is decided | `VobService.cs:51`, `VobInitializerDomain.cs:847`, `NpcAiService.cs:36-38`, `NpcHelperService.cs:35` | S | vob H3.4, npc 12 |
| 49 |  | Remove the cheap architecture baseline lines 28, 29, 31, 50, 51, 52, 56 | `ArchitectureBaseline.txt` and the files it names | S | vob §4, npc §4 |
| 50 |  | Shared Core helpers: `GetItemSymbolName()`, `WeaponStateExtension`, one hero check, one `IsInCurrentDialog`, `IsSeat()`, a Daedalus constant helper | 11 + 20 + 40 + 3 call sites, replaced as files get touched | S to create | vob M2, npc 8 |
| 51 |  | Shared VR helpers: cached `VobContainer` on `VrInteractable`, `TryGetHoldingSide`, item kind checks (`IsScroll`, `IsRune`, `IsTorch`), `IVrBackpackRegistry` | 78 + 5 + 2 call sites | S each | vr 13, 6 |
| 52 |  | Move the simulator keys into the simulator backend | 10 adapters reading `Keyboard.current` / `Mouse.current` | S-M | vr 10 |
| 53 |  | Move misplaced jobs: `TA_Min` to `NpcRoutineService`, `Mdl_*` to `NpcLookService`, lock-pick listeners to `MobLockService` | `NpcService.cs:269-307, 620-691`, `VobService.cs:106-113` | S each | npc 5, vob H3.4 |
| 54 |  | Hero item requirement checks out of `FightService` | `FightService.cs:595-684` | S | npc 6 |
| 55 |  | Use `IVrSfxPlayer` instead of `AudioSource.PlayClipAtPoint`; `Resources.Load` through `ResourceCacheService` | 7 + 3 VR adapters | S | vr 18, 13e |
| 56 |  | VR tuning constants out of Core NPC services | `NpcAiService.cs:995-1013, 1157-1161` | S | npc 10 |
| 57 |  | Write `Docs/vobs.md` (load modes, trigger order, child placement) | new | S | vob L1 |
| 58 |  | Process: tune VR layout values in the headset through the Marvin menu before committing | 26 blind layout fixes | S | fix §7 |

## Tier 5: useful, medium to large

| # | Done | Change | Where | Effort | Source |
|---|---|---|---|---|---|
| 59 |  | `IVobTriggerTarget` / `IVobHandler` interfaces, loading flags on `VobContainer`, then `VobTriggerService` | `VobService.cs:740-951`, `VobLoader`, 6 handlers | M | vob H4, H3.1 |
| 60 |  | `VrSocketPage` shared by backpack, loot and trade; then split the backpack's trade, loot and page parts | `VRBackpack.cs:843-1000`, `VRNpcLoot.cs:404-494`, `VRTradeCounter.cs:551-633` | M + M | vr 9, 3 |
| 61 |  | Pickpocket robbery session into Core `PickpocketService`, G1/G2 difference as a registered mode | `VRPickpocket.cs:479-694` | M | vr 4 |
| 62 |  | `VrItemBehaviourService` instead of the central switch in `VRVobItem.OnGrabbed` | `VRVobItem.cs:114-399` | M | vr 6 |
| 63 |  | Player features onto the VR player prefab (through Unity MCP); split `VRContextInteractionService` | `VRContextInteractionService.cs:143-240` | M | vr 7 |
| 64 |  | Trade counter prefab; trade-end rule in `TradeService` | `VRTradeCounter.cs:166-183, 343-443` | M | vr 14 |
| 65 |  | `NpcHandSlotDomain`, `NpcVoiceDomain`, hero rig provider | `NpcService.cs:492-672, 897-968`, `FightService.cs:514-540, 686-818` | M | npc 11, §2 |
| 66 |  | VOB builders per family out of `InitVob` | `VobInitializerDomain.cs:69-327` | L (3-4 PRs) | vob H2 |
| 67 |  | `VobProperties2` family down to 3 classes in `Models/` | `Adapters/Properties/Vobs/*` | M | vob M4 |
| 68 |  | `NpcProperties` to `Models/`; adapter ports; state machine out of `AiHandler` | `Adapters/Properties/NpcProperties.cs`, `AiHandler.cs` | L | npc 2 |
| 69 |  | Split `AnimationSystem` (track player, event domain, root motion). Makes #23 easier to test | `Adapters/Animations/AnimationSystem.cs` | L (3 PRs) | vob §6 |
| 70 |  | Puppet animation domain (also removes its per-frame allocations); seams in `VRFavoritesRing` and `VrPhysicsHand` | VR adapters | M each | vr 16, 19 |
| 71 |  | Foliage, water and PFX overdraw. Not judged in depth, **verify first** with an overdraw view | shaders | unknown | perf P16 |

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
  Prefab inspection and changes go through Unity MCP. **Checked**: each prefab carries at most one bridge and one
  hinge.
- It removes a decision (a pluggable backend), so the PR needs a short ADR. **Done**:
  `Docs/adr/2026-11-16-remove-vr-backend-abstraction.md` replaces decision 2 of `Docs/adr/2026-10-28-hvr-removal.md`.
- The issue text says "speed up lookup costs". If that refers to something beyond the `IsActive` searches, the
  scope should be confirmed with the issue author.

## Open decisions

| Decision | Blocks |
|---|---|
| Is `oCTouchDamage` a placeholder or a built VOB? | #21, #37 |
| When a lock pick breaks, reduce the world VOB amount, the inventory item, or both? | #16 |
| Is the "doors use VR hinge physics" rule meant for Flat too? | vob M1 |
| Was the forced minimum of 10 VOB builds per frame measured on Quest 2 during play? | #22 |
| Should occlusion data be applied at runtime? An `OcclusionCullingTool` exists in the Editor folder, but nothing uses its data in play | perf, side note |
| May thin Daedalus externals live in the `Vm*ExternalDomain` directly? (ADR) | #44 |
| Does "services don't use adapters" apply inside Gothic-VR? 16 files break it and the doc is silent | vr 8 |
| Are plain worker classes owned by an adapter allowed? | vr 20 |
| Do both dialog hold modes stay? Each doubles the playtest surface | fix §5 |
| Issue #521: narrow or wide scope? **Decided 2026-11-16: narrow**, see `Docs/adr/2026-11-16-remove-vr-backend-abstraction.md` | #47 |

## Not covered by this plan

- Measured performance data. The performance review and the settings inspection (`settings-inspection.md`) read
  code and project files; nothing was profiled.
- The Pico build's OpenXR features: all PICO features are disabled in the settings asset, and whether the Pico
  build switches them on was not inspected.
- Areas the reports counted but did not analyse: VR hands and rig (33 fixes), favorites ring, spells, audio,
  animation, caches and textures.
- `DialogService`, most `Domain/Npc` class bodies, prefab, scene and asset diffs.
- The architecture check itself was not run (no `dotnet` on this machine).
