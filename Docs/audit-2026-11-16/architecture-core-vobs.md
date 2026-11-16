# Architecture audit: VOB creation (Domain/Vobs, Services/Vobs, Core VOB adapters) + AnimationSystem

## Summary
- **Verdict: needs redesign, in small steps.** No single change is wrong. The drift is in how many places know each VOB type: **about 13 per-type switches or lists in 5 files**, and they already disagree with each other.
- `VobService.cs` (1135 lines) holds **6 use cases**: world load, lazy init queue, world items, mob slots, trigger dispatch, lock-pick and routine leftovers. `VobInitializerDomain.cs` (1204 lines) is one builder for about 30 VOB types, run by a 260-line `InitVob` switch.
- The Service/Domain line is real for "build the GameObject of one VOB". It is fiction for "which types are eager, unbuilt or disabled" and for "who registers named VOBs": both files decide those, in different places.
- Most baseline lines in this area come from one root: **loading state lives on the `VobLoader` MonoBehaviour** (`IsLoaded`, `IsQueued`), and **trigger targets are reached through `GetComponentInChildren<XHandler>()`**. Two small interfaces plus moving the flags onto `VobContainer` clear about 6 baseline lines.
- Quest cost: up to 10 forced VOB builds per frame during play, a mob lookup that grows with mobs x NPCs, and world-item searches that loop over every VOB in the world.

---

## 1. Responsibility maps

### `Assets/Gothic-Core/Scripts/Domain/Vobs/VobInitializerDomain.cs` (read fully)

| Lines | Responsibility |
|---|---|
| 41-55 | 12 injected services, plus game state `_createdEarlyVobIds`. The doc says a Domain holds no game state. |
| 69-327 | `InitVob`: a per-type switch (83-274, about 30 cases), then placement, wind, world light, own loader and children (276-326). One method of about 260 lines. |
| 333-359 | Child tree walk and `PlaceAtOffsetToParent` |
| 364-409 | Type predicates: `IsScaledToBoundingBox`, `IsUnbuiltVobType` (public static, also used by VobService), `IsVisualDisabled` (public, also used by VobService) |
| 375-383 | `MarkCreatedEarly` / `ClearCreatedEarly`. Their lifetime is driven by VobService. |
| 411-422 | Pass-throughs: `SetPosAndRot` (used by VobService), `IsLockPick` (wraps `Constants`) |
| 424-580 | `GetPrefab`: a second per-type switch, from VOB type to `PrefabType`, with name heuristics |
| 586-630 | Fire: fire-tree world, slot lookup, `FireService.Register` |
| 632-662 | Light: `StationaryLight` setup, lightmap-near query |
| 664-684 | Mover |
| 686-763 | Items: two almost identical `CreateItemMesh` overloads (717, 734), mob container |
| 766-888 | Sound and sound-daytime: AudioSource setup, async decode, **resolving the SFX file name** (an audio concern). `GetSoundClip` (847) has no caller. |
| 890-989 | Zones: music, change level, trigger zone, trigger script. Four copies of the bounding-box placement. |
| 911-938 | Free point: writes `GameStateService.FreePoints`, adds debug renderers with `AddComponent` |
| 991-1011 | Animated VOB, dig-spot snap (a G2 decal quirk fixed at the call site) |
| 1013-1042 | Child `VobLoader` (`AddComponent`) and named-VOB registration |
| 1044-1154 | Mesh building: mdl / mdh+mdm / mmb / mrm, four branches each ending in the same "destroy on null" code |
| 1156-1202 | Decal, PFX, PFX controller effect, debug cube |

### `Assets/Gothic-Core/Scripts/Services/Vobs/VobService.cs` (read fully)

