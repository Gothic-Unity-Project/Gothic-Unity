# ADR: NPCs fall, wade and swim like in Gothic

- Status: proposed
- Date: 2026-10-31

## Context

NPCs and monsters walked straight over cliff edges, landed unhurt, and walked along the bottom of lakes and rivers. In the original game a fall from high up hurts, NPCs stop at deep edges, wade through shallow water, swim in deep water if they can, and most of all: monsters that can't swim stop at the shore, which is how the hero escapes them. BoroBongo's experimental branch has both parts working; this ports them.

## Decisions

1. **A fall from higher than the guild's fall height costs the guild's fall damage per meter beyond it, less the fall protection.** The engine's formula and the scripts' values per guild. A deadly fall kills.
2. **A walking NPC stops at an edge deeper than three quarters of its fall height,** like the engine. Running or pushed NPCs can still fall. Water below an edge counts as ground.
3. **Water depth decides with the guild's knee and chest depths:** knee deep plays the wading walk, chest deep makes the NPC swim at the surface if its model has swim animations, and is a wall for one that hasn't (zombies, skeletons, trolls, golems in Gothic 1). Flying monsters hover over the water.
4. **An NPC that falls into water deep enough to swim swims and isn't hurt.** Like the engine; non-swimmers hit the bottom.

## Consequences

- Escaping by swimming works against non-swimmers; most Gothic 1 monsters (scavengers, wolves, lurkers, snappers, shadowbeasts) do swim, as in the original.
- Moving NPCs cast up to two short rays per frame for water and one for edges; standing NPCs on land none.
- Still open: diving NPCs, drowning, NPCs climbing ledges, and the splash effect when something falls into water.
