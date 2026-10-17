# 8. Rendering: lights, day/night, particles, Bink video

[← Overview](README.md)

## What changed

**Lights**
- Static lights in light mapped areas (caves, huts) light the walls — Gothic used lightmaps there, which we don't
  render. Light mapped polygons are marked while building the world mesh.
- **Light pool like OpenGothic:** all world lights (fires and static) share the shader's light array, the ones
  closest to the camera get a slot (re-sorted every 2 s, immediately freed when unloaded). Array size 1023 on PC,
  512 on mobile (`SHADER_API_MOBILE`). G2 had ~700 fire lights, more than the old 512 array.

**Day and night**
- Gothic's sky `polyColor` scales the outdoor (vertex lit, no lightmap) world light and the sun/ambient of VOB and
  NPC shaders. Rooms, caves and fires keep their light.

**Particles**
- Spell effects around the caster, hit effects, animation PFX, `Wld_PlayEffect`, blood; world particle
  controllers and particle VOBs show up (Saturas' pentagram); loops emit at Gothic's rate (full torch flames).
- Smoke and other `BLEND` particles use plain alpha blending: the premultiplied blend relied on the URP
  `_ALPHAPREMULTIPLY_ON` variant, which builds strip for runtime-made materials (square smoke in builds only).

**Bink video**
- Original `.bik` videos play in a dark VR cinema, decoded by a C# port of OpenGothic's decoder on a background
  thread, synced to the audio sample position. World paused during videos, skippable ("(Trigger) skip" under
  the screen, localized: en/pl/de, English otherwise).

**Added 2026-10-10 – 2026-10-11**

- The Bink video cinema and the lightning strips render in both eyes (stereo instancing macros were missing -
  single-pass instanced drew them only for the left eye).
- The video cinema shows on the monitor/stream (spectator camera) too, its smoothing runs on unscaled time.

**Added 2026-10-11 – 2026-10-13**

- **Particle controllers** show their particle visual like the engine (`zCPFXControler`, OpenGothic `PfxController`) -
  `pfxName` is mostly an empty Spacer leftover `.PFX`: G2's active teleport platforms (`RESURRECTION.pfx`, visible once
  the mover raises them), cauldrons and the bandit camp's fire smoke showed nothing.
- **Texture array materials** follow the sub mesh grouping (opaque, alpha cutout, water in first-seen order) - sub mesh
  `i` got the material of Gothic's sub mesh `i`: palm leaves sampled the opaque array with alpha indices (stone
  textures without alpha on G2 addon palms, fishing nets and Mroczne Tajemnice meshes).

**Added 2026-10-15**

- **Lightmaps:** light mapped polygons (caves, mines, houses - 92% of the Old Mine, 7.7% of G1's world) show Gothic's
  lightmaps: the world's pages go into one texture array (smaller pages in a corner, uv scaled), u/v come from
  ZenGin's right/up vectors (checked against G1's world and Old Mine: the other order sampled black atlas gaps), a
  w = -1 marker in TEXCOORD1 (a mesh without the channel gets TEXCOORD0 there - a positive marker made VOBs sample
  lightmaps).
- **Per pixel stationary lights:** torches, fires, pooled lights and the Light spell are summed per pixel in Lit-World
  - per vertex a torch in the middle of a big cave wall lit nothing.
- **Indoor worlds** (BSP indoor: Old Mine, Free Mine, Orc Temple, mod caves) have no sun and no fog.
- **VOB light:** a VOB in a light mapped area takes the average lightmap light of the cells around it (2 m cells
  sampled at polygon centers while building the world, rings up to 8 m, the world's average in mostly light mapped
  worlds) through `_VobLightTint`; static lights baked into the lightmaps skip light mapped walls and these VOBs.
- **Dynamic VOB lights** that are switched on are created (they were skipped) - Mroczne Tajemnice's abandoned mine is
  lit only by them.