| Use case | Lines | Callers |
|---|---|---|
| A. World load (load phase) | 74-97, 120-143, 153-167, 287-468, 675-738 | WorldScene, BootstrapService |
| B. Lazy init queue (`ILazyInitQueue`) | 57-68, 172-245 | culling event, SurroundingsBuildService, AnimationSystem (`HasPendingVobInits`) |
| C. World items | 251-285, 470-491, 550-617, 956-1074 | VR (RemoveWorldItem 12x, CreateItem 8x, UntrackVobFromCache, DropItemAtPosition), NpcService, FightService, TakeItem, VmWorldExternalDomain |
| D. Mob slots and availability | 499-521, 620-669, 1080-1133 | NpcHelperService (Wld_IsMobAvailable), UseMob |
| E. Trigger dispatch | 740-951, 1093-1102 | 6 handlers in Adapters/Vob, MoverAdapter, MobInteractionService, VmWorldExternalDomain (Wld_SendTrigger) |
| F. Leftovers | 51 `ObjectRoutines`: written by `Wld_SetObjectRoutine`, read by nobody, while `MobRoutineService` already exists. 106-113: lock-pick rules, while `MobLockService` exists and `PlayerService:72` also listens to `LockPickComboBroken`. 282-285: `DestroyVob` throws `NotImplementedException`. 528-548: `Mob_HasItems`. 148: `IsVobCollider`. |

---

## 2. Is the Service/Domain split meaningful?

Partly.
- **The Domain does the Service's bookkeeping.** It writes `GameStateService.FreePoints` (935), `RegisterNamedVob` (1032), `FireService.Register` (616), `WindService.Register` (310) and the sound culling entries (780, 803). It acts as a "service of services", not a worker.
- **The Service reaches into the Domain** for type knowledge (`IsUnbuiltVobType`, `IsVisualDisabled`, 398), for the created-early bookkeeping (134, 427) and for a one-line transform helper (455).
- **Named-VOB registration happens twice**: VobService:717 for root VOBs, VobInitializerDomain:1032 for child VOBs.
- **The "visual disabled" rule is written three times**: VobService:678-690, Domain 126/133, Domain 388.
- The fix is not to merge the two files. Give each a clear job: the Service decides **when and whether** a VOB is built and registered; the builders decide **how**; a type table decides **what** a type is (see H1 and H2).

---

## 3. Findings (ranked)

**H1 [high] - per-type knowledge spread over about 13 switches and lists; they already disagree** (principle 3, principle 8)
- Where:
  - `VobInitializerDomain.cs`: 83, 316, 364, 402, 429
  - `VobService.cs`: 74, 332, 440, 695/701, 719
  - `Models/Container/VobContainer.cs`: 51
  - `Services/GameStateService.cs`: 137
  - `Extensions/ZenKitExtension.cs`: 515 (`IsLookedUpByName`)
- How they already disagree:
  - `oCTouchDamage` is in `IsUnbuiltVobType` (407) but also has a real build case (233), so VobService:398 treats it as a placeholder for child handling.
  - `oCMobInter` (145-157) has a seat branch that does the same thing as the default.
  - The bench/chair/throne/barrelo name list is written twice (146, 474).
  - `oCMobDoor` gets `BedProperties` (VobContainer:79).
  - `oCMOB` never gets `MovableProperties`, so its `MOBNAME_` focus name is never used.
- Why it matters: adding or changing one VOB type means editing 5 files, and nothing tells you that you missed one.
- Fix: add `Domain/Vobs/VobTypeTable.cs`, a static `IReadOnlyDictionary<VirtualObjectType, VobTypeInfo>`.
  - `VobTypeInfo` holds: `PrefabType Prefab`, `VobLoadMode LoadMode` (Lazy / Eager / EagerLogic / Placeholder / Skip), `IsScaledToBoundingBox`, `ReceivesWorldLight`, `IsLookedUpByName`, `IsMobInteractable`.
  - `GetPrefab` keeps only the 3 data-driven special cases (item weapon/lockpick, oCMobInter dig spot/seat, door-bed/lidless container) as named methods.
  - Name the seat list once, as `ZenKitVobExtension.IsSeat()`.
- Effort: M (one PR, no behaviour change except the touch-damage mismatch, which must be decided on purpose).

