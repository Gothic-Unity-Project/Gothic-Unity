# ADR: The world around the player is built before the view opens

- Status: proposed
- Date: 2026-10-28

## Context

After loading a save, the loading screen closed as soon as the world mesh and the NPCs were there. Everything else (lights, chests, plants, furniture, NPC bodies) was only queued and then built in the background, half a frame per frame and in no particular order. In the Old Camp that meant about 3,000 objects popping in over 8 seconds. A light right next to the player could come last, a barrel far away first.

Building them is cheap: all 3,000 together take under 2 seconds of computing time. The pop-in came from spreading that work thinly, not from the work itself. The earlier code chose pop-in on purpose, to keep the loading screen short.

## Decisions

1. **The view stays black until what's around the player is built.** After the player is put into the world, the screen stays black until every queued object and NPC within 50 meters is built. Meanwhile only those are built, and they may use about three frames of time per frame, as nobody sees a slow frame on a black screen. After at most 5 seconds the view opens anyway. Not chosen: keeping the old pop-in to save that time.
2. **The wait starts once the player has arrived.** In VR the player is moved into the world one frame after the loading is done, and only then the objects around them are queued. The wait therefore first waits for the player (at most one second).
3. **Waiting objects are built nearest first.** Both queues, for objects and for NPCs, hand out what's closest to the player first. Everything that is still missing appears far away. The order is refreshed when new objects come in or the player moved, at most four times a second.
4. **The background building keeps half a frame per frame.** Once the view is open, nothing changes for the frame rate.

## Consequences

- Measured in the editor with a save in the Old Camp: the screen is black for about 1.6 seconds, then all 770 objects within 50 meters and the NPCs there are in place. The other 2,300 objects far away follow within about 3.5 seconds, with no frame over 85 ms. Before, all 3,100 objects popped in over about 8 seconds after the loading screen, in random order, with frames of up to 0.4 seconds.
- Lights near the player are there from the first moment, because they are built first.
- The 50 meters and the 5 seconds are a tradeoff between a short black screen and pop-in. Both can be tuned in one place.
