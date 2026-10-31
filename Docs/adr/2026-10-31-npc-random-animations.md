# ADR: Random animations within a state animation

- Status: proposed
- Date: 2026-10-31

## Context

The scripts give some looping state animations random animations: a knocked out human tries to get up now and then, a monster looks up while eating, the bathing women in Gothic 2 splash. The original engine plays one of them on average every few seconds, set by the scripts. The reference engine leaves them out, our project ignored both script calls.

## Decisions

1. **Each NPC remembers its random animations per state animation,** with the frequency the scripts set (5 seconds if they don't).
2. **One check four times a second for all NPCs that have random animations,** not per NPC every frame. While the state animation loops and none of its random animations plays, one is picked after the frequency, varied by half up or down. It leads back into the state animation by itself.
3. **NPCs out of sight are skipped.**
