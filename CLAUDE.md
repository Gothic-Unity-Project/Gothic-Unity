# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Gothic-UnZENity is a community rebuild of Gothic 1 and 2 in Unity, with a primary focus on native VR. All game assets are loaded **at runtime** from the user's local Gothic installation via ZenKit — no game assets are bundled. This means nearly all C# work is engine/systems code rather than content.

Data flow: Unity → ZenKit.dll (.NET Standard 2.1) → libzenkitcapi (native) → Gothic filesystem → back to Unity as C# objects.

## Developer Setup

1. Place `GameSettings.dev.json` in `Assets/StreamingAssets/` (git-ignored) pointing to your local Gothic installation directory.
2. Open and play the **Bootstrap scene** in the Unity Editor to start the game.
3. Create a `GameConfiguration` ScriptableObject via `Right-click > Create > ScriptableObjects > GameConfiguration` in `UnZENity-Core/Resources/GameConfigurations/` and assign it to `GameManager`'s Config slot if you need a custom config. A Production config exists by default.
4. Configuration is accessed at runtime via `GameGlobals.Config`, which exposes Gothic.ini, GothicGame.ini, GameSettings.json, and DeveloperConfig settings.

**Without HurricaneVR** (paid asset): Remove `GUZ_HVR_INSTALLED` from Project Settings scripting defines, then set `GameManager.DeveloperConfig → Controls.GameControls` to `Flat`.

**Purple HVR hands**: Run `UnZENity > VR > HVR - Convert Materials to URP` to fix URP material conversion.

## Build & Test

Builds are performed via the Unity Editor or CI using `game-ci/unity-builder`. The CI build methods are:
- `GUZ.VR.Editor.VRBuilderActions.PerformWindows64Build`
- `GUZ.VR.Editor.VRBuilderActions.PerformPicoBuild`
- `GUZ.VR.Editor.VRBuilderActions.PerformQuestBuild`

CI triggers on PRs labeled `pipeline-test-build`. HurricaneVR (commercial asset) is sourced from a private asset repo and injected into `Assets/HurricaneVR/` before building.

PlayMode tests live in `Assets/UnZENity-Tests/PlayMode/` (assembly: `UnZENity.Tests.PlayMode.asmdef`). Run them via the Unity Test Runner window.

## Architecture

### Layer Model

The codebase is split into four layers with strict dependency direction (top depends on bottom):

```
UnZENity.VR / UnZENity.Flat ← platform mode (VR or flat-screen)
         ↓
UnZENity.Gothic1 / Gothic2  ← game-version adapters
         ↓
UnZENity.Core               ← engine, services, domain logic
```

`UnZENity.Lab` is a playground for experiments and mocks used in tests.

**Assembly topology**: Every module depends on `UnZENity.Core` and has **no interaction with other UnZENity modules** (star topology — no lateral dependencies).

Within Core, responsibility is further split into four layers: **Model** (data) → **Domain** (business logic) → **Service** (orchestration) → **Adapter** (Unity integration). Dependencies flow upward only.

### Dependency Injection (Reflex)

All major systems are **singletons registered via Reflex DI**. The full list is in `ReflexProjectInstaller.cs`. There are two scopes:

- **ProjectScope** (Bootstrap) — core services available everywhere
- **PlayerScope** (VR/Flat scene) — platform-specific services; all subsequent scene scopes inherit from this, not ProjectScope

This inheritance is managed in `ReflexProjectInstaller.OverrideParent()`. When adding a new service, register it in `ReflexProjectInstaller.InstallBindings()` or the appropriate VR/Flat scene installer.

Injection approaches:
- `[Inject]` attribute on properties (preferred for brevity)
- Constructor injection for non-MonoBehaviour classes created via Reflex
- Each scene needs a `SceneScope` component for auto-injection; prefabs need `GameObjectSelfInspector`
- Static contexts: `DIContainer.Resolve<T>()` or the `.Inject()` extension method on plain C# objects

### Key Patterns

(Each pattern lives in its folder representation like Adapters in `/Adapters/`.)
- **Adapters**: MonoBehaviour classes that bridge Unity lifecycle (Start/Update/etc.) to domain services. They are thin wrappers — logic belongs in services.
- **Services**: Pure C# classes injected via DI.
- **Domain**: Business logic independent of Unity MonoBehaviours (often includes sub-naming _Creators_).
- **Models**: Data containers, often with a `Container` suffix (e.g. `NpcContainer`, `VobContainer`) wrapping entity state.
- **GlobalEventDispatcher**: Central event bus (`Assets/UnZENity-Core/Scripts/GlobalEventDispatcher.cs`) used for cross-service communication (scene loading, NPC creation, fight events, time changes, etc.). Used for all! events at runtime for easy seekability.

### Game Version Specifics

Gothic 1 vs Gothic 2 differences are isolated to `UnZENity-Gothic1/` and `UnZENity-Gothic2/`. Each has a `ContextBootstrap` adapter (`G1ContextBootstrap`, `G2ContextBootstrap`) that registers version-specific services. The Flat mode equivalent is `FlatContextBootstrap`. This separation is rarely used.

### VR Framework

