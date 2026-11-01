# Lighting

Which shader computes which light. The decision behind the hand torch's light:
`Docs/adr/2026-11-01-torch-hero-light.md`.

## The lights

- **Day and night** (`SkyService`): `_SunDirection`, `_SunColor`, `_AmbientColor` light objects; `_DayLightColor` scales
  the light Gothic baked into the outdoor world's vertex colors. Per vertex.
- **Stationary lights** (`StationaryLightsService`, `StationaryLighting.hlsl`): fires, torches, the light spell and the
  static lights next to Gothic's light-mapped polygons share one pool of global slots
  (`_GlobalStationaryLightPositionsAndAttenuation`, w = 1/range^2, and `_GlobalStationaryLightColors`, linear; 1023,
  512 on Quest), closest to the camera first. Each world material lists up to 16 (+16) slots of the lights around it
  (`_StationaryLightIndices`, `_StationaryLightCount`). Per vertex, linear falloff, no surface angle. A moving light
  (`MoveLight`) re-gathers its renderers, so it follows in steps.
- **Unity per-object lights**: each stationary light also has a Unity point light, which URP hands to objects as
  per-object lights. Per vertex, by surface angle.
- **Hero light** (`HeroLightService`, `GothicHeroLight.hlsl`): one point light for every pixel, by surface angle - the
  burning torch in the player's hand (`TorchService`, the torch light option). `_HeroLightPositionRange` (xyz world
  position, w = 1/range^2) and `_HeroLightColor` (linear, flicker and fade-out included), set every frame while it is
  on. Falloff (1 - d^2/r^2)^2, N.L with a wrap of 0.3. While a torch is the hero light, its own stationary light is off.

All point lights are scaled by `_PointLightIntensity` (`SkyService`) and added before the underwater tint and the fog.

## The shaders

| Shader | Used for | Per vertex | Per pixel |
|---|---|---|---|
| Lit/World | world mesh, VOBs in texture arrays | baked color x day light, stationary lights | hero light |
| Lit/AlphaToCoverage (-Dynamic) | their cut-out parts (bushes, leaves) | sun + ambient, stationary lights | hero light |
| Lit/SingleMesh (-Dynamic) | NPCs, items, other VOBs | sun + ambient, Unity per-object lights | hero light |
| Lit/Water | water | sun + ambient | - |

The -Dynamic variants add focus brightness and transparency, their lighting is the same.

## Keywords

- `_HERO_LIGHT`: global, `#pragma dynamic_branch` - a switch inside the one shader variant, so turning it on compiles
  nothing (a new variant would make the Quest freeze for a moment the first time a lit torch is held). Toggled by
  `HeroLightService` only. Off, the shaders still pass the world normal to the fragment (one half3) and test the switch.
- `_WIND` (wind sway, `WindService`, `GothicWind.hlsl`): per material, a real variant (`multi_compile_local`), set on
  the swaying VOBs' materials only while Gothic.ini's wind option is on.
