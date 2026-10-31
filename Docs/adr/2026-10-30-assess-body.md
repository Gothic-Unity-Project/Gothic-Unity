# ADR: NPCs notice bodies

- Status: proposed
- Date: 2026-10-30

## Context

Dead NPCs and monsters lay around unnoticed. In the original game an NPC whose scripts ask for it notices the nearest body around him, and the scripts decide what happens: monsters walk over and eat the corpse, humans go and look at the dead (in Gothic 2 they also comment on it).

## Decisions

1. **NPCs notice bodies like the engine does.** An NPC notices the nearest body he can see: within his senses' range and the range the scripts give this perception, and only with a free line of sight, so a body behind a wall stays unnoticed.
2. **The check runs on every perception tick, like the engine.** As long as a body is near, the scripts hear about it again on every tick, and they themselves check whether the NPC is already busy with it. Not chosen: telling the scripts only once per body, which the original doesn't do.
3. **Only NPCs whose scripts ask for it look for bodies.** Everyone else pays nothing for it.
4. **What the NPC does with the body is up to the scripts.** Monsters eating corpses and humans looking at the dead are the scripts' own reactions, not ours.

## Consequences

- Wolves, scavengers and other monsters eat the corpses near them; humans look at the dead, and in Gothic 2 comment on them.
- Every NPC that looks for bodies goes through all NPCs of the world on each tick, next to the same search for enemies. Fine for now, to be measured in crowded places like the Old Camp; one shared search for enemies and bodies is a possible follow-up.