**H2 [high] - `VobInitializerDomain.InitVob` is a 260-line type switch over a 1200-line class** (principle 8, principle 6)
- Where: `VobInitializerDomain.cs:69-327`
- Why it matters: 46 commits in 8 weeks all land in the same file and method, which means merge conflicts and every reader opening the whole thing.
- Fix: one builder per Gothic family behind `internal interface IVobBuilder { GameObject Build(in VobBuildContext ctx); }` in `Domain/Vobs/Builders/`.
  - `VobBuildContext` holds the vob, parent, world position, `isRelativeToParent` and the parent pose.
  - The builders are created by `VobInitializerDomain` (`new XBuilder().Inject()`, keyed by type in a private dictionary). Do not register them in Reflex yet: there is no second implementation (YAGNI).

  | Builder | Takes over (lines) |
  |---|---|
  | `MeshVobBuilder` | 1044-1154, the shared `CreateDefaultMesh` |
  | `ItemVobBuilder` | 686-748, plus the public `CreateItemMesh` used by VobService |
  | `SoundVobBuilder` | 766-845; move `GetSoundFileName` (857-888) to `AudioService` / an `SfxDomain`; delete `GetSoundClip` |
  | `LightVobBuilder` | 632-662 |
  | `FireVobBuilder` | 586-630 |
  | `MoverVobBuilder` | 664-684 |
  | `ZoneVobBuilder` | 890-989: one `PlaceAtBoundingBox(go, vob, resetRotation, minSize)` instead of 4 copies |
  | `LogicVobBuilder` | the 6 "GetPrefab + name + `GetComponent<X>().Init(vob)`" cases |
  | `SpotVobBuilder` | 911-938 |
  | `PfxVobBuilder` | decal, pfx, pfx controller, 1156-1192 |

  - `VobInitializerDomain` keeps the tree walk, placement and post-steps (about 200 lines).
  - Callers outside are unaffected: only `VobService` uses the domain.
- Effort: L as a whole. Do it as 3-4 PRs, one family each; start with zones plus logic VOBs, the cheapest.

**H3 [high] - `VobService` is 6 services in one** (principle 8, DDD application service)
- Where: `VobService.cs` (map above)
- Fix, in order of value:
  1. **`VobTriggerService`** (`Services/Vobs/`): use case E, about 230 lines, about 20 call sites to repoint. Together with H4.
  2. **`WorldItemService`** (`Services/Vobs/`): use case C. Today 3 places build an item VOB with different follow-up steps:
     - `ExtWldInsertItem` 577-589: lazy, raises `WorldItemInserted`.
     - `DropItemAtPosition` 999-1013: eager, no event.
     - `CreateItem(string)` 484: no save, no culling.
     One `SpawnWorldItem(instance, pose, mode)` makes that explicit. Callers: mostly VR adapters and services (about 25 call sites).
  3. **Mob slot queries**: move use case D into a `MobSlotService`, or into `MobInteractionService` if it stays under ~400 lines. `ExtMobHasItems` goes with the chest code.
  4. **Leftovers** (S each):
     - `ObjectRoutines`: move into `MobRoutineService`, or delete it, since nobody reads it.
     - Lock-pick listeners (106-113): move into `MobLockService`.
     - Delete `DestroyVob`.
  - `VobService` keeps use cases A and B (about 450 lines). Later, move `CreateWorldVobs` / `CreateLogicDescendants` / `PreLoadVob` / `CreateVobLazily` into a `WorldVobLoadDomain`.
- Effort: M per step, each its own PR.

**H4 [high] - Services and Domain drive adapters directly; this is the root of most baseline lines here** (layer rule 1)
- Where:
  - `VobService.cs:784, 794, 801, 811, 818, 825, 832, 857, 867, 874` (`GetComponentInChildren<MoverAdapter / CodeMasterHandler / ...>`)
  - `VobInitializerDomain.cs:219-254, 645, 681, 777, 796, 934, 950, 986, 994`
  - `UseMob.cs:336`
  - Loading flags on the MonoBehaviour: `VobLoader.IsLoaded/IsQueued`, read in `VobService.cs:185-190, 226, 466, 661`, `UseMob.cs:79/154`, `VobMeshCullingDomain.cs:483/527`
