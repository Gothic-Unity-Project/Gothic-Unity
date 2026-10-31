# ADR: NPCs find and take items

- Status: proposed
- Date: 2026-10-31

## Context

The scripts let NPCs look for items lying around and take them: in Gothic 1 a knocked out fighter picks his weapon back up and NPCs take the weapons lying by a body, monsters eat food from the ground, and in Gothic 2 the wisp walks to the items it finds. None of the commands for this existed, so the scripts never found anything.

## Decisions

1. **An NPC finds the nearest item within its senses range of the kind the scripts ask for,** like the reference engine.
2. **Items close to the hero are left out,** since they may be in his hands or holsters, which the core of the game doesn't know about.
3. **Taking an item bends the NPC down and moves the item into its inventory;** it doesn't walk there, because the scripts send it there first when needed.
4. **The scripts' item stands for one item per kind,** so each NPC remembers the item it found last and the commands act on that one.

## Consequences

- A monster eating food takes it out of the world.
