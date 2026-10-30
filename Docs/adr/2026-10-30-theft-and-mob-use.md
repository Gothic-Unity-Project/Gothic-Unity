# ADR: NPCs notice theft and the use of their things

- Status: proposed
- Date: 2026-10-30

## Context

In Gothic, NPCs who see the hero take an item that lies around, or use a chest, door or bed that belongs to them or their guild, react: a warning, a chase, a fight. Guards of item piles react to any item taken. None of this happened here: the scripts' reactions were never asked, and the functions they use to check a mob's owner didn't exist.

## Decisions

1. **Like the engine, the NPCs around are told when the hero takes an item and when he uses a mob.** Their scripts decide what it means. For an item they get the item itself, so they can check its owner; for a mob they ask whose it is.
2. **Only items lying in the world since it was loaded count as taken.** In VR the hero moves his own things in and out of the backpack all the time; taking them out again isn't theft.
3. **Using a mob means grabbing it: a chest's lid, a door, a bed, a wheel.** Grabbing the same mob again within a few seconds counts once.
4. **The scripts can ask which mob the hero uses and whether it belongs to an NPC or a guild,** from the owners the world gives the mobs.

## Consequences

- Taking things in front of their owners or opening their chests has consequences again.
- Picking pockets isn't part of this; it comes with its own way to reach into an NPC's pockets in VR.
