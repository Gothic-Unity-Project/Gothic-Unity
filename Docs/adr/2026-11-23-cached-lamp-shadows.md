# ADR: Cached shadows for every lamp and fire in reach (PC)

- Date: 2026-11-23
- Technical details: Docs/world-lighting.md (section "Shadows and darkness", cached lamp shadows)

## Context

With URP's shadows only the few lamps closest to the player cast a shadow (Docs/adr/2026-11-22-pcvr-shadows-lighting.md, decision 2). URP draws every shadow casting light again in every frame, six times, and takes 256 shadow tiles at most (about 40 lamps); 40 lamps made the frame rate tank. All other lamps lit through walls: candles inside houses glowed on the outside walls and the street, distant buildings showed red patches. GD3D11 gives every light a shadow because it keeps a light's shadow map and draws it again only when something around the light changes. URP has no such cache (only HDRP has).

## Decisions

1. **Our own cache of cube shadows for the pooled lamps and fires.** Every lamp and fire in reach (the light pool, up to 256 lights, not lights baked into lightmaps, not moving spell lights) gets its surroundings drawn once into six faces: the distance to the nearest wall or VOB. The world shader looks it up for these lights. Not chosen: more URP shadow lights (too slow), the roof rule alone (decision 3 of the first ADR; it can't stop light going through a wall).
2. **Only static things are in the cache.** The world mesh and VOBs cast; NPCs, items and the hero move and don't. The few lamps closest to the player keep URP's shadow, where NPCs and the hero cast.
3. **A light is drawn again only when the colliders around it change** (a VOB loads or unloads nearby). To notice that, the colliders around 4 drawn lights are counted every frame (a physics sphere query each) - steady state is cheap, not free. A new light costs about 0.4 ms of CPU once; new lights are drawn within 0.75 ms of CPU per frame (at least 1, at most 4). Not chosen: a fixed number per frame - walking through a town kept refilling the queue. A "VOB loaded here" signal instead of counting is a follow-up.
4. **PC only, with the "Shadows" option on.** Quest's shaders compile the lookup away.

## Consequences

- 48 MB of video memory on PC (256 lights x 6 faces of 128 x 128, 16 bit).
- An opened door keeps the closed door's shadow until something else makes its lamps draw again. VOBs without a collider cast no cached shadow.
- The cached shadows are 128 texels per face - softer than URP's near ones. Lights brighter in the distance could get bigger faces later.
