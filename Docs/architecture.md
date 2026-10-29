# Architecture

How Gothic-Unity is built, which principles new code follows, and why. `CLAUDE.md` has the setup and the module
overview; `Docs/save-game.md` is an example of a documented area.

## In one paragraph

Gothic-Unity is a modular monolith. `Gothic.Core` is the engine. Game version modules (`Gothic1`, `Gothic2`) and
platform modules (`Gothic-VR`, `Gothic-Flat`) plug into it through interfaces and never know each other. Most of the
game's rules aren't ours: they live in Gothic's Daedalus scripts, which ZenKit runs. Our C# is the engine around
them: loading worlds, the externals the scripts call, animation, physics, VR. The game state lives in ZenKit's
objects (VOBs, NPC instances, script symbols), which is also what Gothic's save format stores.

## The big picture

```
              Gothic installation (.zen, .mds, scripts, ...)
                                |
                             ZenKit   (file formats, Daedalus VM, save format)
                                |
    +---------------------- Gothic.Core -----------------------+
    |  Models  <-  Domain <-> Services  <-  Adapters (Unity)  |
    |  areas: Npc, Vobs, Vm, SaveGames/World, Player, UI, ...  |
    +-----^-------------^----------------^-------------^-------+
          |             |                |             |
       Gothic1       Gothic2         Gothic-VR     Gothic-Flat
       (game version modules)        (platform modules)
```

An arrow means "depends on". Modules depend on Core only (star topology).

## Principles

### 1. Scripts first

**Rule:** If Gothic's scripts decide something, let them decide. Implement the missing external, or call the existing
`B_*`/`C_*` helper (`vm.Call`), instead of writing the rule again in C#.

**Why:** Mods change the scripts. A C# copy of a script rule breaks mods and drifts away from the original game.

**Example:** XP, guild attitudes and dialog conditions come from the scripts. The `daedalus-bridge` skill shows how
C# talks to the VM.

### 2. ZenKit objects are the state

**Rule:** Keep game state in the ZenKit object Gothic keeps it in: VOB fields, the NPC instance, script symbols.
C# fields only cache or derive from it.

**Why:** One source of truth means nothing to keep in sync, and saving works without extra code, because Gothic's
save format is made of exactly these objects.

**Example:** remaining trigger activations live in `CountCanBeActivated`, a moving gate in the `zCMover` key frame
fields: both are saved for free. Counter-example: mob states were read from `State`, which is Gothic's *number* of
states (1 for every door). The current state now has its own, documented place (`core.mobStates`).

### 3. One rule, one place, next to its data

**Rule:** A rule about one thing (an NPC, a world, an item list) lives on that thing: a property or method on its
model, or an extension method in `Extensions/`. Everybody else calls it.

**Why:** The same rule written twice drifts apart, and when it changes, nobody finds all copies.

**Example:** `NpcProperties.IsDead`, `NpcContainerExtension.IsUsingMob()`, `ToGothicWorldName()`,
`ContentItemExtension.PackItems()`. Before, "is the NPC dead?" was written in 8 places with two different rules.

### 4. Extend by registration, not by `if`

**Rule:** When G1, G2, VR, Flat or a mod behave differently, Core defines an interface and each module registers its
implementation. Avoid `if (IsGothic2())` and `#if` in Core.

**Why:** A new module or mod plugs in without editing Core, and each module's differences are in its own files
instead of spread over Core.

**Example:** `IContextGameVersionService` (G1's `G1ContextService` is set in
`G1ContextBootstrap.RegisterGameVersionModule()`, e.g. for `CutsceneFileSuffix`), `IContextScreenshotService`
(VR left eye vs. flat camera), `ISaveStateSection` (every feature registers its own save data).

### 5. Fix ZenKit once, at the edge

**Rule:** Work around a ZenKit quirk in one place (`Extensions/ZenKit*.cs` or the domain class of its area) and
document it there. Callers use the clean method.

**Why:** A workaround repeated at every call site is forgotten at the next one.