- Why it matters: every new trigger type means editing the dispatcher, and the baseline cannot shrink.
- Fix:
  - (a) `Domain/Vobs/IVobTriggerTarget { void OnTrigger(string sender); void OnUntrigger(string sender); }`, implemented by the handlers and `MoverAdapter`. The dispatcher calls `container.Go.GetComponentInChildren<IVobTriggerTarget>()` and keeps only the priority order (mover, then code master, then trigger list, then the rest).
  - (b) `Domain/Vobs/IVobHandler { void Init(IVirtualObject vob); }` replaces the 10 typed `GetComponent<XHandler>().Init(...)` calls.
  - (c) Move `IsLoaded` / `IsQueued` onto `VobContainer` and make the queue a `NearestFirstQueue<VobContainer>`. `VobLoader` stays as the GameObject-to-container lookup for adapters.
  - (d) The two `AddComponent<VobLoader>` calls (`VobService:452`, `Domain:1039`) go through an `IVobLoaderFactory` bound in `ReflexProjectInstaller` (rule 1).
  - (e) `StationaryLight` setup (`Domain:645-659`) becomes `StationaryLight.Init(LightSetup)`, a value type in `Models/`.
- Effort: M (a+b), S (c), S (d).

**M1 [medium] - VR decisions in Core** (principle 4)
- Where:
  - `VobService.cs:721-722`: oCMobDoor is left out of the AI mob list "because doors use VR hinge physics". This also applies in Flat.
  - `MobInteractionService.cs:39-49`: the same rule again (`ShouldHandleGrab`).
  - `VobService.cs:1015-1039`: drop-collider size and layer chosen for VR hands, `VrWeaponAttackDomain` and the Rigidbody contract; the layer is the string `"TransparentFX"` (979) although `Constants.TransparentFXLayer` exists.
  - `VobService.cs:1047, 1060-1061`: "kinematic means VR-grabbed", next to the existing `IsHeldByPlayer`.
  - `VobInitializerDomain.cs:1013-1018`: child loaders exist because VR adapters call `GetComponentInParent<VobLoader>`.
  - `PrefabType.VobItemLockPick/VobItemWeapon` are picked in Core; a platform can vary the prefab (rule B) but not the selection.
- No `IsGothic1/2` or `#if` was found in this area. G1/G2 knowledge appears only as data comments, apart from `SnapDigSpotToLoader` (1003), a G2 decal workaround at the call site (principle 5).
- Fix:
  - The door/chest grab policy goes behind one Core method (`MobInteractionService.ShouldHandleGrab`) that `AddToMobInteractableList` also uses, or behind an `IContextMobUsePolicy` if Flat really differs.
  - The drop-collider setup goes into the oCItem prefab (rule A) or a `DropCollider` child prefab.
  - Use `IsHeldByPlayer` instead of `isKinematic`.
- Effort: S-M.

**M2 [medium] - the same rule written several times** (principle 3)
- Item symbol name, `Instance ?? Name`:
  - Core, 6 places: `VobInitializerDomain.cs:433, 690-695`, `VobContainer.cs:122-128`, `VobItemProperties2.cs:24-30`, `VobService.cs:1063`, `HeroPerceptionService.cs:161`
  - VR, 5 places: `VrFavoritesService:265`, `VRHeldItemSyncService:117`, `VRTradeCounter:457`, `VRTradeGoods:153`, `VRFavoritesRing:891`
  - Already drifted: `VobItemProperties2.GetFocusName` (38) uses `Name` only.
  - Fix: add `ZenKitVobExtension.GetItemSymbolName(this IItem)`.
