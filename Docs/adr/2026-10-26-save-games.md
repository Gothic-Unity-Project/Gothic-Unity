# ADR: Save games

- Status: accepted (PR #447), tested in Gothic 1, Gothic 2 needs a tester
- Date: 2026-10-26
- Technical details: Docs/save-game.md

## Context

Saving and loading were switched off. A Gothic save holds the whole world: every item, every NPC with its stats, inventory and what it is doing, the state of doors, chests, gates and levers, quests and the time of day. Some of what we keep track of has no place in Gothic's format, for example the items in the VR holsters.

## Decisions

1. **Gothic's own save format holds everything it can hold.** What the original game saves, we save the same way, in the same place. Only what Gothic has no field for goes into a small extra file next to it. Not chosen: a save format of our own, which would mean keeping two copies of the game state in sync and would close the door to loading original saves later.
2. **A save folder of our own.** We never write into Gothic's saves or those of a mod starter, so trying Gothic-Unity can't damage an existing game.
3. **A save is complete or not there.** It is written to a temporary folder first and only replaces the slot when it's done. A failed save keeps the old one. Loading reads the save and its world before the running game is replaced: a save that can't be read keeps the running game.
4. **Saving is refused when Gothic refuses it:** while the hero is dead, during a dialog or without a running world. The save menu says why.
5. **NPCs carry on like in Gothic.** An NPC that was attacking, fleeing or knocked out does that again after loading, starting the behaviour from its beginning. Not chosen: saving every planned step of an NPC, as OpenGothic does; that can come later.
6. **Leaving a world keeps it as it was.** Changing levels and coming back works like loading a save: the world's start scripts and start triggers don't run again.
7. **In VR, what you hold belongs to your inventory.** Items in the hands are in the backpack after loading, items in the holsters stay in the holsters.

## Consequences

- Only saves made by Gothic-Unity can be loaded for now. Original saves would need their inventories converted.
- A few bugs in the save library had to be worked around until they are fixed there.
- Not yet: quicksave and Gothic 2's save slots 16 to 20.
