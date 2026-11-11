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
