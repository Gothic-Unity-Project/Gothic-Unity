# ADR: Walking NPCs slide along walls near the player

- Status: proposed
- Date: 2026-10-31

## Context

NPCs move by their walk and run animations, which push them straight ahead. Walls and rocks only pushed them back out afterwards, so a runner stuck against a wall and a fast one could go through a rock. Gothic's NPCs slide along a wall they walk into. Checking for a wall costs a physics query, and doing it every frame for every walking NPC in the world is a real cost on Quest 2, where the town and the camps have dozens of NPCs walking at once.

## Decisions

1. **Against a wall, an NPC keeps only the part of its step that goes along the wall,** like in Gothic. Floors and slopes don't count as walls.
2. **Only NPCs near the player look for walls (25 meters).** Those are the ones he sees; farther away an NPC pushing into a wall goes unnoticed and is pushed out as before. Not chosen: every NPC in the world, for the cost on Quest 2.
3. **Each NPC looks every second frame, not every frame,** far enough ahead for both frames, and keeps the wall it found in between. The NPCs take turns, so only half of them look in any frame. These are cheap limits chosen before measuring; the real cost is to be measured on a native Quest build in a crowded place like the Old Camp.
4. **An NPC on its way to a bench, bed or other mob doesn't look for walls,** so the mob itself never stops it before its spot.

## Consequences

- NPCs near the player run around walls and rocks instead of sticking to them.
- A far NPC can still get stuck against a wall until the player comes closer.
- The distance and the number of frames are easy to change once the Quest measurement is there.
