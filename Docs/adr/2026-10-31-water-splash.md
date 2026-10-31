# ADR: Splashes

- Status: proposed
- Date: 2026-10-31

## Context

In Gothic whoever falls or jumps into deep water makes a splash, with the splash particles and sound. Here the water stayed still.

## Decisions

1. **A splash comes when the hero or an NPC goes from dry ground straight into water deep enough to swim,** from a fall, a jump or a step off an edge. Walking in from the shore passes shallow water first and doesn't splash.
2. **Only a real entry splashes.** Whoever appears in the water makes no splash: an NPC spawned there or coming back into view at its routine's place, the hero teleported there, and everyone already in the water when a world or a save is loaded. The first water found after such a moment is taken silently. Not chosen: a splash at every first contact with deep water, which made a load next to a lake splash at every swimmer at once.
3. **The splash sits on the surface where the hero or the NPC enters.**

## Consequences

- An NPC that falls into the water right after it appeared, before it took a step, doesn't splash. Rare, and quieter than a wrong splash.
- For the hero the splash looks at the depth of the water where his feet go below the surface, as his own water levels pass knee depth on the way down and never jumped from dry ground to swimming.