- `SpawnVOBTypes` dev filter: `VobService.cs:385` skips when the type is *not* in the list; lines 560 and 987 return when oCItem *is* in the list. One of the two is inverted. Fix: `ConfigService.Dev.IsVobTypeSpawned(type)`.
- Mob slot lookup ("child(0).child(0), name contains ZS"): written twice, `VobService.cs:620-654` (no allocations) and `1123-1133` (LINQ, a new List per call).
- Bounding-box placement: 4 copies (`Domain:898, 944, 964, 978`) that differ in rotation reset and minimum size.
- Item mesh fallback mrm/mmb: 2 copies with different arguments (`Domain:717-748`).
- World item searches: `VobService.FindNearbyWorldItemContainer:1049` and `NpcHelperService.cs:~285-310` both loop over the full `VobCache` with different "is held" tests (`isKinematic` vs `activeInHierarchy`).
- Lock pick broken: `VobService:106` lowers `Amount`, `PlayerService:72` removes an item. Two reactions to one fact; whether both should run is unclear.
- Effort: S each.

**M3 [medium] - runtime cost in play that grows with the world** (P1, P2, P3)
- Where:
  - `VobService.cs:63, 239`: at least 10 lazy VOB builds per frame **even when the frame budget is used up**. Each build can create meshes, run `GetComponentsInChildren` 2-3 times (`Domain:1061, 1067, 305`, `StationaryLightsService.ApplyWorldLight:366`), build name strings, and for every light and fire run `IsNearLightMappedPolygons` (`Domain:615, 636`; a cube of hash lookups, `StationaryLightsService:576`).
  - `GetFreeInteractableWithin10M` (499-521): `ToUpper`, LINQ `Where`+`OrderBy` (distance computed twice), and per candidate `GetOtherMobUsers` (a full `NpcCache` scan) plus `GetSlots` (a new List). Called from routines through `Wld_IsMobAvailable` and `UseMob`: cost grows with mobs-of-scheme x NPCs. `GetNearestFreeSlot` (1080) is LINQ too.
  - `FindNearbyWorldItemContainer` (1049) and Wld_DetectItem: O(all world VOBs), with `GetComponent<Rigidbody>` per item.
- Fix:
  - Pre-cache the "near light-mapped polygons / indoor" answer per light and fire in `StationaryLightCacheCreatorDomain`.
  - Make the forced minimum adaptive (e.g. 10 only while `SurroundingsBuildService` holds a focus, otherwise 1-2), measured in a Quest build.
  - Keep a mob-to-user count updated where `CurrentInteractable` is set, and replace the LINQ with a loop.
  - Let `WorldItemService` keep a world-item list (items only), updated on add and remove.
- Effort: M. Please profile the forced minimum in the Old Camp on Quest 2 (P5).

**M4 [medium] - the `VobProperties2` family: 10 near-empty classes chosen by a switch, living in `Adapters/` although they are plain data**
- Rules: layer rule 1; Models without services.
- Where: `Adapters/Properties/Vobs/*`, `VobContainer.cs:46-91`
- What is wrong: Bed, Switch, Wheel, Ladder, Music, Sound and SoundDayTime properties are 10-line shells. `VobItemProperties2` and `MovableProperties` `[Inject]` services. `VobContainer` injects `VmCacheService` (baseline line 39).
- Fix:
  - Keep 3 classes (`VobProperties`, `InteractiveProperties`, `ItemProperties`) and move them to `Models/Vob/`.
  - Move the focus-name lookup (VM symbol `MOBNAME_*`) to a `FocusNameService`.
  - Resolve `GetItemInstance` once, through a service, and store the result.
  - `NpcHelperService:106`: test `Vob is IInteractiveObject` instead of `Props is InteractiveProperties`.
- Effort: M.

**L1 [low] - a missing doc and an adapter creating Domain classes** (principles 8, 1)
- There is no `Docs/vobs.md`: no map of load modes, trigger dispatch order, child-VOB placement or the created-early rule. The 46-commit churn shows people need it.
- `Adapters/Scenes/PreCachingScene.cs:138-151` creates 5 `*CacheCreatorDomain` objects itself (its own FIXME says so). This belongs in a `PreCachingService`.
- Effort: S each.

