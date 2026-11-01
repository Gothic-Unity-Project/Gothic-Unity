# ADR: Speed potions

- Status: proposed
- Date: 2026-10-31

## Context

Gothic 1's three haste potions and Gothic 2's speed potion lay a sprint animation over the hero for a few minutes. The command behind them didn't exist, so the potions did nothing.

## Decisions

1. **A timed animation overlay works like the engine's:** it stays for its time, then the one before comes back. A newer overlay put on meanwhile stays.
2. **The VR hero has no animations, so he moves as much faster as the overlay's walk and run do,** measured from the animations themselves.
3. **A second potion starts the time anew** instead of adding up.
4. **The potion works on land only,** like Gothic, where the sprint overlay only replaces the run animations on land: wading and swimming stay as slow as without it. Its time runs on in the water.
5. **The VR player's walk and run speed has one owner,** which combines the normal speed, the potion and the water. Swimming only says whether the player is in the water. Not chosen: the potion and swimming each writing the speed and putting back what they found, where the last writer won - leaving the water during a potion could leave the hero at half speed on land.

## Consequences

- The faster movement isn't saved: a game loaded during a potion's time starts at normal speed.
- In the water the player wades at half his walking speed at every depth, also when he gets into deep water without wading first (a jump, a teleport).
