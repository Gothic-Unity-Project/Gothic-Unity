# ADR: Cooking and smithing at mobs that change an item

- Status: proposed
- Date: 2026-10-30

## Context

In Gothic 1, raw meat is fried in a pan, and a sword is made step by step: raw steel heated in the forge, hammered on the anvil, cooled in the barrel and sharpened on the grindstone. These mobs have no script of their own: the hero's animation at the mob swaps the item for the next one. In VR the hero plays no such animation, so using these mobs did nothing.

## Decisions

1. **A mob that needs an item and has no script of its own changes the item like its animation would.** What it becomes is read from the game's animation data, so mods with their own such mobs work too. The change can sit in the animation into the mob or out of it: Gothic 1's mobs change the item when the hero leaves, Gothic 2's forge when he steps in (found on BoroBongo's branch).
2. **In VR the hero grabs the mob while holding the item in the other hand.** The held item disappears and the new one goes into the inventory. Holding nothing, the item is taken from the inventory. Without the item, Gothic 2 says what's missing, Gothic 1 says nothing.
3. **Mobs with a script of their own keep it** (Gothic 2's alchemy table, anvil and rune table open their dialogs as before). If their animation destroys the item, it is taken before the script runs: Gothic 2's anvil takes the glowing steel and gives it back when the player leaves without forging (found on BoroBongo's branch).

## Consequences

- Gothic 1 cooking and the sword chain work in VR.
- The new item goes to the inventory, not into the hand; the player takes it out of the backpack.
