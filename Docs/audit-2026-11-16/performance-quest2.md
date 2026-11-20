# Performance review for Quest 2 (static, 2026-11-16)

Scope: code, shaders and plain-text project settings of `main` at `ffd7f5ec8`. No profiler data, no play mode, no
device. Every number called "estimate" is an estimate. Instance counts come from code comments where they exist
(`NpcTargetSelectionDomain.cs:195`: "Old Camp: 52 awake of 992").

**Unity MCP worked only partly.** The server answered `no_unity_session` for most of the review and came up near the
end. Read through it (read-only): the active URP asset's main values, the renderer feature list, the layer collision
matrix, and the component lists of the VR NPC prefab, the VR item prefab and the light prefab. Its read-only tools
do not expose the other fields of the URP asset and renderer (SRP Batcher switch, depth and opaque texture, depth
priming, post-processing), the OpenXR settings or `Production.asset`; those would need `execute_code`, which was
ruled out. Following the project rule, no YAML under `Assets/` was read by hand, so these are **not checked**
(section 8). `ProjectSettings/*.asset` and `Packages/manifest.json` were read directly as allowed.

Paths below are relative to `Assets/` unless they start with `ProjectSettings/`.

## 1. Summary

1. Every NPC within 50 m animates and skins every frame, also behind the player or behind a wall: the Animator is
   set to always animate and every skinned mesh is set to update when offscreen. In the Old Camp that is about 50
   NPCs with several skinned meshes each.
2. The world shader `Lit/World` declares `_FocusBrightness` outside the per-material buffer, which very likely
   makes it incompatible with the SRP Batcher. As each renderer has its own material, every world chunk and VOB
   draw then pays a full state setup.
3. Texture memory: the opaque texture array is created as DXT1, which Quest's GPU does not support, so Unity
   stores it as RGBA32 (8 times the size). Every slice is 512x512 whatever the source size, and the arrays seem to
   keep a CPU copy as well.
4. Streaming while walking is allowed half of a frame (6.9 ms at 72 Hz) on top of the normal frame, at least 10
   VOBs and one whole NPC per frame even when the budget is gone, with mesh collider cooking, new materials and
   first-use shader compiles in it. This is the most likely source of hitches.
5. Nothing in code sets foveated rendering, eye texture scale or refresh rate, and the physics step is 50 Hz
   against a 72 Hz display. The URP asset for Android is sane where readable (no HDR, 4x MSAA, scale 1, no
   renderer features); the OpenXR settings could not be checked.
6. Per awake NPC per frame there is no distance throttling: AI update, animation update, a physics overlap query
   and a PlayableGraph each. The world shader also loops over up to 32 point lights per pixel.

## 2. Findings, ranked by expected impact on Quest 2 divided by effort

Effort: S = under a day, M = days, L = a week or more.

