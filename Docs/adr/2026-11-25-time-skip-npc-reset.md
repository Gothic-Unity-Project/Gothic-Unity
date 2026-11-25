# ADR: After a time jump every NPC starts over

- Date: 2026-11-25
- Technical details: NpcService.ResetNpcsAfterTimeSkip and AiHandler.ResetAfterTimeSkip (Core)

## Context

When the hero sleeps, the scripts move the clock forward (Wld_SetTime); Marvin's "set time" does the same. Only the clock moved: NPCs kept doing what they did before the jump. Sleepers slept on through the morning, guards kept chasing the hero after a night in bed. The original game ends the current activity of an NPC when its daily routine changes, so after the jump everyone is where the new time wants them. This behaviour existed before and was lost when an earlier branch was split into PRs.

## Decisions

1. **A time jump makes every living NPC start over at its daily routine for the new time, like an NPC that comes back into view.** NPCs near the hero start over one after another (so they don't appear inside each other), NPCs out of view when they come back. They forget a fight in progress and put their weapon away. Not chosen: only recalculating the routines - an NPC keeps its current activity until it ends by itself, which for a sleeper is the whole morning.
2. **Some NPCs are left out: dead and knocked out ones, the hero's followers, guides and summons, and the NPC the hero is talking to.** Starting over would bring the downed back to their feet, teleport the hero's company to a waypoint far away, and break an open dialog (Marvin can set the time while talking). A magically frozen NPC is not down and starts over.
3. **A time jump while a world is loading is ignored.** Gothic 2's world startup scripts set the time; every NPC starts fresh there anyway.
4. **Not chosen for now: ending the current activity whenever a routine changes, as the original game does.** That is the general rule (also for the hourly routine changes without a jump) and is done in its own PR (fix/routine-change-ends-state). The time jump reset stays as the quick, complete answer for sleeping.

## Consequences

- After sleeping, NPCs near the hero visibly jump to their routine spots for the new time, one per frame.
- An NPC that was out of view during the jump starts over when it comes back into view, even much later. It also forgets a fight it had at that moment.
- The pending resets are not saved: saving after a sleep and loading again lets NPCs out of view continue normally instead of starting over.
- Once routine changes end the current activity in general, this record may be replaced.
