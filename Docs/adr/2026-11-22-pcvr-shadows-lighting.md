# ADR: Dynamic shadows and lighting on PC (GD3D11-like)

- Date: 2026-11-22
- Technical details: Docs/world-lighting.md (section "Shadows and darkness")

## Context

Gothic bakes its lighting: the sun's shadows are in the world's vertex light, the light of lamps and fires is in the lightmaps or added flat. Nothing casts a shadow at runtime, and at night the world stays fairly bright. GD3D11, the well-known D3D11 renderer mod for the original games, adds dynamic sun shadows, shadows of lamps and fires, ambient occlusion and darker nights, while keeping Gothic's look. In VR these shadows make a big difference to immersion. Our shaders could neither cast nor receive shadows, and the world had no sun light. The shaders' "sun direction" was a set of angles, not a direction.

## Decisions

1. **The sun and moon cast dynamic shadows on PC.** The sun rises at 6, is highest at 12 (60 degrees, so shadows never vanish under objects) and sets at 18. At night the moon takes its place with a weaker shadow, and the shadows fade out at the horizon. The shadow darkens only the sun's part of the light, so lightmaps, ambient light and fires stay as they are. That is how GD3D11 does it, and it keeps the baked Gothic look. Not chosen: replacing Gothic's baked light with Unity's lighting, which would be a different game.
2. **The few lights closest to the player cast shadows too.** 4 to 12 lamps and fires within 20 to 50 m (not lights baked into lightmaps; the "Shadow range" option) get a real shadow: a street lamp throws its iron cage onto the ground, NPCs and the hero cast shadows in it. The hero's torch, the Light spell and spell projectiles cast shadows as well. Every such light renders its surroundings six times per frame - with 40 of them the frame rate tanked - so all lights casting shadows this way would be far too expensive. Not chosen: shadows for every light.
3. **A light under a roof doesn't light outdoor surfaces.** A light whose position has a light mapped ceiling above (house, cave, sewer) skips vertex lit polygons and VOBs outside light mapped areas: the candles in a house no longer glow on its outside walls and the street. A stand-in for a real shadow where the light has none.
4. **The VR hero's body casts its shadow.** The head is scaled away so the camera doesn't see into it; a shadow-only copy of it casts the head's shadow, the camera never draws it.
5. **Ambient occlusion on PC.** It gives soft contact shadows in corners and under objects, and darkens the baked and ambient light more than the sun and fires (like GD3D11).
6. **Gothic's light stops at full brightness.** Lamps next to each other no longer burn the colors out. The engine's 8-bit vertex light worked the same way.
7. **Nights, mines and rain are darker; lamps and fires outdoors dim by day.** Lights stand out at night, mines are lit by their lamps and the hero's torch, and a street lamp doesn't glow on a shaded wall at noon. Lights under a roof (houses, caves, sewers) stay as they are. Gothic's lamps burn all day too, but its bright vertex light hid them. Ours are stronger, so we dim them, which is a deliberate difference from the original.
8. **Options in the VR Immersion menu: "Shadows" (off, hard, medium, soft; default soft) and "Shadow range" (how many lamps get a shadow).** Not every PC is fast. Quest stays without shadows and ambient occlusion: its quality level has them off.
9. **Gothic's world shader stays SRP batcher compatible.** Every pass of Lit/World has the same material buffer with every material property, and VOBs get their lightmap tint in their own material, not in a property block. Without it every shadow pass drew the world chunk by chunk (3100 SetPass calls in Khorinis, 190 with it).

## Consequences

- PC costs GPU time for the shadow maps (the sun's cascades plus up to 12 point lights) and ambient occlusion. Players with slower PCs pick hard shadows, a shorter shadow range or switch them off.
- Lamps beyond the shadow range have no shadow; walls between them and the player don't stop their light (only the roof rule of decision 3). Lights baked into lightmaps cast none.
- New Gothic shader passes must keep the material buffer identical, or the SRP batcher drops the world again.
- Not done in this step: height fog, god rays, bloom and tonemapping, water reflections.