| Id | Area | Where | What costs | How often, how many | Proposed change | Effort | Impact | Confidence | Confirm on device |
|---|---|---|---|---|---|---|---|---|---|
| P01 | CPU+GPU | `Gothic-Core/Scripts/Domain/Meshes/Builder/AbstractMeshBuilder.cs:311`, `Adapters/Animations/AnimationSystem.cs:338` | `updateWhenOffscreen = true` on every NPC `SkinnedMeshRenderer` (skinning and bounds recalculation also when not visible) and `AnimatorCullingMode.AlwaysAnimate` | Every frame, every skinned mesh of every awake NPC (about 50 NPCs in the Old Camp, several meshes each: estimate 100-200 renderers) | Set fixed generous `localBounds` per model (from the MDH/bounds cache) and turn `updateWhenOffscreen` off; set the Animator to `CullUpdateTransforms` (root motion is applied by our own code, so the NPC still moves) | S | high | high (code), medium (size of gain) | Profiler: `MeshSkinning.Update`, `Animators.Update`, `SkinnedMeshRenderer.UpdateBounds`/`PostLateUpdate.UpdateAllSkinnedMeshes`. A/B: stand in the Old Camp looking at a wall |
| P02 | CPU (render thread) | `Gothic-Core/Shaders/Lit-World.shader:7,34,70-77`; `AbstractMeshBuilder.cs:455,487,890-902` | `_FocusBrightness` is a material property declared outside `CBUFFER_START(UnityPerMaterial)`: the shader is very likely not SRP Batcher compatible. Every renderer gets `new Material(...)`, so nothing else batches either | Every world chunk and every VOB using the texture arrays, every frame | Move `_FocusBrightness` into the buffer (or drop it from `Lit/World`, the Dynamic shaders do the highlight). Check all custom shaders in the Inspector for "SRP Batcher: compatible" | S | high if confirmed | medium (needs the Inspector or Frame Debugger) | Shader Inspector line "SRP Batcher"; Frame Debugger: "SRP Batch" entries versus single draws; OVR Metrics: draw calls and render thread time before/after |
| P03 | GPU | not set anywhere in `Gothic-Core/Scripts`, `Gothic-VR/Scripts` (grep for `foveat`, `eyeTextureResolutionScale`, `renderScale`, `SpaceWarp`, `displayFrequency`: no hits) | No fixed foveated rendering, no explicit eye texture scale, no refresh rate request. Quest 2 default eye buffer with 4x MSAA and alpha-to-coverage foliage is fill-rate heavy | Every frame | Enable the OpenXR "Foveated Rendering" feature and set `XRDisplaySubsystem.foveatedRenderingLevel` at start (Vulkan only); set the eye texture scale explicitly per device; consider Application SpaceWarp only after the rest | S | high | medium (assets not checked, it may be partly set there) | OVR Metrics Tool: "Foveation level", eye buffer size, GPU utilisation and app GPU time with FFR off/high |
| P04 | hitch | `Services/FrameSkipperService.cs:21`, `Services/Vobs/VobService.cs:63,239`, `Services/Npc/NpcService.cs:117-150` | Lazy init may use 50 % of a frame measured from frame start (6.9 ms at 72 Hz), on top of game logic and rendering. At least 10 VOBs per frame (known) and at least one complete NPC per frame are built even when the budget is used up. `InitNpc` is one block (body, head, armour, weapon meshes, materials, animation baking) | While the queues are not empty: after load and whenever the player crosses a culling border (50/100/200 m for VOBs, 50 m for NPCs) | Budget in ms from device headroom (for example 2 ms at 72 Hz), not a share of the frame; no minimum when the frame is already late; split `InitNpc` into steps that can yield | M | high | high (code), medium (size) | Walk from the start to the Old Camp with OVR Metrics "stale frames" and a Profiler capture: frames over 13.9 ms with `InitVobCoroutine` / `InitNpcCoroutine` on top |
| P05 | memory | `Services/Caches/TextureCacheService.cs:255-259,288,301-306,329-331`, constants at `:33,42,44` | Opaque array is `TextureFormat.DXT1`: not native on Quest, stored as RGBA32 (issue #356, logged since `245756e09`). Transparent and water arrays are RGBA32 by choice. Each slice is 512x512 with 6 mips (smaller textures are tiled up to 512), about 1.4 MB per slice in RGBA32 against 0.17 MB in DXT1/ASTC 6x6-class. The `Texture2DArray` is filled with `Graphics.CopyTexture` and never made non-readable, so a CPU copy probably stays | Once per world, held for the whole session | Transcode to ASTC in the pre-cache step on Android (the cache already stores per-texture metadata); bucket arrays by size (256 and 512) instead of tiling everything to 512; call `Apply(false, true)` after the copies | M | high (memory, load time, texture bandwidth) | high for the format, medium for the CPU copy | The new `[TextureArray]` log line on device; Memory Profiler package (already in the manifest): `Texture2DArray` sizes; OVR Metrics: available memory |
| P06 | GPU | `Gothic-Core/Shaders/Lit-World.shader:83-99,134-145`, `StationaryLighting.hlsl:6-16` | Per pixel: a dynamic loop over up to 16 + 16 stationary lights, each with an indexed read into a 512-entry global array and a `sqrt`; manual mip level (`ddx/ddy/log2`) on every sample; a branch for the lightmap sample | Every lit pixel of every world chunk and VOB, both eyes. Chunks are merged by light count, so a chunk near several fires runs the full loop on all its pixels | Cap the loop at 4-8 on mobile (keyword or constant), sort lights per renderer by distance (the code notes they are in slot order, `StationaryLightsService.cs:66-67`), or evaluate per vertex for chunks beyond a few metres. Keep per pixel for caves only | M | medium to high at night and in camps | medium | OVR Metrics GPU time at the Old Camp fires at night versus noon; RenderDoc for Oculus: draw time of chunk draws with `_StationaryLightCount` > 8; A/B by forcing the count to 0 |
| P07 | CPU | `Adapters/Npc/AiHandler.cs:133`, `Adapters/Animations/AnimationSystem.cs:707-760,844`, `Adapters/Npc/RootCollisionHandler.cs:48-65`, `Adapters/Animations/NpcHeadAnimationHandler.cs:92` | Per awake NPC per frame, without distance tiers: AI update (perception timers, passive perception delivery), animation update (track clocks, `SetJobData` of a large struct, one `Physics.SphereCast` while walking), one `Physics.OverlapSphereNonAlloc`, a head LateUpdate, plus one PlayableGraph and one Animator each | About 50 awake NPCs in the Old Camp, more in Khorinis (estimate). Six to seven callbacks per NPC per frame | Tiers by distance and visibility: beyond about 20 m or when not rendered, tick AI and separation every 3rd-5th frame and animation at half rate; skip `SeparateFromNearbyNpcs` for NPCs that did not move | M | medium to high | medium | Profiler (deep off): `AiHandler.Update`, `AnimationSystem.Update`, `RootCollisionHandler.Update`, `Director.ProcessFrame`, `Physics.OverlapSphere` self times summed; compare with NPC culling distance 25 m |
| P08 | CPU/physics | `ProjectSettings/TimeManager.asset` (fixed timestep 0.02, maximum allowed timestep 0.333), `ProjectSettings/DynamicsManager.asset` | 50 Hz physics on a 72 Hz display: some frames run no step, some one, hand and held-item physics beat against the display. After a hitch up to 16 physics steps run in one frame (0.333 / 0.02), which makes one long frame into several | Every frame | Set `Time.fixedDeltaTime = 1 / refreshRate` at start (the rate is already read in `VRContextInteractionService.cs:161`) and lower the maximum allowed timestep to about 0.05 | S | medium | medium | Profiler: `FixedUpdate.PhysicsFixedUpdate` call count per frame after a streaming hitch; feel of held items while turning |
| P09 | hitch | `ProjectSettings/GraphicsSettings.asset:43-45` (no preloaded shaders), no `ShaderVariantCollection`/`WarmUp` in code; `Services/Meshes/DynamicMaterialService.cs:126-160` | Shader variants compile at first use on Vulkan (first water, first additive PFX, first decal). The first hover over an item creates materials with the `*-Dynamic` shaders (new variant, new pipeline) and allocates lists with LINQ | First time per variant per session; hover material creation on every first hover of an object | Warm a `ShaderVariantCollection` during the loading screen; create one hidden draw with each Dynamic shader at load; pool the dynamic materials | S-M | medium | medium | Profiler: `Shader.CreateGPUProgram` / `CreateGraphicsPipeline` spikes; logcat with "Log Shader Compilation" in a development build |
| P10 | hitch | `Adapters/Vob/StationaryLight.cs:274`, `AbstractMeshBuilder.cs:724-729`, `AbstractMeshBuilder.cs:455-487`, `Services/World/StationaryLightsService.cs:343-392` | Inside lazy VOB init: an allocating `Physics.OverlapSphere` plus `GetComponent` per collider for each light; non-convex `MeshCollider` on VOB meshes (PhysX cooks each new mesh on the main thread at first use); `new Material` per sub mesh; `GetComponentsInChildren<Renderer>` per VOB for the world light | Per VOB or light when it is first created | `OverlapSphereNonAlloc` with a layer mask; `Physics.BakeMesh` on a worker thread or pre-cached primitive colliders for common VOBs; share materials per texture array where the light indices allow | M | medium | high (code) | Profiler during the walk: `Physics.OverlapSphere`, `MeshCollider.Cook`/`Mesh.Bake PhysX CollisionData`, `GC.Alloc` in `VobInitializerDomain.InitVob` |
| P11 | GC | `Adapters/Animations/Morph/AbstractMorphAnimation.cs:371,405,419` | Non-blending morph path (`VobAnimateMorph`): `ToList()` and a new vertex array per frame per running morph, then a full `mesh.vertices` upload. No distance check on this path (`MaxBlendDistance` is `float.MaxValue`, `:59`) | Every frame per active VOB with a morph visual (flags, plants: count unknown) | Reuse one buffer per component as the head path already does (`:308`), iterate backwards instead of `ToList()`, add the camera distance check | S | medium | high (code), unknown count | Profiler: `GC.Alloc` under `AbstractMorphAnimation.Update`; GC frequency in OVR Metrics while standing still outdoors |
| P12 | hitch/GC | `Gothic-Core/Scripts/Logging/Logger.cs:24-27`, `UberLogger/Scripts/UberLogger.cs:605-645,448`, `ProjectSettings/ProjectSettings.asset:881` | 522 `Logger.Log(` call sites build their message (string interpolation, often ZenKit `GetName` calls) and call `cat.ToString()` before the level filter drops them. Messages that pass the filter capture a `StackTrace(true)`, write to an auto-flushed file and are forwarded to `Debug.Log`. With a tester's log level below Warning this happens for every AI and fight log | Per AI state change, perception, fight event | A cheap `Logger.IsEnabled(cat)` check before formatting on AI paths, cached category strings, no stack trace below Warning in builds | S | low at level Warning, medium with verbose logging | high (code) | Compare frame times in a fight with `LogLevel` Warning and Debug in `GameSettings.json`; Profiler: `StackTrace..ctor`, `GC.Alloc` under `Logger.Log` |
| P13 | memory | `Services/Vobs/VobService.cs:282-285` (`DestroyVob` not implemented), `MultiTypeCacheService.Meshes`, no `UploadMeshData(true)` anywhere | VOBs and NPCs are only deactivated by culling, never released, so GameObjects, materials and colliders grow with the area visited. Runtime meshes stay readable (CPU and GPU copy), needed only where a `MeshCollider` or morph uses them | Grows over a session | Release VOBs beyond a second, larger distance; `UploadMeshData(true)` for meshes without collider or morph | M-L | medium (memory pressure, long sessions) | medium | Memory Profiler snapshots at start and after walking Old Camp to New Camp; OVR Metrics available memory over 30 minutes |
| P14 | CPU spike | `Services/Npc/NpcRoutineService.cs:38-50` | Routine recalculation for every NPC of the world (about 990 in G1) on every game minute | Every game minute (a few real seconds) | Spread over the minute (N NPCs per frame) or index routines by start time | S | low to medium | medium | Profiler: periodic spike in `NpcRoutineService.OnGameTimeMinuteChanged` |
| P15 | CPU | `Adapters/Npc/AiHandler.cs:369-447`, `Domain/Npc/NpcTargetSelectionDomain.cs:123-200`, `Services/Npc/NpcHelperService.cs:480-492` | Perception tick: each NPC scans all awake NPCs (enemy and body search, about N squared per perception time), with line-of-sight raycasts for candidates and `Vob.CurrentStateName` string marshalling in `IsInEscortState` | Per NPC per perception time (0.3-5 s). All NPCs created together can tick in the same frame | Stagger the first perception time randomly per NPC; cache the escort flag on state change | S | low to medium | medium | Profiler: `AiHandler.ExecuteActivePerceptions` spikes with a period equal to the perception time |
| P16 | GPU | `Gothic-Core/Shaders/Lit-AlphaToCoverage.shader:20,113-121`, `Lit-Water.shader:16-18`, `Domain/Meshes/Builder/VobPfxMeshBuilder.cs:349-374,547-550` | Foliage with alpha-to-coverage (needs MSAA, no early-Z benefit), water blended with `Cull Off`, one material per particle system, particle world collision against all layers | Forests, lakes, fires | Draw alpha-tested geometry after opaque (already queue AlphaTest); cull back faces of water when the camera is above; limit PFX collision to the world layer and set `maxParticles` by distance | M | medium in forests | low to medium | RenderDoc overdraw view near the swamp camp and a forest; OVR Metrics GPU time looking at foliage versus at the ground |
| P17 | CPU (render) | `Gothic-Core/Resources/Prefabs/Vobs/zCVobLight.prefab` (has `Light` + `UniversalAdditionalLightData`), `Adapters/Vob/StationaryLight.cs:8`, `Lit-SingleMesh.shader:66-71`, URP-Performant: additional lights per vertex, 8 per object | Each stationary light VOB carries a Unity `Light`; NPCs and items are lit per vertex by URP per-object lights. Light VOBs stay loaded up to about 200 m (`StationaryLightsService.cs:61-62`). If the `Light` components are enabled (the flag is not visible through MCP), URP culls them and builds per-object light lists every frame | Every frame, lights in range times renderers | Disable the Unity `Light` beyond 20-30 m (the slot pool already knows the distance), set the per-object limit to 2-4 on Quest | S-M | low to medium | low (enabled state not checked) | Frame Debugger: additional light count; Profiler: `CullScriptable`, `SetupLights`/`ForwardLights.Setup` |
| P18 | hitch/CPU | VR `oCNpc.prefab` and `oCItem.prefab` (read through MCP), `Gothic-VR/Scripts/Adapters/VRFocus.cs:45,236` | Every NPC instance carries 3 canvases (name canvas, health bar with its own `Canvas` and `GraphicRaycaster`, subtitles), 4 TMP texts, 5 images, an `AudioSource`, an `XRSimpleInteractable` and 10 components on the root. Every item carries a name canvas with a TMP text, a `Rigidbody`, and a grab interactable with a two-hand transformer. TMP and canvas objects are costly to instantiate and to activate | Per NPC (about 990 in a G1 world over time, about 50 awake) and per item at creation and at each culling activation | One shared name canvas, health bar and subtitle panel that is moved to the focused or speaking NPC; add the grab components when a hand comes near | M | medium (streaming hitches and memory) | high (prefab), medium (size) | Profiler: `Instantiate` and `Canvas.BuildBatch`/`TMP` time inside `InitNpc`; A/B with the name canvas removed from the prefab in a test build |
| P19 | CPU | VR `oCItem.prefab` (`GothicGrabInteractable`), VR `oCNpc.prefab` (`XRSimpleInteractable`), XRI 3.6.1 | The XR Interaction Manager processes every registered interactable in each of its update phases per frame (dynamic, late, before render, fixed), whether a hand is near or not | Every frame, every active item, NPC and mob in culling range (hundreds, estimate) | Register interactables only near the hands (enable the component from a trigger around the player), or leave them disabled until hovered by our own overlap check | M | medium | medium (XRI behaviour from its source, count not measured) | Profiler: `XRInteractionManager.Update`, `ProcessInteractables` self time, with many items in view (Old Camp market) |

## 3. Detail for the high and medium findings

### P01 Skinning and animation of NPCs that are not visible

`AbstractMeshBuilder.BuildViaMdmAndMdh` sets `meshRenderer.updateWhenOffscreen = true` for every soft-skin mesh
(`:311`); its own comment says "This can become a performance issue which we need to monitor carefully". The
reason given is bounds: without it the bounds are not recalculated, and the root bone was removed because sitting
poses grew the box. `AnimationSystem.CreateGraph` sets `cullingMode = AlwaysAnimate` with the comment "NPC culling
is handled by our own culling domain" (`:338`), but that domain (`NpcMeshCullingDomain`) only culls by distance
(50 m default, `DeveloperConfig.cs:196`), not by frustum or occlusion. Result: in the Old Camp all awake NPCs are
posed (`AnimationPoseJob`, Burst, worker threads), their transforms written back, and all their meshes skinned on
the GPU each frame, including the ones behind the player. `ProjectSettings.asset:102` has GPU skinning on, so the
skinning cost lands on the GPU that is already the bottleneck.

Change: give each `SkinnedMeshRenderer` a fixed `localBounds` large enough for all poses of that model (twice the
rest-pose bounds is the usual choice; the VOB bounds cache already has the numbers), turn `updateWhenOffscreen` off,
and use `AnimatorCullingMode.CullUpdateTransforms`. Root motion is applied in `ApplyFinalMovement` by our own code
and events fire from `ApplyEvents`, so game logic keeps running for NPCs that are not rendered. `NpcBodyBounds`
reads bone positions for the hit box only within 12 m (`NpcBodyBounds.cs:19`), so a culled pose further away is
harmless; check the weapon hit limb in fights behind the player.

### P02 SRP Batcher and materials

`Lit/World` lists `_FocusBrightness` in `Properties` (`:7`) and declares `float _FocusBrightness;` before the
`UnityPerMaterial` buffer (`:34`), while the buffer itself is at `:70-77`. The SRP Batcher requires every material
property in that one buffer. `Lit/SingleMesh` has a comment showing the authors care about this
(`Lit-SingleMesh.shader:51-52`), so this looks like an oversight. `Lit/AlphaToCoverage`, `Lit/Water` and
`Lit/SingleMesh` look compatible from the source.

Why it matters here more than usual: `AbstractMeshBuilder.GetDefaultMaterial` returns `new Material` for every
renderer and sub mesh (`:890-902`), because the stationary light indices are stored per material
(`StationaryLightsService.UpdateRenderer`, `:153-196`). With the SRP Batcher that is fine (different materials of
one shader variant batch). Without it each draw is a full material setup, and the globals include two 512-entry
arrays (`StationaryLighting.hlsl:13-14`).

Also check: `StationaryLightsService.ApplyWorldLight` sets a `MaterialPropertyBlock` on VOB renderers in light
mapped areas (`:381-389`). A property block takes a renderer out of the SRP Batcher. `_VobLightTint` is already in
the material buffer (`Lit-World.shader:76`), and every renderer owns its material, so it can be set on the material
instead.

Static batching is on for Android (`ProjectSettings.asset:505-507`) but does nothing for meshes built at runtime;
`isStatic = true` in `WorldMeshBuilder.cs:131,582,600` has no batching effect in a build. No GPU instancing path
exists for VOBs (each has its own material), so 200 identical barrels are 200 draws.

### P03 XR settings in code

`VRContextInteractionService.GetFrameRate` (`:153-164`) only reads the refresh rate. There is no call that sets
foveation, eye texture scale, or a display frequency. The manifest has `com.unity.xr.openxr` 1.16.1 and the Pico
OpenXR package, no Meta-specific package. With plain OpenXR, foveation needs both the OpenXR feature enabled in the
OpenXR settings asset (not checked) and `foveatedRenderingLevel` set at runtime, which is missing. On Quest 2 fixed
foveation is the cheapest large GPU win available.

### P04 Streaming budget

`FrameSkipperService._frameQuota = 0.5f` and the budget is `quota / targetFrameRate` measured from the start of
`Update` (`:21,64,93-97`). Coroutines run after all `Update` calls, so by then part of that window is already used
by game logic; rendering both eyes comes on top. At 72 Hz a frame that spends until 6.9 ms in script cannot make
13.9 ms on Quest 2. `VobService.InitVobCoroutine` then still builds 10 VOBs (`:63,239`) and
`NpcService.InitNpcCoroutine` checks the budget only after a full `InitNpc` (`:141-151`). The culling borders are
crossed constantly while walking, so the queues refill continuously. `NearestFirstQueue.UpdateOrder` re-sorts all
entries when the camera moved (`NearestFirstQueue.cs:60-84`); cheap for hundreds, noticeable for thousands of
entries right after load.

When a culled VOB comes back, `VobMeshCullingDomain.SetVobState` calls `SetActive(true)` on the whole VOB
(`:237`), which re-registers its colliders with PhysX and runs `OnEnable` of its components (for lights:
`StationaryLight.OnEnable` and a pool rebalance). Many VOBs crossing a border in one frame is a second hitch source
apart from first-time creation.

### P05 Texture arrays

`BuildTextureArray(TextureFormat.DXT1, ...)` for opaque and `RGBA32` for transparent and water (`:255-259`). The
new log (`245756e09`) will show "supported by GPU: False" on Quest. Sizes per slice with 6 mips: RGBA32 about
1.40 MB, DXT1 about 0.17 MB. A world with 1000 opaque textures (estimate) is about 1.4 GB uncompressed against
175 MB; even half of that is a problem on a 6 GB device. Smaller source textures are tiled up to 512x512
(`:329-360`), so a 64x64 texture also takes a full slice. Single textures for NPCs and items
(`TryGetTexture`, `:106-160`) use the Gothic format too, so DXT1/DXT5 are also decompressed there. DXT3 and others
go to RGBA32 on all platforms (`ZenKitExtension.cs:224-232`).

### P06 World shader per-pixel cost

See the table. Two further points: `CalcMipLevel` plus `SAMPLE_TEXTURE2D_ARRAY_LOD` replaces hardware mip selection
and rules out anisotropic filtering, at the cost of derivative maths per pixel; the reason is the per-slice maximum
mip, which could instead be handled by filling the missing mip levels at array build time. The fog is computed per
pixel with a `length` (`GothicIncludes.hlsl:17-22`); per vertex is enough for world chunks.

### P07 Per-NPC per-frame cost

The AI loop itself is throttled since the 2026-11-02 ADR (loop passes wait for the perception time,
`AiHandler.cs:273-275`). What remains per frame per awake NPC: `DeliverPassivePerceptions` (cheap when the list is
empty), timers, `TickCurrentAction`, and in `AnimationSystem.Update` a snapshot copy of the track list (`:715-716`),
`BuildJobData` (a struct with 8 track slots and 27 native array handles copied through `SetJobData`, `:457`),
`ApplyFinalMovement` with a `SphereCast` when moving (`:844`) and four event checks per track with events.
`RootCollisionHandler.Update` runs an overlap query for every NPC every frame even when nobody moved (`:65`).
`NpcJumpFall` goes to sleep by itself (`:149`), `NpcBodyBounds` runs at 10 Hz within 12 m (`:18-19`), `HeadMorph`
only writes the mesh within 12 m (`HeadMorph.cs:35`): these three are fine.

The pose job is Burst compiled (`AnimationPoseJob.cs:37`) but uses `Vector3`/`Mathf` rather than
`Unity.Mathematics`, and one PlayableGraph per NPC means about 50 graphs evaluated per frame. Moving to
`Unity.Mathematics` types and, later, one graph with many outputs are possible follow-ups; measure first.

### P08 Physics

Settings found: fixed timestep 0.02, maximum allowed timestep 0.3333, solver iterations 6/2, auto sync transforms
off (good), reuse collision callbacks on (good), `m_GenerateOnTriggerStayEvents: 1`, `m_QueriesHitTriggers: 1`
(queries without an explicit `QueryTriggerInteraction` also test triggers), a customised layer matrix. Each awake
NPC has a non-kinematic Rigidbody with a capsule against the world mesh collider (`AnimationSystem.cs:185-186`).
`VrPhysicsHand` runs in `FixedUpdate` (`:504`), so hand physics updates at 50 Hz and is shown at 72 Hz.

### P09 and P10 First-use work

See the table. `Constants.cs:97-122` resolves shaders with `Shader.Find`, so they must be in "Always Included
Shaders" or referenced by a material to exist in a build; then all their variants are included but none is warmed.

## 4. Inventory of per-frame callbacks

Multiplicity: "1" = singleton or one per player; "per hand" = 2; estimates are marked "est.".

### Core

| Callback | Instances at runtime | Notes |
|---|---|---|
| `UnityLifecycleEvents.Update/LateUpdate/FixedUpdate` (`UnityLifecycleEvents.cs:43-59`) | 1 | Calls `FrameSkipperService`, `NpcMeshCullingService.Update` (loop over awake NPCs, 3 transform reads each), `LoadingService`, `HeroPerceptionService`, `StationaryLightsService.LateUpdate`, `BarrierService.FixedUpdate` |
| `AiHandler.Update` (`:133`) | per awake NPC (about 50 Old Camp); disabled when dead | see P07 |
| `AnimationSystem.Update/LateUpdate` (`:707,760`) | per awake NPC, plus the hero body | see P07 |
| `RootCollisionHandler.Update` (`:48`) | per awake NPC | overlap query every frame |
| `NpcHeadAnimationHandler.LateUpdate` (`:92`) | per awake NPC with a head | cheap unless in dialog |
| `NpcBodyBounds.LateUpdate` (`:77`) | per awake NPC | 10 Hz, within 12 m |
| `NpcJumpFall.Update` (`:141`) | per awake NPC that moved recently | sleeps when idle; raycast when moving near the hero |
| `AbstractMorphAnimation.Update` (`:212`) | per NPC head (`HeadMorph`) and per VOB with a morph visual (`VobAnimateMorph`) | see P11 |
| `NpcBurnEffect.Update` (`:183`) | per burning NPC | rare |
| `NpcRandomAniService.Update(npc)` (`:75`) | called per NPC (caller not traced) | |
| `InitVobCoroutine`, `InitNpcCoroutine`, `ReEnableNpcCoroutine` | 1 each, every frame | see P04 |
| `VobMeshCullingDomain.StopVobTrackingBasedOnVelocity` coroutine (`:555`) | 1 | loops paused VOBs |
| `TriggerListHandler.Update` (`:75`) | per trigger list VOB (not lazy loaded, est. tens) | not read in detail |
| `TouchDamageHandler.Update` (`:48`) | per touch damage zone (not lazy loaded) | not read in detail |
| `VisualFxLight.Update` (`:81`) | per active effect light | |
| `Projectile.Update` (`:75`) | per flying projectile | |
| `PfxShapeScaler.Update` (`:33`) | per PFX with shape scale keys (count unknown) | |
| `BackgroundMusic.Update`, `EnvironmentReverb.Update`, `RainFollower.LateUpdate` | 1 each | |
| `FloatingHitNumber.LateUpdate`, `FloatingUiHitNumber.Update` | per visible hit number | |
| `BinkPlayer.Update`, `GothicLogoHandler.Update`, `GameProfileMenu.LateUpdate` | 1, menus and videos only | |
| `NpcColliderDebugAdapter.Update` | debug only | |
| `HandPresence.Update` (`Resources/Prefabs/Hands/HandPresence.cs:26`) | per hand | |

### VR

| Callback | Instances | Notes |
|---|---|---|
| `VRFocus.LateUpdate` (`:236`) | per item, NPC and mob instance (hundreds, est.) | known, section 7 |
| XRI interactables (`GothicGrabInteractable` on items, `XRSimpleInteractable` on NPCs) | per active item and NPC (hundreds, est.) | processed by the XR Interaction Manager each frame, see P19 |
| `VRUiOnTop.Update/LateUpdate` on the NPC subtitle panel | per NPC, but the panel is inactive until the NPC speaks (`VRSubtitles.cs:38`) | fine |
| `VrHinge.LateUpdate` (`:162`), `OnCollisionStay` (`:188`) | per door and chest lid in range (est. tens) | runs `Swing` every frame also at rest; could sleep |
| `VRItemUser.Update` (`:64`) | per usable item with the component (est.; prefab not checked) | early-outs unless grabbed |
| `VRTorch.Update` (`:52`) | per torch item | early-outs unless held |
| `VRWeaponAttackAdapter.OnTriggerStay` (`:101`) | per weapon trigger overlap per physics step | cheap checks first, then `GetComponentInParent` |
| `VRDigSpot.FixedUpdate` (`:61`) | per dig spot | |
| `VRNpcLoot.Update` (`:101`) | per NPC, disabled unless loot is open | fine |
| `VRMobDocument.Update`, `VRLockPickInteraction.Update`, `VRBow`, `VRCrossbow`, `VRRuneCaster`, `VRStackSplitter`, `VRDocReader`, `VRDocViewer`, `VRChainLightning` | per such item or open interaction (few) | not read in detail |
| `VrSocketSlot.Update` (`:249`), `XriSocketBridge.Update` (`:70`) | per body slot (est. 5-10) | cheap |
| `VrPhysicsHand.Update/FixedUpdate/LateUpdate` (`:499-576`), `VrPhysicsHandContact.OnCollisionStay`, `VrGhostHand.LateUpdate`, `VrBlockHand.Update`, `VrHeldItemVisual.FixedUpdate` | per hand or per held item | |
| `VRPickpocket.Update` (`:140`) | 1 | scans NPCs on a timer |
| `VRSwimDive.Update/FixedUpdate`, `VRSpellTransformPuppet.Update/LateUpdate` | 1 | known, section 7. The puppet also renders a second camera (`:854`) while transformed |
| `VRPlayer`, `VRHeroMovement`, `VRHeroBody`, `VRHeroBlock`, `VrBodyAnchor`, `VrMoveVignette`, `ScreenFade`, `WallFade`, `VRBackpackEquipToggle`, `VRBackpackVacuum.FixedUpdate`, `VRBackpackItemPassThrough.FixedUpdate`, `VRFavoritesRing`, `VRFavoritesRingSpell`, `VRItemDetailsPopup`, `VrLedgeClimb`, `VrLedgeReach`, `VrLadderExit`, `XriRig`, `XriTeleportAimer`, `GothicJumpProvider`, `VRUnityLifecycleEvents` (`VRWeaponService`, `VrWeaponAttackDomain.FixedUpdate`) | 1 each | player singletons; not read line by line |
| `VRSpectatorCamera.LateUpdate` | 0 on Android (`:47-50` disables it) | fine |
| `VRUiOnTop`, `VRFollowCamera`, `VRPlaceInFrontOfCamera`, `VRGrabBar`, `VRCinema`, `VRPrintScreen`, `VirtualKeyboard`, `VRTradeCounter`, Marvin tabs, `SpeechToTextHandler` | 1 each while the UI is open | |
| `XriHvrStyleSimulator`, `XriSimulatorControlsGUI.OnGUI`, `VRHandVelocityDebugger` | editor or debug | confirm `OnGUI` is not active in builds |

Gothic1 and Gothic2 assemblies have no per-frame callbacks. Flat has two (`FlatTextInput`, `FlatPlayerMovement`).

## 5. Settings for Android as found

Read directly from `ProjectSettings/`. "Not checked" = lives in an asset under `Assets/`, needs Unity MCP.

| Setting | Found | Recommended for Quest 2 |
|---|---|---|
| Unity / URP | 6000.3.13f1 / URP 17.3.0 | - |
| Default quality level on Android | 0 "Low" with `URP-Performant.asset` (`QualitySettings.asset`); Medium excludes Android | keep one Quest level; add a Quest 3 level later |
| Graphics API Android | Vulkan, then GLES3 (`m_APIs: 150000000b000000`), auto off | Vulkan only (foveation and SpaceWarp need it; one shader set less) |
| Scripting backend / architecture | IL2CPP, ARM64 | keep; IL2CPP configuration is default (Release), consider Master for releases |
| Multithreaded rendering / graphics jobs | on / on | keep; A/B graphics jobs once on device |
| Static / dynamic batching | on / off | no effect on runtime meshes; fine |
| GPU skinning | on (`gpuSkinning: 1`, `meshDeformation: 2`) | keep, with P01 fixed |
| Colour space | Linear | keep |
| Incremental GC | on | keep |
| Scripting defines Android | `ENABLE_UBERLOGGING` on | see P12 |
| Preloaded shaders | none | see P09 |
| Quality "Low": MSAA | 4 (the URP asset overrides this) | 4x in the URP asset is the usual Quest choice; 2x if GPU bound |
| Quality "Low": skin weights | 2 bones | keep |
| Quality "Low": anisotropic | per texture (1) | irrelevant while the shaders sample with explicit LOD |
| Quality "Low": vSync | 0 | fine, the XR runtime paces frames |
| Quality "Low": LOD bias | 0.4 | no `LODGroup` exists in code, no effect |
| Quality "Low": shadows | off in legacy field (URP asset decides) | off: none of the custom lit shaders has a ShadowCaster pass |
| Fixed timestep | 0.02 s | 1 / display refresh rate, set at runtime (P08) |
| Maximum allowed timestep | 0.3333 s | 0.05 s |
| Physics solver iterations | 6 / 2 | keep |
| Auto sync transforms | off | keep |
| Generate OnTriggerStay events | on | keep only if needed (`VRWeaponAttackAdapter`) |
| Queries hit triggers | on | pass `QueryTriggerInteraction.Ignore` in hot queries (`RootCollisionHandler.cs:65`, `StationaryLight.cs:274`) |
| Far clip plane | 40 x (zVidVisualRange + 1) m, 200 m at the default 4 (`GothicIniConfig.cs:22-23`, `VRContextInteractionService.cs:370`) | lower default on Quest (120-160 m) with fog matched |
| VOB culling distances | 50 / 100 / 200 m by size (C# defaults, `DeveloperConfig.cs:154-160`; `Production.asset` not checked) | measure; 40 / 80 / 150 m as a Quest profile |
| NPC culling distance | 50 m (C# default, `DeveloperConfig.cs:196`) | 35-40 m on Quest, or tiers (P07) |
| Stationary light slots | 512 on mobile, 16 + 16 per renderer (`StationaryLightsService.cs:26`) | 8 per renderer on Quest (P06) |
| Application target frame rate | not set in code | fine in XR |
| URP-Performant: render scale | 1.0 (MCP) | keep; set the XR eye texture scale per device instead |
| URP-Performant: HDR | off (MCP) | keep |
| URP-Performant: MSAA | 4x (MCP) | keep (alpha-to-coverage needs it); try 2x if GPU bound |
| URP-Performant: colour grading | LDR, LUT 16 (MCP) | keep |
| URP-Performant: main light | per pixel; shadow map 1024, distance 50, 1 cascade, soft shadows off (MCP). Whether shadow casting is enabled is not visible | turn main and additional light shadows off in the asset: no custom lit shader has a ShadowCaster pass, so they can only cost |
| URP-Performant: additional lights | per vertex, 8 per object (MCP) | per vertex, 2-4 per object |
| URP-Performant: SRP Batcher, depth texture, opaque texture, upscaling filter, LOD cross fade | not checked (not exposed by the MCP read actions) | SRP Batcher on, depth and opaque texture off |
| URP-Performant-Renderer: renderer features | none (MCP). Consequence: the `DecalProjector`s created in `VobDecalMeshBuilder.cs:34` have no Decal feature on this renderer and probably do not draw | keep it empty on Quest; draw decals as plain alpha quads |
| URP-Performant-Renderer: rendering path, depth priming, post-processing, native render pass, intermediate texture | not checked | Forward, depth priming off, post-processing off, native render pass on, intermediate texture "Auto" |
| Layer collision matrix | read through MCP. NPC against NPC collides (plus the manual separation in `RootCollisionHandler`), items against NPCs do not, hitboxes only against items, Hand against Default, items, NPCs and movables | fine; consider NPC against NPC off, as the separation code already handles it |
| OpenXR: render mode, foveation feature, late latching, symmetric projection, SpaceWarp, Meta Quest feature group | not checked | Single Pass Instanced (multiview), foveated rendering feature on, symmetric projection on (Quest 2 foveation works better with it), late latching on |
| Default volume profile (`Assets/DefaultVolumeProfile.asset`) | not checked | no active overrides on Quest |

## 6. Measurement plan on a Quest 2

Build: IL2CPP development build with the Profiler attached for CPU markers, and a non-development build for the
numbers that count. Tools: OVR Metrics Tool overlay (FPS, stale frames, app GPU time, CPU and GPU utilisation and
level, foveation level, available memory), Unity Profiler, Memory Profiler, RenderDoc for Oculus. Record each scene
for 60 seconds, log level Warning.

| # | Scene | Record | Good |
|---|---|---|---|
| 1 | G1 start point, standing still, noon | FPS, app GPU time, main and render thread ms, draw calls, set-pass calls | 72 FPS, GPU under 10 ms, main thread under 7 ms, render thread under 5 ms |
| 2 | Old Camp centre (market), noon, slow 360 degree turn | Same, plus awake NPC count from the log, `Animators.Update`, `MeshSkinning.Update`, `AiHandler.Update` totals | 72 FPS, under 1 % stale frames; frame time looking at a wall clearly lower than looking at the crowd (it will not be before P01) |
| 3 | Old Camp centre at night next to the fires | App GPU time compared with scene 2 | under 2 ms more than at noon |
| 4 | Walk from the start to the Old Camp gate at running speed | Stale frame count, longest frame, frames over 20 ms, `InitVobCoroutine` / `InitNpcCoroutine` ms per frame, `GC.Collect` count | no frame over 28 ms, under 2 % stale frames, no GC spike over 3 ms |
| 5 | Fight with 5 NPCs or monsters (wolves near the Old Camp), weapon drawn | Main thread ms, physics ms, `AiHandler` and `FightService` self time, GC alloc per frame | 72 FPS, under 1 KB allocated per frame on average |
| 6 | Forest and lake edge: look through foliage across water | App GPU time, RenderDoc overdraw | GPU under 11 ms |
| 7 | Inside the Old Mine (light mapped, many torches) | App GPU time, draw calls, `_StationaryLightCount` on visible chunks in RenderDoc | GPU under 10 ms |
| 8 | Memory: after loading the world, and after 30 minutes with a walk Old Camp to New Camp to Swamp Camp | Memory Profiler: `Texture2DArray`, `Texture2D`, `Mesh`, managed heap; OVR Metrics available memory | total under 3 GB, growth under 200 MB over the walk, no low-memory warnings in logcat |

A/B switches that need no code change, for a first split of CPU against GPU: lower zVidVisualRange (far clip),
`DeveloperConfig.NpcCullingDistance`, `EnableNpcs` off, VOB culling distances, and the OVR Metrics CPU/GPU level
readout (if GPU utilisation is above 90 % the scene is GPU bound).

## 7. Already known (earlier audit, not re-derived)

- `VRFocus.LateUpdate` runs on every item, NPC and mob prefab instance although it only has work while highlighted
  or locked. Added here: each of these prefabs also carries the name canvas referenced at `VRFocus.cs:45`; the
  hover path creates materials and uses LINQ (`DynamicMaterialService.cs:106-160`); `SetFocusName` calls
  `GetComponentInParent` and `GetComponentInChildren<TMP_Text>` on every hover (`VRFocus.cs:286-298`).
- `VobService` forces at least 10 lazy VOB builds per frame even when the frame budget is used up
  (`VobService.cs:63, 239`). Added here: the budget itself is half a frame from frame start and the NPC queue has
  the same pattern with a minimum of one full NPC (P04).
- `NpcService.ExtHlpGetNpc` scans the whole NPC cache with a closure per call. Added here: other whole-cache scans
  are `NpcService.GetClosestNpcInFront` (`NpcService.cs:365`), `NpcHelperService.ExtWldDetectItem` over all VOBs
  (`NpcHelperService.cs:288`), `NpcKnockoutDomain.cs:156,193`, and `NpcRoutineService.cs:40` every game minute.
- `NpcAiService.DeliverPassivePerceptions` allocates symbol-name strings per receiver.
- `VobService.GetFreeInteractableWithin10M` uses LINQ and scans the NPC cache per candidate;
  `FindNearbyWorldItemContainer` loops over all world VOBs.
- `VRSpellTransformPuppet` allocates strings and lambdas per frame while moving. Added here: it also renders a
  second camera with a 50 m far plane while active (`VRSpellTransformPuppet.cs:854`).
- `VRSwimDive` looks up guild depths every FixedUpdate.
- `AudioSource.PlayClipAtPoint` in 7 VR adapters (15 call sites in Core and VR together).

## 8. Not checked

- Not readable with the read-only MCP actions that were allowed: the remaining fields of `URP-Performant.asset`
  (SRP Batcher, depth and opaque texture, shadow switches) and all of `URP-Performant-Renderer.asset` except its
  empty feature list, the OpenXR package settings and feature set (render mode, foveation, late latching, Meta
  Quest support), `XRGeneralSettingsPerBuildTarget`, `DefaultVolumeProfile`, `Production.asset` values (the
  culling distances above are C# defaults), serialized values on prefabs (whether the `Light` on
  `zCVobLight.prefab` is enabled, canvas render modes, Rigidbody collision detection modes), materials, Always
  Included Shaders. Someone with the Editor open can read these in a few minutes from the Inspector.
- Prefabs other than the VR NPC, VR item and light prefab (mobs, doors, chests, the player rig, hands).
- Whether `Lit/World` is really reported as SRP Batcher incompatible (P02): needs the Inspector or Frame Debugger.
- Whether Unity keeps a CPU copy of the texture arrays (P05): needs a Memory Profiler snapshot.
- Real counts: world chunks per world, VOBs with morph visuals, lights per chunk, draw calls. All need a run.
- The two shader graphs (`Lit_Both_ScrollY`, `Unlit_Both_ScrollY`) beyond a search for scene colour or depth
  nodes (none found), `Gothic/Lit/Misc` (a copy of URP Lit with all its variants; no reference to it found in
  code, prefab and material use not checked), sky shader cost, UI shaders.
- Player singletons in VR were only read where listed; `VrPhysicsHand` (collider syncing, pass-through overlap
  queries), `VrWeaponAttackDomain` and the XRI interactor setup (ray and sphere casts per hand per frame) deserve
  their own pass.
- Way-net searches (`WayNetService`, Dijkstra), `NpcNavMeshService` runtime NavMesh builds, save and load, audio
  (voice count, DSP buffer, `EnvironmentReverb`), dmusic CPU cost, Bink video playback.
- Occlusion: an `OcclusionCullingTool` exists in the Editor folder, but nothing was found that bakes or applies
  occlusion data for the runtime world; inside the Old Camp everything in the frustum up to the far plane is drawn.
