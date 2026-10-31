# ADR: VR backpack, equipping and items in the hands

- Status: proposed
- Date: 2026-10-31
- Technical details: VRBackpack, VRItemUser, VRBackpackEquipToggle, VRStackSplitter (Gothic-VR); IInventoryOwner, NpcInventoryService, DocService (Core); DeveloperConfig "Backpack and Items"

## Context

In VR the player has no inventory screen. Items are physical: in the hands, in holsters, in the backpack. Gothic's inventory actions (equip, use, read, split a stack, loot a corpse) all need a gesture.

## Decisions

1. **One backpack prefab for every inventory.** The hero's backpack, an NPC's loot and a container's loot are the same prefab. Each one shows an `IInventoryOwner` (hero, NPC, container), and changes go through it. Not chosen: a separate loot UI per case.
2. **Equip and use with both hands and the trigger.** Holding an item with both hands and pressing the trigger uses it, like the engine's "use" (armor, amulets, rings and belts are put on; potions, food and pouches are used). In the backpack, a trigger click with an empty hand toggles a worn item. Equipped items get an [E] badge, and taking the last one out of the backpack takes it off. Not chosen: dropping an item onto the body, which can't be told apart from storing it.
3. **The engine's equip rules apply to script equips too.** EquipItem creates the item if the NPC doesn't have it, and protection plus on_equip apply. An armor change sets the NPC's visual (MdmName), and the VR hero body follows it. Swapping an NPC's built mesh comes later.
4. **Small gestures instead of menus:**
   - the trigger next to a held stack takes one piece;
   - documents are read while held with both hands;
   - joints are smoked at the mouth;
   - a key in the inventory opens its lock when the lock is touched.
5. **Holsters and the shoulder backpack turn with the head's yaw at once.** Turning while looking down no longer brings them into view.

## Consequences

- Trade, theft, the favorites ring and save tracking of loose items plug into the same owners and backpack later.
- Save/load: equipped items and the inventory are saved by the existing save. Items moved between the world and the backpack are tracked in the save/load PR.