---

## 4. Baseline lines in this area (`ArchitectureBaseline.txt`)
The `dotnet` architecture check could not run here (no `dotnet`); the lines come from the baseline file.

| Line | Entry | Cost to remove |
|---|---|---|
| 56 | `VobService` -> `Adapters.UI.LoadingBars` | **S**: only `nameof(WorldLoadingBarHandler.ProgressType.VOB)`; move the enum to `Models/Loading/`. The same move clears 28 and 29 and the `NpcInitializerDomain`/`WorldMeshBuilder` uses. |
| 52 | `NpcHelperService` -> `Adapters.Properties.Vobs` | **S**: `Props is InteractiveProperties` becomes `Vob is IInteractiveObject` |
| 31 | `VobInitializerDomain` -> `Adapters.Properties` | **S**: `VobSpotProperties.Fp` (934) is only read by its own gizmo; drop it or read it through the loader |
| 55 | `MobInteractionService` -> `Adapters.Vob` | **S-M**: `HandleMobGrab(VobLoader)` takes `VobContainer` + `GameObject`; one VR caller (`VRMobUseService:49`) |
| 38 | `VobContainer` -> `Adapters.Properties.Vobs` | M (M4) |
| 39 | `VobContainer` -> `Services.Caches` | M (M4) |
| 32 | `VobInitializerDomain` -> `Adapters.Vob` | M (H4 a/b/d/e) |
| 57 | `VobService` -> `Adapters.Vob` | M (H4 a/c/d) |
| 22 | `UseMob` -> `Adapters.Vob` | M (H4 a/c) |
| 12 | `VobMeshCullingDomain` -> `Adapters.Vob` | M (H4 c) |
| 60 | `StationaryLightsService` -> `Adapters.Vob` | not analysed |
| 16 | `VobPfxMeshBuilder` | not analysed |

No Domain class in this area is created by an adapter. The one case found is `PreCachingScene` (L1).

## 5. Extension points
- **What works:** VR and Flat can replace any VOB prefab per `PrefabType` (rule B).
- **What does not:**
  - Changing *which* prefab type or builder a VOB gets requires editing `GetPrefab` / `InitVob`.
  - New trigger targets require editing `DispatchTriggerInternal`.
  - Mob-use policy (doors, chests) is fixed in Core (M1).
- For G1/G2 there is no hook, and no real need for one showed up.
- H1 + H2 + H4 give a natural seam (type table plus builder dictionary plus `IVobTriggerTarget`). Turn the builder dictionary into a registration (`VobBuilderRegistry` in a `*ContextBootstrap`) only once a module actually needs to override one.

## 6. `Adapters/Animations/AnimationSystem.cs` (1316 lines)
- **About 60-70% of it is Gothic logic, not Unity glue.** It covers:
  - the track state machine: layer blend-out, `NextAni` chaining, idle fallback (540-705, 707-745)
  - attack and combo frame queries (1126-1250)
  - turning event tags into game calls: item insert/remove into `NpcService`, attack windows (869-918)
  - step and SFX sound choice (925-959)
  - item random animations (965+), PFX lifetimes (1005-1064)
  - root motion with wall sliding and water (785-851), root-collider sizing (196-318)
