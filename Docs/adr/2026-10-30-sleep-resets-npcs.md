# ADR: Sleeping puts the NPCs back on their daily routine

- Status: proposed
- Date: 2026-10-30

## Context

When the hero sleeps, Gothic jumps the clock forward. The original game then puts every living NPC where its daily routine wants it at the new time, and sleeping past midnight starts a new day. Our game only changed the clock: NPCs stood where they were, kept chasing the hero with their weapons drawn, and the day counter never moved, so scripts that wait for the next day waited forever.

## Decisions

1. **A clock set to an earlier time of day means tomorrow**, like in Gothic. Sleeping from evening until morning starts the next day.
2. **After the clock jumps, every living NPC starts its routine over at the routine's place**, like in Gothic (found on BoroBongo's branch). It forgets its fight and puts its weapon away; monsters go back to their usual behaviour. NPCs out of sight get this when they come into sight. The dead and the knocked out stay where they are. NPCs are placed one per frame so they don't land on top of each other.

## Consequences

- Waking up, the world is as Gothic shows it at that time: guards at their posts, workers at work, nobody still hunting the hero.
- The day counter moves on with sleep, so day-based quests and events can happen.
- What an NPC was doing when the hero went to bed is lost, as in the original.
