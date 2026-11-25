# ADR: Items from working-place animations are in the NPC's hands and inventory

- Date: 2026-11-25
- Technical details: Docs/npc-ai.md

## Context

Gothic's animations of working places put items into the NPC's hands: the smith's hammer at the anvil, the glowing blade from the forge, a drummer's sticks, a cook's pan or meat.
We showed none of them.
The original also puts these items into the NPC's inventory for as long as he holds them.

## Decisions

1. **The items appear in the hands like in the original, and go with the end of the animation or when the NPC is interrupted.** The workplaces look alive again, and nothing stays in a hand after the work is done.
2. **While held, the items are in the NPC's inventory like in the engine, so they can be bought or stolen.** This keeps the original's "infinite steel" trick at Harad on purpose. Not chosen: only showing the item, which hides what the original lets players do.
3. **Nothing extra is saved.** An item saved while held stays in the NPC's inventory, like the original.

## Consequences

- Harad's hot blade can be bought or pickpocketed while he holds it.
- A save and load while an item is held gives the NPC one more item.
- NPC fights that look at the inventory may see the hammer, like in the original.