- Only the PlayableGraph and job binding (320-460) and the Update/LateUpdate hooks truly need the adapter.
- It also injects `VobService` just to wait for `HasPendingVobInits`, and runs per-frame for every active NPC.
- **A split is worth it**, mostly because the track logic could then be EditMode-tested and fight code (`HasOptimalFrameReached`, `ContinueCombo`) would stop depending on a MonoBehaviour. Proposed pieces:
  - `Domain/Animations/AnimationTrackPlayer` (plain C#: track list, play/stop/blend/idle, frame queries; about 450 lines)
  - `Domain/Animations/AnimationEventDomain` (event tags, SFX, PFX, morph; about 250 lines)
  - `Domain/Npc/NpcRootMotionDomain` (movement, walls, water, root collider; about 200 lines)
  - The adapter shrinks to about 300 lines of graph and lifecycle.
- Effort L, in 3 PRs. Lower priority than H1-H4, unless fight or animation work is planned soon.

## 7. Suggested order of work
1. S cleanups, one PR: delete `DestroyVob`, `GetSoundClip` and `ObjectRoutines`; move lock-pick listeners to `MobLockService`; add `GetItemSymbolName()` and `IsVobTypeSpawned()` (fixing the inverted filter on purpose); clear baseline lines 56/28/29, 52 and 31.
2. H1 `VobTypeTable`: decide the `oCTouchDamage` mismatch; replace the lists in VobService, the Domain, GameStateService and ZenKitExtension.
3. H4 (a)+(b)+(c): the `IVobTriggerTarget` / `IVobHandler` interfaces and loading flags on `VobContainer`; clears baseline 12/22/55, most of 32/57. Then H3.1, extracting `VobTriggerService`.
4. H3.2 `WorldItemService`, with the item index (M3) and the drop-collider prefab (M1).
5. H2 builders, family by family: zones+logic, then sound, then light+fire, then mesh/item/pfx. After each one, check that the remaining `InitVob` stays readable.
6. M3 performance: pre-cache light/fire indoor flags, adaptive per-frame minimum, mob user count. Measure on Quest 2.
7. M4 properties family, write `Docs/vobs.md` (L1), then AnimationSystem (section 6).

## Questions for the author
- Is `oCTouchDamage` supposed to be a placeholder (`IsUnbuiltVobType`) or a built VOB? Today it is both.
- When a lock pick breaks, should both the world VOB `Amount--` and the inventory `RemoveItem` happen?
- Should Flat NPCs and the hero use doors through `AI_UseMob`, or is the VR hinge rule meant for all platforms?
- Was the forced `_minVobInitsPerFrame = 10` measured on Quest 2 during play, or only during loading?

## What I read
- **Read fully:**
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Docs/architecture.md`
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Domain/Vobs/VobInitializerDomain.cs`
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Services/Vobs/VobService.cs`
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Services/Vobs/MobInteractionService.cs`
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Services/Vobs/MobRoutineService.cs`
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Models/Container/VobContainer.cs`
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Adapters/Vob/VobLoader.cs`
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Adapters/Properties/Vobs/VobProperties2.cs`, `VobItemProperties2.cs`, `MovableProperties.cs`, `SwitchProperties.cs`
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Adapters/Properties/VobProperties.cs`, `VobSpotProperties.cs`
- **Read in part:**
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Adapters/Vob/MoverAdapter.cs` (lines 280-561 plus its outline)
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Core/Scripts/Adapters/Animations/AnimationSystem.cs` (outline plus lines 535-974)
  - Outlines or excerpts of TriggerListHandler, TouchDamageHandler, TriggerZoneHandler, CodeMasterHandler, MessageFilterHandler, MoverControllerHandler, TriggerScriptHandler, SoundHandler, SoundDaytimeHandler, StationaryLight
  - `GameStateService` (registries), `StationaryLightsService` (`ApplyWorldLight`, `IsNearLightMappedPolygons`), `NpcHelperService` (~285-310), `PlayerService` (65-90), `VmCacheService.IsDigSpot`, `VmWorldExternalDomain` (300-320), `MoverKeyframeDomain` (signatures only)
  - `/home/jaxt0r/Gothic-workspace/Gothic-Unity/Assets/Gothic-Tests/EditMode/Architecture/ArchitectureBaseline.txt` (VOB-related lines only)
- **Not read:** `LightPresetLoader.cs`, `FireService.cs`, `MobLockService.cs` (outline only), `TouchDamageService.cs`, Projectile, VisualFxLight, WeaponAdapter, PfxShapeScaler, DamageTargets (outline only), ResourceCacheService, MeshService, all prefabs and scenes, and the VR adapters beyond grep hits.
- The facts script was not used (it takes a diff range, and this was a folder audit).
