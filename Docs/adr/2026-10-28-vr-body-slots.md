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

## Consequences

- Open: the chests' drop zone and the quick stash do the same thing in two places, and moving an item into the inventory should become one rule for the whole game. Both are planned as follow-ups.
