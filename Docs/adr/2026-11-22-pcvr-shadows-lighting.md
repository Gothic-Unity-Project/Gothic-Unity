# ADR: Dynamic shadows and lighting on PC (GD3D11-like)

- Date: 2026-11-22
- Technical details: Docs/world-lighting.md (section "Shadows and darkness")

## Context

Gothic bakes its lighting: the sun's shadows are in the world's vertex light, the light of lamps and fires is in the lightmaps or added flat. Nothing casts a shadow at runtime, and at night the world stays fairly bright. GD3D11, the well-known D3D11 renderer mod for the original games, adds dynamic sun shadows, shadows of lamps and fires, ambient occlusion and darker nights, while keeping Gothic's look. In VR these shadows make a big difference to immersion. Our shaders could neither cast nor receive shadows, and the world had no sun light. The shaders' "sun direction" was a set of angles, not a direction.

## Decisions

1. **The sun and moon cast dynamic shadows on PC.** The sun rises at 6, is highest at 12 (60 degrees, so shadows never vanish under objects) and sets at 18. At night the moon takes its place with a weaker shadow, and the shadows fade out at the horizon. The shadow darkens only the sun's part of the light, so lightmaps, ambient light and fires stay as they are. That is how GD3D11 does it, and it keeps the baked Gothic look. Not chosen: replacing Gothic's baked light with Unity's lighting, which would be a different game.
2. **The few lights closest to the player cast shadows too.** Up to 6 lamps and fires (not lights baked into lightmaps) get a real shadow: a street lamp throws its iron cage onto the ground. Every such light renders its surroundings six times, so all lights casting shadows would be far too expensive. Not chosen: shadows for every light.
3. **Ambient occlusion on PC.** It gives soft contact shadows in corners and under objects, and darkens the baked and ambient light more than the sun and fires (like GD3D11).
4. **Gothic's light stops at full brightness.** Lamps next to each other no longer burn the colors out. The engine's 8-bit vertex light worked the same way.
5. **Nights, mines and rain are darker; lamps and fires outdoors dim by day.** Lights stand out at night, mines are lit by their lamps and the hero's torch, and a street lamp doesn't glow on a shaded wall at noon. Lights under a roof (houses, caves, sewers) stay as they are. Gothic's lamps burn all day too, but its bright vertex light hid them. Ours are stronger, so we dim them, which is a deliberate difference from the original.
6. **A "Shadows" option in the VR Immersion menu: off, hard, medium, soft (default soft).** Not every PC is fast. Quest stays without shadows and ambient occlusion: its quality level has them off.

## Consequences

- PC costs GPU time for the shadow maps (the sun's cascades plus up to 6 point lights) and ambient occlusion. Players with slower PCs pick hard shadows or switch them off.
- The hero's torch and spells don't cast shadows yet (follow-up). Neither do lights baked into lightmaps.
- Not done in this step: height fog, god rays, bloom and tonemapping, water reflections.
