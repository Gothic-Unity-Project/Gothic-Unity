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
  thread, synced to the audio sample position. World paused during videos, skippable. The player's hands (and the
  VR hero body) stay visible in the dark room.

## Key commits

`4982a263` lights + pool · `d07e1c4d` day/night · `8c4b4f51`/`d4e451c5` particles · `1fb8180a` PFX loops · `bc831b78` square smoke in builds ·
`d688f290`/`9504e2c9`/`9f984e2c` Bink · `2de7f2dc` Bink attribution

## Main files

`Services/World/StationaryLightsService.cs` (+271) · `Adapters/Vob/StationaryLight.cs` ·
`Domain/Meshes/Builder/WorldMeshBuilder.cs` · `Services/World/SkyService.cs` · Shaders `Lit-World.shader`,
`GothicIncludes.hlsl`, `StationaryLighting.hlsl`, `GothicBinkYUV.shader` · `Domain/Meshes/Builder/VobPfxMeshBuilder.cs`,
`Services/Meshes/ParticleService.cs` · `Bink/*` (new), `Adapters/Video/BinkPlayer.cs`, VR `UI/VRCinema.cs`

## Config flags

`EnableStaticLightsInLightMappedAreas`, `StaticLightPoolSize`, `EnablePooledStationaryLights`,
`StationaryLightMaxDistance`, `StationaryLightPoolRefreshSeconds`, `EnableDayNightWorldLight`,
`DayNightLightStrength`, `EnableAnimationPfx`, `EnablePfxMinimumEmission`, `EnablePfxFullRateLoops`,
`EnableWldPlayEffect`, `EnableScriptVideos`, `EnableCinemaShowsPlayer`.

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
