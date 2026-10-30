# ADR: NPCs use items, whirl around and turn away

- Status: proposed
- Date: 2026-10-30

## Context

Several things NPC scripts ask for did nothing: drinking a potion after a fight, whirling around to someone behind them, turning their back after a beating, and taking off their weapons.

## Decisions

1. **An NPC using an item runs the item's own script, like the hero's use does.** Potions and food are used up. NPCs heal themselves after fights again.
2. **Whirling around is a quick turn to the other one; turning away turns the back to him.** Not chosen: the surprised whirl animation of the original, which can come with the animation work.
3. **Taking off the weapons removes them from what the NPC wears.** They stay in its inventory.
4. **These script functions are only connected where the game's scripts know them.**

## Consequences

- Fights end more like in Gothic: the beaten look away, the hurt drink potions.
- An NPC can look into its inventory by category (the first food item, the second weapon) like in the engine: G1 NPCs find their food after a fight and plunder the knocked out hero again.