**Example:** `ZenKitNpcExtension.TrySetAiVars()` (ZenKit's setter copies bytes instead of ints), the pitfall table
in `Docs/save-game.md`.

### 6. Speak Gothic

**Rule:** Name things after Gothic's concepts: VOB, Mob, oCNpc, AI state (`ZS_*`), perception, waynet, routine, the
name of the external. Split big classes along Gothic's own families.

**Why:** Whoever knows Gothic, its scripts or OpenGothic finds the code by name. `MobInteractionService` says more
than `InteractableManager`.

**Example:** `VmExternalDomain` (1,440 lines) would split by the externals' prefixes: `Npc_*`, `AI_*`, `Wld_*`,
`Mdl_*`, `Log_*`/`Info_*`.

### 7. Events for facts, calls for sequences

**Rule:** Use `GlobalEventDispatcher` to announce facts other areas react to ("world loaded", "NPC attribute
changed"). When steps must run in a fixed order, call them explicitly.

**Why:** The order of event listeners is invisible in the code. A sequence that depends on it breaks as soon as
somebody adds a listener.

**Example:** `SaveGameService` calls `Reset()` and then `Restore()` on every save section. Before, each section reset
itself on a different event, and one forgot to.

### 8. Small files, written down

**Rule:** A class has one reason to change. Aim for less than ~400 lines per class and ~50 per method. A non-trivial
area gets a short doc in `Docs/`, and every PR that decides something a decision record in `Docs/adr/` (why, and
what wasn't chosen).

**Why:** People and AI agents understand a change by reading a few small files. A 1,000-line class costs everybody
time on every change.

**Example:** `SaveGameService` orchestrates, the details live in `Domain/SaveGames/*`, the design in
`Docs/save-game.md`.

### 9. Leave it better, don't rewrite

**Rule:** Improve the code your change touches anyway. No big-bang refactorings.

**Why:** Rewrites conflict with every open branch and bring nothing a player notices. Small steps keep the game
working.

## Layers inside Core

| Layer | Folder | Holds | Rules |
|---|---|---|---|
| Models | `Models/` | data: containers (`NpcContainer`, `VobContainer`), proxies, value types | no services, no Unity lifecycle |
| Domain | `Domain/` | Gothic logic, often the helper of one service (`*Domain`, `*Creator`) | holds no game state, may call services |
| Services | `Services/` | use cases and state lifetime, registered in Reflex | thin orchestration |
| Adapters | `Adapters/` | MonoBehaviours bridging Unity and services | no rules, the only layer with Unity lifecycle |

Dependency rules. Domain and Services share a rank and may use each other: a Domain class is a worker owned by a
Service (`new XDomain().Inject()`).

1. Nothing outside `Adapters/` uses the `Adapters` namespace. Plain data classes there (`NpcProperties`,
   `InteractiveProperties`) belong in `Models/`.
2. Models don't use Domain or Services.
3. A file's namespace starts with `Gothic.Core.<Layer>` of the folder it lives in.
4. MonoBehaviours live in `Adapters/` only.
5. Modules use Core only, never each other.

Rules 1-4 are checked by `CoreArchitectureTest` (EditMode); rules 1-3 also by the `Architecture` check (run manually).
Today's violations are listed in `Assets/Gothic-Tests/EditMode/Architecture/ArchitectureBaseline.txt`. The list may
only shrink: new code follows the rules, old code gets fixed when it's touched (principle 9), and the fixed line is
removed from the baseline. Rule 5 is enforced by the asmdef references.

## Where does my code go?

| You want to ... | Put it into |
|---|---|
| implement a Daedalus external | `Domain/Vm/VmExternalDomain` registers it, the service of its area implements it (e.g. `NpcAiService`) |
| add a rule about an NPC, item or VOB | a property on its model or an extension in `Extensions/` |
| add a use case with state (loading, saving, dialogs) | a service in `Services/<Area>/`, registered in `ReflexProjectInstaller` |
| move a big piece of logic out of a service | `Domain/<Area>/<Name>Domain.cs`, created by the service |
| react to Unity (Update, collisions, input) | an adapter in `Adapters/<Area>/` that calls a service |
| behave differently in G1/G2/VR/Flat | an interface in `Services/Context/`, the implementation in the module, registered in its `*ContextBootstrap` |
| save state Gothic doesn't store | an `ISaveStateSection` (`Docs/save-game.md`) |
| work around a ZenKit bug | `Extensions/ZenKit*.cs`, plus a line in the pitfalls of the area's doc |
| add a new kind of VOB or UI element | a prefab in `Gothic-Core/Resources/Prefabs/`, a `PrefabType` entry, loaded via `ResourceCacheService` |
| make a VOB behave differently in VR/Flat | a Prefab Variant of the Core prefab at the same path under `Resources/VR/` or `Resources/Flat/` |

## Inside Gothic-VR

VR interaction runs on a backend: XR Interaction Toolkit (free, `VrInteractionBackend.Xri`) or HurricaneVR
(commercial, behind `GOTHIC_HVR_INSTALLED`, being replaced: `Docs/HVR-replacement/`). Game code doesn't know which.

| Folder (`Gothic-VR/Scripts/`) | Holds | Uses |
|---|---|---|
| `Interaction/Models/` | VR value types (`VrHandSide`, `VrGrabArgs`, `VrHapticPreset`) | nothing else |
| `Interaction/Ports/` | the VR framework as interfaces (`IVrRig`, `IVrInput`, `IVrHaptics`, `IVrSocket`) | Models |
| `Interaction/Backends/Xri/`, `Hvr/` | the only code with XRI or HVR types: port implementations, bridges | Ports, Models, Components |
| `Interaction/Components/` | backend-neutral MonoBehaviours with the Gothic behaviour (`VrSocketSlot`, `VrPhysicsHand`) | Ports, Models, Core |
| `Adapters/`, `Services/` | the game in VR (`VRBackpack`, `VrHapticsService`) | all of the above |

1. **The backend executes, Gothic decides.** A Gothic rule lives in a component, the backend part next to it only
   translates: `VrSocketSlot` decides what goes in, `XriSocketBridge` holds it in XRI terms. When a backend needs a
   Gothic answer, it asks a port that a component implements.
2. **Components don't use `Adapters/` or `Services/`.** They announce facts (principle 7) and a service reacts:
   `VrPhysicsHand` raises `PhysicalHandContact`, `VrHapticsService` vibrates.
3. **New port members are default interface members** while HVR exists, so the HVR backend stays untouched.
4. **Haptics:** only `VrHapticsService` picks preset and strength. A new kind of vibration is a `VrHapticPreset`
   with its values in both backends' tables (`XriHaptics`, `HvrHaptics`, same index).
5. **Don't act inside a backend event.** Destroying, re-parenting or placing objects within an XRI select or hover
   event breaks the interaction in progress: do it in the next `Update` (`VrContainerDropZone`).
6. **Reuse the building blocks** before writing a new one:

| Need | Use |
|---|---|
| "is it a Gothic item?" | `VrSocketSlot.IsItem` |
| a slot for one item (holster, chest, loot) | `VrSocketSlot` and its backend part |
| "item let go in a zone, put it somewhere" | `VrContainerDropZone` |
| something worn on the body | `VrBodyAnchor` |
| the layers the player collides with | `VrCollisionPassThrough.GetCollidingLayers` |
| a vibration | an event `VrHapticsService` listens to |

## Unity assets

The C# rules above have a twin in the Editor: prefabs, scenes, materials and settings are code too. They are
reviewed like code, but changed through the Editor or UnityMCP only (`CLAUDE.md`), never as hand-written YAML.

The project's split: **Gothic data comes from the Gothic installation at runtime** (meshes, textures, sounds,
worlds). **Our structure comes from assets in the repo**: which components, colliders, layers and child objects
a VOB, NPC, menu or the player has. Code fills the structure with Gothic data, it doesn't build the structure.

### A. Structure in prefabs, Gothic data in code

**Rule:** Components, colliders, layers, child objects and default values of a Unity object live in its prefab.
Code instantiates the prefab (`ResourceCacheService.TryGetPrefabObject(PrefabType.X)`) and adds only what depends
on Gothic data: the mesh (`MeshFilter`/`MeshRenderer`/`SkinnedMeshRenderer`), its collider, the name, the ZenKit
object.

**Why:** A prefab shows its whole setup in the Inspector, can be tested in the Lab scene, and a platform can vary it
(rule B). `AddComponent` chains are invisible until runtime and can't be overridden per platform.

**Example:** one prefab per Gothic VOB class (`Prefabs/Vobs/oCMobDoor`, `zCTrigger`, ...), named after the class
(principle 6). The 82 `AddComponent` calls are mostly runtime meshes, which is fine; `FistFightAdapter`,
`ColliderHitTracker` or `AudioSource` added in code are candidates for the prefab.

### B. Platforms vary prefabs as Prefab Variants

**Rule:** `ResourceCacheService` loads `Resources/<Context>/<path>` before `Resources/<path>`, so VR and Flat can
replace any Core prefab. The replacement is a **Prefab Variant** of the Core prefab: it adds the platform components
(HVR grabbables, `VRVobDoor`) and overrides values, nothing else. The same applies to a family inside one module
(`StatusBarHealth` is a variant of `StatusBar`).

**Why:** A copy forgets every later change to the Core prefab. Core components then exist in VR only, and Flat
silently runs without them.

**Today:** all 11 VR VOB prefabs that replace a Core prefab are copies, not variants. The VR `oCNpc` has
`AnimationSystem`, `NpcHeadAnimationHandler`, `NpcHitboxColliderAdapter` and `NpcPrefabProperties` (all Core
adapters), the Core `oCNpc` that Flat uses has none of them. The `MENU_*` prefabs don't use `_MenuSkeleton`, and
`VR - Player XRI`/`BackPack XRI` copy their HVR counterparts. Fix them when touched (principle 9): rebase the copy
onto a variant and move Core components into the Core prefab.

### C. Reference, don't search

**Rule:** Inside a prefab, reach other objects by a serialized reference (`[SerializeField]`), not by name
(`FindChildRecursively("Label")`, `transform.Find`, `GameObject.Find("Rain")`) or a scene search
(`FindObjectsByType`).

**Why:** Renaming a child in the prefab breaks a string lookup at runtime, without an error in the Editor. Scene
searches are slow and find objects of other scenes.

**Exception:** names Gothic defines - bones and slots of runtime meshes (`BIP01 HEAD`, `ZS_RIGHTHAND`) or menu items
named like their Daedalus instance (`MENUITEM_*`). They don't exist before runtime, so the name is the reference.

### D. One way to load assets

**Rule:** Prefabs load through `PrefabType` + `ResourceCacheService`, not through `Resources.Load` with a string.
Put an asset in `Resources/` only when it's loaded by path at runtime; everything else is a serialized reference.
Addressables are used for Localization only; adding them elsewhere is a project decision, not a PR detail.

**Why:** One loader means one cache, one place for the VR/Flat override (rule B) and one error message when a path
is wrong. Everything in `Resources/` is in every build, used or not.

**Today:** ~10 direct `Resources.Load` calls (logos, fonts, `NpcColliderDebug` material, input actions).

### E. Shaders survive the build

**Rule:** A shader that code finds by name (`Shader.Find`) is listed in Graphics Settings > Always Included Shaders
or referenced by a material that ships. Shader names live in `Constants`. Only URP shaders - Built-in shaders like
`Standard` render pink.

**Why:** The Editor finds every shader, a build only the ones it includes. `Shader.Find` then returns `null` on Quest
or Pico only.

**Today:** `Unlit/ThunderShader` (`Constants.ShaderThunder`) is neither always-included nor referenced by a material,
and `Constants.ShaderStandard` (waynet debug lines) is a Built-in shader. Both need a check in a device build.

### F. Layers, tags and physics are project-wide contracts

**Rule:** Layer and tag names are used through `Constants` only. Adding a layer or changing the collision matrix
(`ProjectSettings/DynamicsManager.asset`) is called out in the PR: it changes every collider in the game. A prefab
sets its layer; code sets it only for objects created at runtime.

**Why:** A string layer name that doesn't exist returns -1 and the object silently lands on `Default`.
`feature-checklist` lists the physics layers.

**Also:** `Physics.autoSyncTransforms` is off. Code that moves a collider and queries physics in the same frame calls
`Physics.SyncTransforms()` in between, otherwise the query sees the old place. Queries without a layer mask
(`Physics.CheckSphere`, `OverlapSphere`) see every layer: a layer only some casts may find is masked out there.

### G. Assets respect the module borders

**Rule:** The asmdef rules (rule 5 of the layers) hold for assets too: a Core scene, prefab or material references
Core assets and scripts only. VR and Flat assets may reference Core. The exception is the composition root: the
`Bootstrap` scene holds the `*ContextBootstrap` of every module.

**Why:** A GUID reference is a dependency the compiler doesn't see. A Core asset pointing to a VR prefab drags it into
Flat, or breaks when VR changes it.

**Today:** `Gothic-Core/Scenes/MainMenu.unity` references `Gothic-VR/.../UI/Tutorials.prefab`.

### H. Scenes are thin

**Rule:** A scene holds its `SceneScope`, its bootstrap adapter and prefab instances, not hand-built object trees.
New scenes go into the Build Settings and get a `SceneScope` (`CLAUDE.md`). World scenes are generated from `.zen`
files at runtime; nothing is placed into them by hand.

**Why:** Scenes merge badly in Git and two branches touching one scene conflict. A prefab instance moves the
change into the prefab, which only the branch that needs it touches.

**Example:** the VR `Player` scene is five prefab instances on two GameObjects.

### I. Configuration is data, reviewed like code

**Rule:** `DeveloperConfig` is the one ScriptableObject for developer settings. A new field also gets its value in
`Production.asset`; personal configs stay in `DeveloperConfigs/Local/` (git-ignored). Player settings belong in
Gothic.ini/GameSettings.json, cheats in the Marvin menu (`marvin-cheats` skill), not in a new asset.

**Why:** The C# default isn't what ships: `Production.asset` is. A field without an asset value is a different
game in the Editor and in the build.

## Performance: Quest 2 is the floor

The game runs on standalone headsets. Quest 2 is the slowest one we support: a mobile CPU and GPU, 72 Hz, so about
13.9 ms per frame for everything (scripts, physics, animation, rendering both eyes). A missed frame is felt as
judder, not just seen as a lower number. What runs on a desktop PC in the Editor says nothing about the headset.

### P1. Per-frame work is a decision

**Rule:** New code reacts to an event (principle 7) or a timer instead of checking something in `Update`,
`FixedUpdate` or `LateUpdate`. A component that isn't needed right now (far from the player, not in combat, not
visible) turns itself off. Work per NPC or VOB scales with what is near the player, not with the world.

**Why:** A world has hundreds of NPCs and thousands of VOBs. 0.01 ms per object per frame is invisible on one NPC in
the Lab scene and a lost frame in the Old Camp.

**Example:** the combat box colliders are off outside combat (`CLAUDE.md` > Combat).

**Today:** 54 scripts in Core, VR and Flat have an `Update`, `FixedUpdate` or `LateUpdate`. When you touch one, ask
whether it needs to run every frame for every instance.

### P2. Hot paths don't allocate

**Rule:** Code that runs every frame (or every physics step) doesn't allocate: no LINQ, no lambdas capturing
variables, no `string` concatenation or interpolation, no `foreach` over interfaces that box, no `new List` per call.
Cache `GetComponent` results, use the `NonAlloc` physics queries with a buffer and a layer mask (rule F), and log
there with `LogEditor` or a one-shot guard.

**Why:** Every allocation feeds the garbage collector, and a GC pause on Quest is several milliseconds - a dropped
frame at a random moment, which is hard to trace back to its cause.

**Today:** `StationaryLight`, `VRVobItem`, `VrWeaponAttackDomain` and `VrLadderExit` use allocating physics queries
(`OverlapSphere`, `OverlapCapsule`, `OverlapBox`, `RaycastAll`); `AnimationSystem` and `AbstractMorphAnimation` use
LINQ in files with an `Update`. Check whether they run per frame when you touch them.

### P3. Heavy work happens while loading

**Rule:** Anything that can be computed before the player moves is computed at world load: in the pre-caching
(`Domain/StaticCache/`) or spread over frames with `FrameSkipperService`. Nothing big happens lazily the first time
the player meets it (first dialog, first door, first spell) unless it is spread over frames too.

**Why:** A 200 ms hitch during loading is a slightly longer loading screen. The same 200 ms in play freezes the
player's view, which in VR causes nausea.

**Example:** VOB bounds, light chunks and texture array metadata are pre-cached (`CLAUDE.md` > Scene Loading).

### P4. Rendering has a budget

**Rule:** A feature that adds draw calls, materials, realtime lights, shadows, transparency, post-processing or
shader variants says what it costs on Quest in its PR, and has a quality setting if it's optional. Meshes share
materials and texture arrays so they batch.

**Why:** On a mobile GPU the frame is usually lost in rendering, not in C#. Each eye renders the scene, so every
draw call and every overdrawn pixel counts.

### P5. Measure on the device

**Rule:** A change on a hot path (per-frame code, NPC count, rendering, physics) is profiled in a Quest build, not
only in the Editor. The PR says what was measured (frame time, GC allocations, draw calls) and where in the world.

**Why:** The Editor's CPU is several times faster and hides both the cost and the GC pauses.

## Smells

- the same check written a second time
- `if (IsGothic1())` or `#if` in Core
- a C# field mirroring a ZenKit field
- a class over ~400 lines, a method over ~50, nesting deeper than 3
- names like `*Manager`, `*Helper`, `*Util`
- an event listener that only works if it runs before or after another one
- a ZenKit workaround at a call site
- a prefab copied into `Resources/VR/` or `Resources/Flat/` instead of a Prefab Variant
- `AddComponent` for a component that doesn't depend on Gothic data
- `FindChildRecursively("...")`, `GameObject.Find` or `FindObjectsByType` for an object we created ourselves
- `Resources.Load("...")` outside `ResourceCacheService`, `Shader.Find("...")` outside `Constants`
- a Core asset referencing a VR, Flat or Lab asset
- an XRI or HVR type outside `Gothic-VR/Scripts/Interaction/Backends/`
- a VR component calling `IVrHaptics` or a service instead of announcing a fact
- a new `Update` that polls something an event could announce, or runs for every NPC/VOB in the world
- LINQ, lambdas, string building, `GetComponent` or an allocating physics query on a per-frame path
- heavy work the first time the player meets something, instead of at load time

## Domain-Driven Design, mapped

The terms reviews use, for developers new to DDD:

| DDD term | Meaning | Here |
|---|---|---|
| Bounded context | an area with its own model and words | the area folders: `Npc`, `Vobs`, `Vm`, `SaveGames`, ... |
| Ubiquitous language | the same words in code, docs and conversation | Gothic's terms (principle 6) |
| Entity | a thing with an identity and a lifetime | an NPC (`NpcContainer`), a VOB |
| Value object | a small value, compared by its content | world names, item lists (`ContentItem`) |
| Domain service | logic that belongs to no single entity | `Domain/*Domain` |
| Application service | runs a use case, owns the state's lifetime | `Services/*Service` |
| Anti-corruption layer | translates a foreign model and hides its quirks | the ZenKit extensions (principle 5) |
| Domain event | a fact other contexts react to | `GlobalEventDispatcher` (principle 7) |
| Ports and adapters | the core defines interfaces, the edges implement them | `IContext*Service` + modules, Unity adapters |

DDD is a toolbox here, not a framework: no repositories or aggregates for their own sake. Most of Gothic's domain
lives in the scripts (principle 1), so bounded contexts, the ZenKit boundary and "one rule, one place" matter more
than rich models.

## Tools

- `CoreArchitectureTest` (`Assets/Gothic-Tests/EditMode/Architecture/`) and the `Architecture` check: the
  mechanical dependency rules above, against the baseline. They fail, the reviewer only advises. The check runs
  without Unity, locally via `dotnet run --project .github/architecture-check` or on GitHub via Actions >
  Architecture > Run workflow.
- `architecture-reviewer` agent (`.claude/agents/`): reviews against this doc what a test can't decide - a branch
  before the PR, `--wip` changes, a plan before implementing, or a folder before splitting it. E.g. "use
  architecture-reviewer on my branch". Cheapest on the plan: a structure fixed before coding costs nothing, the
  same finding on a finished branch costs a refactoring.
- `.claude/scripts/architecture-facts.sh`: the structural facts of a change the reviewer starts from.
- `gothic-reviewer` agent: Gothic pitfalls, per-frame allocations and coding style.
- `feature-checklist` skill: config flags, logging, DI wiring, save/load impact.
