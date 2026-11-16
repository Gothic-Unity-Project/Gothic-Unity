# Settings inspection for Quest 2

- Date: 2026-11-16. Closes the "not checked" settings of `performance-quest2.md` (its sections 5 and 8) and item 2
  of `action-plan.md`.
- Method: read-only, through the Unity MCP server with the Editor in edit mode (Unity 6000.3.13f1). Serialized
  values were read with `SerializedObject`; nothing was changed, no play mode. `ProjectSettings/QualitySettings.asset`,
  the shaders and C# were read as text.
- These are the values in the project files. What the headset really does at runtime (for example the eye
  texture size the runtime picks) still needs the on-device baseline.

## Which settings a Quest build uses

| Fact | Value | Source |
|---|---|---|
| Default quality level for Android | 0 = `Low` | `QualitySettings.asset`, `m_PerPlatformDefaultQuality` |
| Render pipeline asset of `Low` | `Assets/Gothic-Core/Settings/URP-Performant.asset` | quality level 0 |
| Renderer | `URP-Performant-Renderer.asset`, no renderer features | renderer data list |
| Code that changes quality level, eye texture scale, foveation or refresh rate | none (`VRContextInteractionService.cs:161` only reads the refresh rate) | grep over `Assets/**/*.cs` |

## Render pipeline (`URP-Performant`)

| Setting | Value | Verdict for Quest 2 |
|---|---|---|
| SRP Batcher | on | good, but see "World shader" below |
| Dynamic batching | off | fine |
| Depth texture / opaque texture | off / off (camera: use pipeline settings) | good |
| HDR | off | good |
| MSAA | 4x | usual for Quest; 2x is the cheaper option to A/B |
| Render scale | 1 | fine; the lever for GPU headroom |
| Main light | per pixel, shadows off | good |
| Additional lights | per vertex, 8 per object, shadows off | see "Light VOBs" below |
| Soft shadows | off | good |
| Store actions | Discard | good for a tile-based GPU |
| LOD cross fade | on | no effect unless LOD groups exist |
| Light cookies | supported, 2048 | unused variants; could be switched off |
| Rendering path | Forward | good |
| Depth priming | disabled | good (it costs more than it saves with MSAA on tile-based GPUs) |
| Native render pass | off | worth an A/B on Vulkan |
| Intermediate texture | Auto | good (`Balanced` and `HighFidelity` use Always) |
| Post-processing | off on the VR camera (`m_RenderPostProcessing = False`) | good |

The quality level `Low` itself: `vSyncCount 0`, `lodBias 0.4`, `skinWeights 2` (two bones per vertex),
`anisotropicTextures 1` (per texture), `pixelLightCount 0`.

## Player settings for Android

| Setting | Value | Verdict |
|---|---|---|
| Graphics APIs | Vulkan, then OpenGLES3 (auto off) | good |
| Multithreaded rendering | on | good |
| Graphics jobs | on | good |
| Color space | Linear | fine |

## OpenXR for Android

| Setting | Value | Verdict |
|---|---|---|
| Loader | OpenXR only | good |
| Render mode | Single Pass Instanced | good |
| Depth submission | None | good |
| Optimize buffer discards | on | good |
| Latency optimization | Prioritize Input Polling | fine |
| Symmetric projection | off | optional Meta optimisation, A/B later |
| Meta Quest feature | enabled | needed |
| Late latching (inside Meta Quest feature) | off | optional, lowers perceived head latency |
| **Foveated Rendering feature** | **disabled** | **confirms P03: no foveation at all** |
| Foveated rendering API | Legacy | switch to the SRP Foveation API when enabling |
| Display Refresh Rate feature | disabled | fine while 72 Hz is the target; needed to request 90 Hz |
| Application SpaceWarp | disabled | fallback option only, needs motion vectors |
| Performance Settings feature | disabled | optional (CPU/GPU level hints) |
| Controller profiles | only Oculus Touch | fine for Quest 2 |
| PICO features and profiles | all disabled in the asset | **check that the Pico build enables them** (`VRBuilderActions.PerformPicoBuild`), not inspected |

## VR camera (`VR - Player XRI.prefab`, Main Camera)

| Setting | Value | Verdict |
|---|---|---|
| Far clip plane | 300 in the prefab; set at runtime from the Gothic.ini visual range | check the value a Quest player actually gets |
| Occlusion culling | on | no effect, there is no baked occlusion data |
| Allow MSAA / HDR | on / on | HDR is off in the pipeline, so no HDR |
| Spectator camera | enabled in the prefab, disables itself in `Start()` outside Editor and standalone (`VRSpectatorCamera.cs:44-47`) | fine |

## Findings this settles

| Plan item | Result |
|---|---|
| #4 foveation (P03) | **Confirmed.** The Foveated Rendering feature is disabled and no code sets a foveation level. The refresh-rate part is not a problem for a 72 Hz target |
| #5 world shader and SRP Batcher (P02) | **Confirmed by the shader source.** The SRP Batcher is on, but `Lit-World.shader` declares the material property `_FocusBrightness` at line 34, outside its `CBUFFER_START(UnityPerMaterial)` block (lines 71-78). The SRP Batcher needs every material property inside that block, so this shader falls back to the slow path. The Inspector's "SRP Batcher: not compatible" line on the shader is the 10-second proof |
| #71 light VOBs (P17) | **Confirmed.** `zCVobLight.prefab` carries an enabled real-time point `Light` (range 10, no shadows), and `StationaryLight` never disables it. The world shaders (`Lit-World`, `Lit-AlphaToCoverage`, `Lit-Water`, `Lit-SingleMesh`) do their own stationary lighting and never read URP's additional lights; only `Lit-Misc` does. So every light VOB costs URP light culling and per-object light lists for almost nothing. **Check first** what `Lit-Misc` is used for before disabling the component |
| Culling distances | `Production.asset` holds the C# defaults: VOBs 50 m (small), 100 m (medium), 200 m (large); NPCs 50 m. Nothing platform-specific for Quest |
| Depth priming, post-processing, depth/opaque texture | All off. No hidden full-screen passes |

## Still not checked

- Whether the SRP Batcher verdict also applies to the other custom shaders (only `Lit-World` was read in detail).
- `GothicUI`, `Lit-Misc`, `Skybox`, `ThunderShader` and the three VR shaders have no `UnityPerMaterial` block at
  all; whether that matters depends on how many renderers use them.
- The Pico build's OpenXR feature set.
- The far clip plane and eye texture resolution a Quest 2 really runs with.
- Everything measured: this file lists settings, not frame times.
