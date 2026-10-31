# ADR: VR body slots - vibration and quick stash

- Status: accepted (PR #457)
- Date: 2026-10-28

## Context

The backpack sits on the left shoulder, a second slot on the right shoulder, two holsters on the hips. All of them are behind the back or out of sight, so reaching for the backpack or a holstered item was trial and error. The old HurricaneVR setup also let players drop an item over the left shoulder straight into the backpack, which the new setup couldn't do yet.

## Decisions

1. **Touch instead of sight.** A hand vibrates briefly and softly when it reaches a slot it can use. Not chosen: rings or markers that are always visible, since the slots are out of sight anyway.
2. **Only when there is something to do.** An empty hand vibrates at the backpack or at a holstered item, a hand carrying something vibrates at a slot that takes it. An empty hand at an empty holster stays quiet, and pointing at a slot doesn't count, only reaching it.
3. **Once per reach.** A hand doesn't vibrate again right after, for example when it touches the item it has just put in.
4. **One place decides about vibration.** The slots only report that a hand arrived, the game's vibration settings choose how it feels. A later setting for vibration strength covers these too.
5. **Quick stash over the left shoulder, like before.** An item let go there while the backpack sits on the shoulder goes straight into the inventory. The backpack stays on, so there's no need to open it.
6. **Only for the new VR setup** (XR Interaction Toolkit).
7. **One way to let go of an item in a zone.** The chests' drop zone and the quick stash share one trigger adapter (`VrDropZone`) that reports to one VR service (`VrDropZoneService`), which hands a released item to the zone a frame later. Nothing checks the hands every frame; reaching a slot is noticed when it happens and decided in one place (`VrBodySlot`). Not chosen: checking the hands against the zone every frame (cost on Quest 2) and a second drop mechanism next to the chests' one.
8. **In a hand, a holster or the backpack means in the inventory.** One rule in the core game (`PlayerService.PickUpItem`/`DropItem`) moves an item between the world and the inventory, for every way of picking it up or putting it down. Before, an item in a hand or a holster was counted in the inventory but also stayed in the world, so a save could hold it twice.

## Consequences

- The quick stash now also takes items let go after a distance grab, like the chests' drop zone does.
- The stash notices an item by its colliders touching the shoulder zone, not by the hand's position, so the zone needs a trigger on a layer that collides with items.
- Open: holstered items keep their culling entry, and items picked out of a chest come back as plain world items when dropped.
