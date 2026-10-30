# ADR: Warnings and other signals between NPCs reach the NPCs around the sender

- Status: proposed
- Date: 2026-10-30

## Context

Gothic's scripts let an NPC send a signal to the NPCs around it, for example a guard that warns the player, an NPC that is attacked, or one that sees a theft. In Gothic the signal goes to every NPC within its range, but not to the sender itself and not to the player, and it arrives in their next update, not in the middle of the sender's own script.

Our game ran the signal on the sender only, right away and inside the sender's own script. Other NPCs never heard a warning or a call for help. The sender reacted to its own signal instead, which in Gothic 2 can start the same reaction again from within itself (a guard that warns the player warns again). Each round nests one more script call into the running one. In Khorinis the script engine ran out of its stack after about two minutes and took the whole game down.

## Decisions

1. **A signal is collected and handed out in the next frame, like in Gothic.** Every NPC that is awake, alive and not the player gets it, if it's within the signal's range of where the sender stood. Not chosen: handing it out right away, which keeps the nested script calls.
2. **The sender doesn't get its own signal.** That is how the scripts expect it, and it ends the loop of a guard warning itself.

## Consequences

- Guards and citizens around a warning, a fight or a theft react to it now, as in the original.
- No signal runs inside the script that sent it, so a chain of reactions can't fill up the script engine anymore.
- Signals reach their NPCs one frame later than before.
