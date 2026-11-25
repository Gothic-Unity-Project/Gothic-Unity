# ADR: A routine change ends the routine's state

- Date: 2026-11-25
- Technical details: Docs/npc-ai.md (The loop, point 4)

## Context

NPCs follow a daily routine: from 07:10 to 07:20 Harad works at the anvil, then at the fire, then at the anvil again. Our NPCs only took the next entry of their routine when their current activity ended by itself. Working at an anvil, sitting on a bench or sleeping never ends by itself, so once Harad stood at the anvil he stayed there for good, and sleepers never got up in the morning while the player watched.

In the original game each routine entry has an end time. When the time passes it, the activity the routine started is ended properly (the smith steps back from the anvil, the sleeper gets up) and the next entry begins. Reactions the NPC is busy with, like talking to the player, fighting or watching him, are not cut off. The reference engine does the same.

## Decisions

1. **When a visible NPC's routine entry changes, the activity of its routine ends properly and the next one starts.** The NPC finishes what it is doing at that moment, plays the ending (standing up, stepping back) and then walks to its next spot. Not chosen: teleporting the NPC to its new spot, as we do for NPCs out of sight, because the player would see it.
2. **Only activities started by the routine end, never reactions.** Talking, fighting, fleeing, watching the player and the dialog partner are left alone; the NPC takes its new routine entry when they are over. An activity the routine started itself counts as part of the routine: in Gothic 2, going to bed leads to sleeping, and the sleeper gets up in the morning. Not chosen: the reference engine's rule that every reaction started during a routine activity also ends with it (unless the NPC talks or has a target), because it can cut off reactions like watching the player.
3. **A new routine set by the story (a quest changes where an NPC lives) ends the current routine activity right away.** Like a change by time. An NPC in a dialog changes after the dialog. Not chosen: waiting for the old entry's end time like the reference engine, because an NPC sitting on a bench would ignore its new task for hours.
4. **Two entries in a row with the same activity at the same spot don't interrupt.** Bilgot sits at the campfire from 08:00 to 23:00 and from 23:00 to 08:00; he doesn't stand up and sit down again at 23:00. Not chosen: restarting the activity like the reference engine, because it shows as a pointless stand up and sit down.
5. **Ending to use an object that the NPC isn't using does nothing.** If the routine changes while the smith is still walking to the anvil, he doesn't walk to an anvil just to step back from it. The reference engine does the same.
6. **After a time jump (sleeping, Marvin's "set time") this rule stays out of the way.** The routines are recalculated for the new time, but the NPCs near the hero start over at their new spots anyway ([After a time jump every NPC starts over](2026-11-25-time-skip-npc-reset.md)). Not chosen: both at once - an NPC would begin to get up or walk off, and a few frames later the start-over cuts that off and puts it on its spot.

## Consequences

- Smiths, cooks, sleepers, bench sitters and guards with several entries follow their schedule while the player watches.
- NPCs out of sight behave as before: they appear at their new spot when they come into view.
- A routine change while the NPC is in the middle of switching to another activity is skipped; that activity then runs until it ends by itself or the next routine change.
- Saved games: an activity the routine started (now also sleeping) isn't continued after loading, the NPC starts its routine entry again.
