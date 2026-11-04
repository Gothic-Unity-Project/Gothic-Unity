# ADR: Rooms and what NPCs notice about the hero

- Status: proposed
- Date: 2026-10-31
- Technical details: Docs/architecture.md (Daedalus VM, AI); ADR 2026-10-31-npc-perceptions.md

## Context

Huts and houses in Gothic belong to someone: the scripts give rooms to guilds and NPCs react when the hero walks into a room that isn't his, opens someone's chest, tries a lock or takes something that lies around. The engine tells the NPCs about it on its own (entering a room, using an object), the scripts decide the reaction. None of this existed: walking into any hut, opening any chest or taking anything went unnoticed.

## Decisions

1. **A room is the floor under the hero's feet.** The world's rooms are its portal sectors; their floors are marked when the world is built, like OpenGothic looks at the polygon under the feet. Walls, roofs and platforms overhead don't count, a step out of the door already does. A new room counts after half a second, so standing on a doorstep doesn't flip in and out. Not chosen: anything near the room's walls and roof - standing next to a hut counted as being inside.
2. **Walking into another room and using an object tell the NPCs around.** Grabbing a chest lid, a door, a bed or a wheel, or putting a lock pick into a lock, counts as using it; the same object again within 2 seconds counts once. The scripts decide who cares (the owner, the guild of the room).
3. **Taking an item that lay in the world tells the NPCs around,** with the item itself, so the scripts can check whose it is. Only items lying in the world since it was loaded (or put there by the scripts later) count: in VR the hero moves his own things in and out of the backpack all the time, and taking them out again isn't theft.
4. **Sneaking into a room isn't noticed,** like in the engine. The room changes anyway.
5. **Outdoors an NPC is in no room at all.** The scripts compare an NPC's room guild with "no guild": outdoors counting as "no guild" made a guard outside look like one in another room, and nobody came when the hero entered a house.
6. **No new settings** (wave 2 flag policy).

## Consequences

- Owners and guards react to the hero in their huts, at their chests and doors, and when he takes their things.
- Only the hero's used object is known; NPCs using objects don't tell anyone.
- A loaded save doesn't know whose rooms are whose yet: the scripts assign them only when a new game starts. The save/load PR runs that part again on load.
- Still open: footsteps as quiet noise and "who made the noise" (VR hero body PR, it needs the hero's walking state), picking pockets (it needs its own way to reach into an NPC's pockets in VR).
