# ADR: Script commands from the reference engine

- Status: proposed
- Date: 2026-10-31

## Context

The scripts of both games call a handful of commands the reference engine (OpenGothic) knows and we didn't: pointing at a place or person (guards pointing the hero out, Guy showing his hut), body state flags that some spell states ask for, single perceptions that Gothic 1's charm, shrink and berserk spells send, playing an animation at once, the distance to the hero, an item from the inventory, whether an NPC knows the hero, and whether an NPC is in a cutscene.

Short remarks while busy and seeing the source of a noise come with the NPC AI changes (#479), the NPC being looked at with the script command fixes (#471).

## Decisions

1. **Pointing turns the NPC to the place or person and raises its arm** until the scripts lower it.
2. **An NPC is "in a cutscene" while it talks to the hero,** like in the reference engine: there are no cutscenes, and nobody attacks during a dialog.
3. **An NPC remembers that it knows the hero** for the rest of the session. It isn't saved yet: after loading a save, NPCs have forgotten it.
4. **The language switches do nothing,** since the texts come from the installed game.
