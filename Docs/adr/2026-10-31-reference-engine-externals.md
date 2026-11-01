# ADR: Script commands from the reference engine

- Status: proposed
- Date: 2026-10-31

## Context

The scripts of both games call a handful of commands the reference engine (OpenGothic) knows and we didn't: short remarks while busy ("Verstehe", "Ooh", 54 times in Gothic 2), pointing at a place or person (guards pointing the hero out, Guy showing his hut), body state flags that some spell states ask for, single perceptions that Gothic 1's charm, shrink and berserk spells send, playing an animation at once, the NPC being looked at, the distance to the hero, an item from the inventory, whether an NPC is in a cutscene, and whether it sees the source of a noise.

## Decisions

1. **A short remark doesn't stop what the NPC does:** no gesture, and the NPC goes on while it speaks. The hero says his at once.
2. **Pointing turns the NPC to the place or person and raises its arm** until the scripts lower it.
3. **An NPC is "in a cutscene" while it talks to the hero,** like in the reference engine: there are no cutscenes, and nobody attacks during a dialog.
4. **The source of a noise is the NPC that made it,** so seeing the source means seeing that NPC.
5. **The language switches do nothing,** since the texts come from the installed game.
