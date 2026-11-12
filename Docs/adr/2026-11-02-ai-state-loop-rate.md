# ADR: How often an NPC's state loop runs

- Date: 2026-11-02
- Technical details: no technical doc for the NPC AI yet

## Context

Every NPC runs the loop part of its current AI state (standing guard, sitting at a fire, fighting) whenever it has nothing left to do. Many of these loops only re-check things or turn the NPC towards its spot, which is done at once. So the loop ran again in the very next frame, about 90 times a second in VR, for every NPC in reach. In Destiny of Myrtana the orc guards alone started 65,000 "turn to your spot" actions in a few minutes. That costs main-thread time on the Quest and makes random idle animations far more frequent than intended.

The reference engine (OpenGothic) runs a state loop at most once per perception time of the NPC (the scripts set it, usually 0.3 to 1 second; 5 seconds if they never do), and earlier only after fight moves.

## Decisions

1. **A loop pass that only did instant things waits for the NPC's perception time before the next pass.** Same rate as the reference engine, and idle NPCs stop costing a script call per frame. Not chosen: a fixed rate for everyone, which would ignore what the scripts set.
2. **After the NPC really did something (walked, played an animation, fought), the loop runs at once when it is done.** Fights, following and walking routines keep their current pace, so vanilla G1 and G2 play as before. Not chosen: the reference engine's exact timing (wait the rest of the perception time even after a short action), which only adds small pauses without a benefit.
3. **A new state's first loop pass runs right away.**

## Consequences

- Idle NPCs (guards on their spot, NPCs at a fire) react to time-based checks in their loop up to one perception time later than before.
- Random idle animations chosen per loop pass happen at the scripts' intended rate instead of once per frame.
- Far fewer AI log lines in the editor.
