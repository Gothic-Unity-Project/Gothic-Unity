# ADR: NPCs in crowded places without stutter

- Status: proposed
- Date: 2026-10-28

## Context

In the Old Camp the game stuttered: several times every few seconds a frame took 25 to almost 90 milliseconds. Measured with a save in the camp, normal frames were fine. The stutter came from single NPC actions that did far too much work at once, and from cleaning up the memory they left behind.

## Decisions

1. **A spoken line looks up its subtitle directly.** Before, every line, also every "Hey you!" an NPC calls out, built a list of all subtitles of the game to find one. That alone caused the worst stutter.
2. **NPCs find their way with a proper shortest-path search.** The old search was slow and took the first way it found, not the shortest. NPCs may now walk slightly different, shorter ways.
3. **NPCs that wake up together don't all check their surroundings at the same moment.** Each one starts at a random point of its rhythm. Its first check comes sooner than before, never later.
4. **An NPC only looks for enemies among the player and the NPCs that are awake.** NPCs far away from the player are frozen anyway. Not chosen: searching every NPC of the world, which cost a lot in crowded places for nothing.

## Consequences

- In the Old Camp test no frame took longer than 25 ms anymore, before several frames per 16 seconds did (up to 88 ms).
- An NPC call-out still costs a few milliseconds, mostly for loading its sound. That's the next step.
- The NPC code now marks its steps for Unity's profiler, so the next measurement is quicker.
