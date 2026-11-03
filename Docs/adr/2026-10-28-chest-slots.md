# ADR: Chest slots are built when a chest is opened for the first time

- Status: proposed
- Date: 2026-10-28

## Context

In VR, a chest shows its content in 12 slots that items can be put into and taken out of. Each slot is a full interaction object of the XR Interaction Toolkit. They were part of the chest itself, so every chest that came into range (100 meters for objects of a chest's size) built all 12 slots right away, even though they stay hidden until the lid is opened. Chests that were built once only get switched off when the player walks away, so they keep their slots.

After loading a save in the Old Camp, 111 chests were built and with them 1,332 slots. Copying the slots took about 2 milliseconds per chest and starting them up about one more, with extra garbage collection on top. Most of these chests are never opened.

## Decisions

1. **The slots are their own prefab, created on the first open of a chest.** A closed chest has no slots. When the lid opens for the first time, the slots are created on the chest's base, laid out to its size and filled with its content, as before.
2. **Once created, they stay.** Closing hides them, opening again shows the same slots. Not chosen: removing them again on close, which would cost the same work on every open.
3. **There is one slot prefab, made for the XR Interaction Toolkit.** Before, a chest held one set of slots per VR backend and used the active one. A second backend would need its own slot prefab again.

## Consequences

- Measured in the editor with a save in the Old Camp: no chest slots are built while loading anymore (14 slots in the whole scene, all of them the player's backpack and holsters, instead of 1,346). The objects in the background are done about half a second sooner.
- The first opening of a chest costs about 14 milliseconds for creating its slots (measured in the editor). On Quest 2 that is at least one dropped frame. We accept it for now; it still has to be measured on a Quest build. Every later opening costs nothing extra.
- Save games aren't affected: the content of a chest is stored with the chest, not in its slots.
