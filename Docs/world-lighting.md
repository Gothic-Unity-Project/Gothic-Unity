# World lighting and fires

How the world is lit like in the original engine: lightmaps, lights, the light of VOBs, fireplaces and torches, the
indoor sky. Why it's built this way: Docs/adr/2026-11-05-world.md.
Core code is in `Assets/Gothic-Core/Scripts/`.

## Lightmaps

| Rule | Lives in |
|------|----------|
| Light mapped polygons (caves, mines, houses) show Gothic's lightmaps. Setting, on by default | `DeveloperConfig.EnableWorldLightmaps` |
| Pages are decoded and deduplicated (SHA1), each vertex gets its lightmap uv | `Domain/Meshes/WorldLightmapDomain.cs` |
| The `Texture2DArray` and the shader globals belong to the service; freed on `LoadingSceneLoaded` and `MainMenuSceneLoaded` | `Services/World/StationaryLightsService.cs` |
| The world mesh builder asks the domain for the uv, it keeps no lightmap state | `Domain/Meshes/Builder/WorldMeshBuilder.cs` |
| The shader mixes lightmap and vertex light | `Shaders/Lit-World.shader` |

## Lights

| Rule | Lives in |
|------|----------|
| A static light already in the lightmaps lights only objects and characters, not the walls | `StationaryLightsService`, `Adapters/Vob/StationaryLight.cs` |
| Outdoors static lights are baked into the vertex light and get no slot; in light mapped areas they get a runtime slot | `VobInitializerDomain.CreateLight` |
| Lights are created when their VOB is lazy loaded, so the shader index comes from the world position, not the load order | `VobInitializerDomain` (`parentWorldPosition`), `Services/Culling/VobMeshCullingService.cs` |
| Light pool: only lights within 80 m of the camera are active, closest first, re-sorted with hysteresis | `StationaryLightsService` (`_poolMaxDistance` and the pool constants) |
| Slots: 1023 on PC, 512 on mobile (Quest); the same number is `MAX_TOTAL_STATIONARY_LIGHTS` in `StationaryLighting.hlsl` | `StationaryLightsService._maxTotalLights` |
| A renderer takes 16 lights | `StationaryLightsService.UpdateRenderer` |
| Lights that move (torch, Light spell) update the shader by the "lightRefresh" option | `Constants.IniNames.LightRefresh` |

## Light of VOBs

| Rule | Lives in |
|------|----------|
| A static VOB in a light mapped area takes the light of the floor below it (nearest lightmap cell, searched ring by ring) | `StationaryLightsService.ApplyWorldLight`, called by `VobInitializerDomain` |
| A VOB lit this way ignores the baked static lights, else it gets the light twice | `Adapters/Vob/StationaryLight.cs` |
| The tint is `_VobLightTint` in the VOB's own materials, never a MaterialPropertyBlock: VOBs stay SRP batched; the focus highlight copies the material, so the tint stays | `StationaryLightsService.ApplyWorldLight`, `DynamicMaterialService.CacheGameObject` |

## Fireplaces and torches

| Rule | Lives in |
|------|----------|
| Scripts' `Wld_SetMobRoutine(hour, minute, scheme, status)` is stored per scheme, matched case-insensitively | `Domain/Vm/VmWorldExternalDomain.cs`, `Services/Vobs/MobRoutineService.cs` |
| The fire's scheme is its visual name without extension (`GetVisualScheme`), uppercase | `ZenKitExtension` |
| Fire trees follow the routine (lit at night), checked once per second for all fires, no Update per fire | `Services/Vobs/FireService.cs` |
| Indoors (within `Constants.Fires.IndoorRadius`, 3 m, of light mapped polygons) a fire always burns | `VobInitializerDomain.CreateFire`, `StationaryLightsService.IsNearLightMappedPolygons` |
| Further than `Constants.Fires.CullDistance` (60 m) from the camera the fire's effects are off | `FireService`, `Const/Constants.cs` |
| Touching a fire (VR or Flat) lights it or puts it out until the routine switches | `Services/Vobs/MobInteractionService.cs` -> `FireService.TryToggle` |
| Fire states are not saved; after a load the routine decides | `FireService` |

## Fire trees

| Rule | Lives in |
|------|----------|
| A fire is its own `.zen` file; its VOB positions are relative to the fire's slot | `VobInitializerDomain.CreateFireVobs` (`isRelativeToParent`, passed down to the children) |
| Other child VOBs stand at their world position | `VobInitializerDomain.InitVob` |
| The cache loader makes the positions relative once (zeroes the roots, shifts the children) | `Services/Caches/ResourceCacheService.cs` (`TryGetWorld`) |

## Indoor sky

| Rule | Lives in |
|------|----------|
| A world whose BSP tree is indoor (mines, temples) has no sun and no fog, the time of day doesn't darken it | `Services/World/SkyService.cs` |

## Shadows and darkness

Why: Docs/adr/2026-11-22-pcvr-shadows-lighting.md. PC only - the Quest quality level (URP-Performant) draws no shadows
and no SSAO, the shaders compile those parts away there.

