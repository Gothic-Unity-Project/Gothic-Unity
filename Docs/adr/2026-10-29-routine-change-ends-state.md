# ADR: NPCs in sight follow their daily routine by the clock

- Status: proposed
- Date: 2026-10-29

## Context

Every NPC has a daily routine: sit at the campfire until 22:00, then sleep in the hut, and so on. In Gothic, the state an NPC is in ends when the time of its routine entry is over, and the next entry starts. Our game only noted the new entry: an NPC in sight kept its state until it ended by itself, which the routine states (sitting, standing guard, small talk) never do. NPCs only switched while the player was away, so walking away and coming back made them jump.

## Decisions

1. **When the time of its routine entry is over, the NPC's current state ends at its next loop, like in the engine.** Its end runs first (standing up from the bench), then the next entry starts. Not chosen: ending it at once in the minute the clock changes, which would cut off what the NPC is doing in the middle.
2. **A fight, a dialog and lying knocked out aren't interrupted.** The state ends once the NPC is free again. A fight here means a drawn weapon against a living target, because our NPCs keep their last target after a fight.
3. **"Continue your routine" from the scripts goes the same way:** the current state ends with its end, then the routine starts again. Before, the routine was started directly and kept the old state's leftovers.
4. **An NPC whose routine is taken away (monsters) runs its start state again.**

## Consequences

- Diggers go to bed, merchants close their stalls and guards change shifts while the player watches.
- A new routine from a script (a quest) still starts when the NPC's current state ends, mostly at the end of the dialog that gave it, like in the engine.
