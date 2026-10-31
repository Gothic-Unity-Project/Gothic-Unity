# ADR: NPCs and monsters make footsteps

- Status: proposed
- Date: 2026-10-30

## Context

NPCs and monsters walked and ran without a sound. In the original game their walk and run animations mark the moments a foot touches the ground, and at each of them the engine plays a step that fits the ground under the NPC: sand, wood, stone, water and so on, taken from the material of the ground the NPC stands on. Gothic 1's world marks almost all of its ground as "undefined", which plays the plain stone step.

## Decisions

1. **Steps come from the animations, like in the original.** The walk and run animations already say when a foot lands, which step sound to play and how far it carries, so steps stay in time with the feet and each creature sounds like itself.
2. **Only two grounds: water, or "undefined" for everything else.** The water step plays while the feet are in water, the "undefined" step everywhere else. In Gothic 1 this is what the original plays almost everywhere anyway. Not chosen: reading the ground's material like the engine does, which our world loading doesn't keep yet.
3. **No guessing the ground from its texture.** An earlier branch picked the step by the name of the ground's texture. Not chosen: texture names are not what the original uses and say nothing reliable about the ground, so the guess plays steps the original doesn't play there, and it costs a ground lookup per step for every walking NPC.
4. **A step plays on top of the NPC's other sounds.** It must not cut off a swing or a scream.

## Consequences

- Walking and running NPCs and monsters can be heard, each as far as its animation says (about 10 meters unless set otherwise).
- Where a world marks other grounds (wood, sand, earth), steps sound like stone except in water. Using the real ground material later only needs it kept when the world loads.
- Every walking NPC near the player now plays a sound per step; to be measured on Quest in a crowded place like the Old Camp.