| Rule | Lives in |
|------|----------|
| The "Sun" directional light is URP's main light (shadow map only, Gothic's shaders take `_SunColor`): rises 6:00, highest 12:00 (60°, path tilted), sets 18:00; at night the moon on the same path with a weaker shadow, faded out at the horizon. Moves every game second | `Services/World/SkyService.cs` (`RotateSun`, `UpdateSunLight`), `DeveloperConfig.SunUpdateInterval` |
| `_SunDirection` is the light's direction (sun/moon to the ground) - it used to be Euler angles | `SkyService`, `Shaders/GothicIncludes.hlsl` |
| Gothic's lit shaders cast shadows with the main pass' wind, alpha cut (plants) and fatness (NPCs); depth/normals passes feed SSAO | `Shaders/GothicShadowCaster.hlsl`, `Shaders/GothicDepthPasses.hlsl` |
| The sun's shadow darkens only the sun's part: baked light of sun-facing outdoor polygons and VOBs (not light mapped ones), the sun term of objects/NPCs | `Shaders/GothicShadows.hlsl`, `Lit-World`, `Lit-AlphaToCoverage`, `Lit-SingleMesh` |
| SSAO (HighFidelity renderer): darkens baked/ambient light, less the sun and lights | `GothicShadows.hlsl` (`GothicGetOcclusion`) |
| The pooled lights closest to the camera (option "Shadow range": 4 within 20 m up to 12 within 50 m, spell lights first; not baked into lightmaps) cast cube shadows; the pool marks them (slot w < 0) and skips them, the shaders light them through URP | `Services/World/StationaryLightsService.cs` (`UpdateShadowLights`, `ShadowLightCount`), `StationaryLighting.hlsl`, `GothicShadowedLights` |
| Cached lamp shadows (Docs/adr/2026-11-23-cached-lamp-shadows.md): every pooled lamp/fire in reach (not baked, not spell lights, up to 256) gets 6 faces of distance-to-the-nearest-wall (R16 Texture2DArray, 128 px) drawn once with the `GothicLightCache` pass (world chunks by bounds, VOBs by colliders on Default/Ignore Raycast/VobMovable); drawn again when the collider count around it changes. The slot goes into the light's color alpha (indoor + 2 * (slot + 1)); `StationaryLightContribution` samples it (2x2), lights without a slot fall back to the roof rule | `Domain/Meshes/LightShadowCacheDomain.cs`, `StationaryLightsService` (`UpdateShadowCache`, `IsShadowCacheWanted`), `StationaryLighting.hlsl`, `GothicShadowCaster.hlsl` (`GOTHIC_LIGHT_CACHE`) |
| The hero's torch / Light spell (`ClaimHeroLight`) and spell projectiles (`AddDynamicShadowLight`, max 4) cast shadows too; near plane keeps the stick and hand out | `StationaryLightsService`, `VRTorch`, `VRSpellFx`, `VRSpellThrow` |
| A light under a light mapped ceiling (`IsIndoor`) skips outdoor surfaces - slot color alpha 1, `normal.w` marks outdoor surfaces | `VobInitializerDomain.CreateLight`, `StationaryLightsService.GetSlotColor`, `Lit-World` (`StationaryLighting`) |
| The VR hero body casts shadows; its head (scaled away for the camera) has a `ShadowsOnly` copy following the head bone | `Gothic-VR/.../VRHeroBody.cs` (`CreateShadowHead`), `NpcHeadMeshBuilder` (head without NpcContainer) |
| Their index among URP's additional lights, every frame - URP's 8 per-object lights cut a lamp off at a world chunk's edge | `Adapters/Rendering/GothicShadowLightsFeature.cs` (on `URP-HighFidelity-Renderer`) |
| Only Unity lights within 40 m stay enabled: URP takes 256 visible lights, the city has ~770 | `StationaryLightsService.UpdateUnityLights` |
| Renderers list their lights shadow casting first, then closest | `StationaryLightsService.UpdateRenderer` |
| Lamps/fires: falloff 1 - (d/R)², strength 1.4; the light sum stops at 1 like Gothic's 8 bit vertex light | `GothicIncludes.hlsl` (`StationaryLightFalloff`), `GothicShadows.hlsl` (`GothicClampLight`) |
| Nights: the world gets 50% of the night sky's light; rain 70% and a 35% sun shadow; indoor worlds (mines) 0.12 | `SkyService` (`SetDayLight`) |
| Outdoor lamps/fires (no light mapped ceiling 2-8 m above) dim by day: 100% at night, 20% at noon | `StationaryLightsService.SetDaylight`, `HasLightMappedCeiling`, `VobInitializerDomain.CreateLight` |
| Option "Shadows" (Immersion, Gothic.ini `shadows`): 0 off, 1 hard, 2 medium, 3 soft (default) - sun and point lights | `Constants.IniNames.Shadows`, `VRContextMenuService.CreateShadowsChoice` |
| Option "Shadow range" (Immersion, Gothic.ini `shadowRange`, slider 0..1, default 0.75) | `Constants.IniNames.ShadowRange`, `VRContextMenuService.CreateShadowRangeSlider` |
| Lit/World is SRP batcher compatible: every pass has the same `UnityPerMaterial` with every material property (`_FocusBrightness` too); the VOB lightmap tint goes into the renderer's own material, no MaterialPropertyBlock. Check: `ShaderUtil.GetSRPBatcherCompatibilityCode` | `Shaders/Lit-World.shader`, `StationaryLightsService.ApplyWorldLight` |