- **Light refresh** (Immersion): the torch's/Light spell's world light moves every 0.3/0.15/0.075 s.
- **Mod textures:** textures above 512 without the needed mips (Mroczne Tajemnice's 1024x1024 single mip ones) are
  downscaled with a generated chain (their slices stayed empty: black/stone patterns); uv scale and mip limit follow
  the slice. Meshes are cached separately for texture array and single texture use.

**Added 2026-10-13**

- **Rain around the player:** the `Rain` object sat at the world's origin below the player rig's root, which doesn't
  move - it now follows the player's head.

## Key commits

`4982a263` lights + pool · `d07e1c4d` day/night · `8c4b4f51`/`d4e451c5` particles · `1fb8180a` PFX loops · `bc831b78` square smoke in builds ·
`d688f290`/`9504e2c9`/`9f984e2c` Bink · `2de7f2dc` Bink attribution

2026-10-10/11: `9775ab32` stereo cinema · `443afd62` spectator cinema

2026-10-11/13: `87bb86c8` particle controller visuals · `4d39c699` texture array materials

2026-10-15: `b2af4399` lightmaps, per pixel lights, indoor worlds, VOB light · `25183f71` dynamic VOB lights ·
`23d763df` texture array mips · `d9963450` mesh cache per texture mode · `45ee344c` light refresh

2026-10-13: `3f9923d1` rain follows the player

## Main files

`Services/World/StationaryLightsService.cs` (+271) · `Adapters/Vob/StationaryLight.cs` ·
`Domain/Meshes/Builder/WorldMeshBuilder.cs` · `Services/World/SkyService.cs` · Shaders `Lit-World.shader`,
`GothicIncludes.hlsl`, `StationaryLighting.hlsl`, `GothicBinkYUV.shader` · `Domain/Meshes/Builder/VobPfxMeshBuilder.cs`,
`Services/Meshes/ParticleService.cs` · `Bink/*` (new), `Adapters/Video/BinkPlayer.cs`, VR `UI/VRCinema.cs`

## Config flags

`EnableStaticLightsInLightMappedAreas`, `StaticLightPoolSize`, `EnablePooledStationaryLights`,
`StationaryLightMaxDistance`, `StationaryLightPoolRefreshSeconds`, `EnableDayNightWorldLight`,
`DayNightLightStrength`, `EnableAnimationPfx`, `EnablePfxMinimumEmission`, `EnablePfxFullRateLoops`,
`EnableWldPlayEffect`, `EnableScriptVideos`, `EnableCinemaSkipHint`.

2026-10-10/11: —

2026-10-11/13: `EnablePfxControllerVisuals` (needs `EnableParticleEffects`), `EnableTextureArraySubmeshMaterials`

2026-10-15: `EnableWorldLightmaps`, `WorldLightmapBrightness`, `EnableIndoorWorldDarkness`, `EnableVobWorldLight`,
`EnableDynamicVobLights`, `EnableTextureArrayMipFix`, `EnableMeshCachePerTextureMode`

2026-10-13: `EnableRainFollowsPlayer`

## Review notes

- ✅ Shader loops are unchanged (still max 32 lights per renderer); the pool only decides *which* lights get slots.
- ✅ Bink decoder is self-contained, no Unity dependency, licensed and attributed (see its `README.md`).
- ⚠️ **Mobile untested.** 512 slots, the decoder thread and the extra light work need a Quest/Pico run.
- ⚠️ Lights are still assigned to renderers once at creation (existing limitation): lazily loaded VOB meshes are
  not lit by lights that were already active.
- ⚠️ The world vertex `NormalW` is now used as an "outdoor" flag — anything else reading it must know.

## How to test

- G1 Saturas' cave and G2 caves: walls lit by torches. Walk through Khorinis at night: fires light the ground,
  the world turns dark blue, huts stay lit.
- Start a new game: the intro video plays in the cinema, in sync, skippable.
- Watch a video in the headset (both eyes) and on the monitor/OBS at the same time.

- G2 Jharkendar: activate the canyon teleport - sparks over the platform. Cauldrons bubble in the bandit camp.
- G2 addon beach and Mroczne Tajemnice: palms have leaves, fishing nets look like nets.

- Let it rain (Marvin skip time): it rains around you anywhere in the world.

- G1 Old Mine: walls with Gothic's lightmaps, no sun/fog, the torch in the hand lights walls and VOBs, scaffolds and
  stairs as dark as the walls. G1 world: huts and caves lit, not black under roofs.
- Mroczne Tajemnice abandoned mine: blue/teal light. Siekacz's tavern, catapult, roofs: textured.
- Immersion "Light refresh" on High: the torch's light follows the hand smoothly.
