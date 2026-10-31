# ADR: NPCs use items, whirl around and turn away

- Status: proposed
- Date: 2026-10-30

## Context

Several things NPC scripts ask for did nothing: drinking a potion after a fight, whirling around to someone behind them, turning their back after a beating, and taking off their weapons.

## Decisions

1. **An NPC using an item runs the item's own script, like the hero's use does.** Potions and food are used up. NPCs heal themselves after fights again. Like in the original, the NPC lifts the item with its own animation (the potion to the mouth) and puts it away after; an item without both animations works at once, and an item the NPC doesn't carry isn't used.
2. **Whirling around is the startled jump around of the original, towards the other one; turning away turns the back to him.** Creatures without that animation just turn to face the other one.
3. **Taking off the weapons removes them from what the NPC wears.** They stay in its inventory. A drawn weapon leaves the hand too, and the NPC stands weaponless again.
4. **These script functions are only connected where the game's scripts know them.**
5. **Clearing an NPC's inventory takes everything but what it wears or holds and its mission items,** like the engine.
6. **When a script asks for an NPC through a variable like "self", it gets the NPC the variable points to,** like the engine. Scripts that hand over "self" (killing an NPC by script, for example) work again.

## Consequences

- Fights end more like in Gothic: the beaten look away, the hurt drink potions.
- An NPC can look into its inventory by category (the first food item, the second weapon) like in the engine: G1 NPCs find their food after a fight and plunder the knocked out hero again.