HurricaneVR (HVR) is the VR interaction framework. Its presence is gated by the `GUZ_HVR_INSTALLED` scripting define symbol. VR-specific services live in `UnZENity-VR/Scripts/Services/` and adapters in `UnZENity-VR/Scripts/Adapters/`.

### Asset Loading Pipeline

Gothic mesh objects are resolved by name (e.g. `HUM_BODY_NAKED0`) using this priority order:
1. `.mds` (IModelScript) — animation + mesh data for animated objects
2. `.mdl` (IModel) — combined .mdh + .mdm
3. `.mdh` (IModelHierarchy) — bone structure
4. `.mdm` (IModelMesh) — mesh + optional bone details
5. `.mrm` (IMultiResolutionMesh) — actual render data

### Animation System

The project uses a **custom animation system** in `GUZ.Core.Animations` — not Unity's Mecanim or Timeline — to support Gothic's runtime animation layering and blending. Two execution modes:
- **Immediate functions**: Control flow (e.g. `AI_SetWalkmode`) executes during script parsing
- **Queued actions**: Animation sequences execute via Command pattern; complex actions like `AI_UseMob()` decompose into sub-actions (turn, walk, animate) managed by a queue

### Scene Loading & Performance

Scene loading uses **async-await frame-skipping** — this is not multithreading; it works like Coroutines, deferring object creation across frames to avoid hitches.

Pre-caching reduces world load time from ~45s to ~10s by separating computation phases:
1. **VOB bounds** — lazy-loading components computed once and cached
2. **World chunk slicing** — light-based chunk boundaries pre-calculated
3. **TextureArray metadata** — composition cached; only texture creation at runtime

### Daedalus VM

ZenKit executes Gothic's Daedalus scripts. External functions called by scripts are implemented in `VmGothicExternals`. Unregistered functions fall through to a `DefaultExternal` handler that logs them. The `VmService` and `VmExternalService` in Core manage VM lifecycle.

### Combat

Physical hit detection requires three conditions simultaneously:
1. Animation frame is within the `DEF_OPT_FRAME` window
2. The attack has not previously connected
3. `DEF_HIT_LIMB` bone triggers `OnTriggerEnter`/`OnTriggerStay`

Box colliders on combatants are deactivated outside combat for performance.

## Logging

- Uber Logger wraps all `Debug.Log*()` calls and routes to three sinks: the Uber Console (filterable by category), Unity Console, and `UnZENity.log` file (`FileLoggingHandler`).
- Log categories are defined in the `LogCat` enum: `AI`, `Animations`, `Dialog`, `DxMusic`, `NPC`, `PreCaching`, `ZenKit`, `ZSpy`, and others. Use `LogEditor()` variants for editor-only logs — they compile out in release builds (`#define ENABLE_UBERLOGGING`).
- Access the Uber Console at `UnZENity > Debug > Uber Console`.
- Always create logs with this feature: `Logger.Log(message, LogCat)`/`Logger.LogWarning(message, LogCat)`/`Logger.LogError(message, LogCat)`

## Coding Style

Enforced via `.editorconfig` and the [Coding Style Guide wiki](https://github.com/Gothic-UnZENity-Project/Gothic-UnZENity/wiki/Coding-Style-Guide):

- Private fields/properties: `_camelCase` (underscore prefix)
- Public/protected fields: `PascalCase`
- Methods and classes: `PascalCase`
- Interfaces: `I` prefix with adjective phrase (e.g. `IDamageable`)
- Boolean fields: verb prefix (e.g. `IsPlayerDead`)
- Enums: singular; Flag enums: plural. No Enum prefix/suffix.
- Local variables: prefer `var` when type is apparent
- Allman brace style (opening brace on new line)
- Braces on single-line `if` are optional (`csharp_prefer_braces = false`), used a lot, but mostly when the if+else statement is a one liner.
- Max line width: 120 characters
- Use `[Tooltip]` attributes instead of field comments
- No regions; one class per file
- 4-space indentation, CRLF line endings, UTF-8

Namespaces follow folder structure: `GUZ.Core.Services.Npc`, `GUZ.VR.Adapters.Player`, etc.

UI: Default `TMP_Text` font size is **12** to preserve layout integrity for Gothic menu elements.

## Key Dependencies

- **ZenKit** — Gothic asset parser (runtime loading of meshes, worlds, scripts, audio). It is symlinked at **./ZenKitCS/** (Use it, whenever information are needed)
- **dmusic** — DirectMusic reimplementation for Gothic's original music system
- **Reflex** — IoC/DI container (`com.gustavopsantos.reflex`)
- **HurricaneVR** — Commercial VR interaction framework (private repo, not in this repo)
- **Unity URP** — Universal Render Pipeline for graphics
- **Unity OpenXR + Pico OpenXR** — XR platform support
- **Newtonsoft JSON** — serialization

## Workflow Notes

- Branch from `main`, submit PRs with detailed descriptions of what changed and how to test manually.
- The project tracks issues and feature status on [GitHub Projects](https://github.com/Gothic-UnZENity-Project/Gothic-UnZENity/projects?query=is%3Aopen).
- Team coordination happens on Discord; align with in-progress work before starting large features.