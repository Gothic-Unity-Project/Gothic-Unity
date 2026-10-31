# ADR: Lock picking by Gothic's rules

- Status: proposed
- Date: 2026-10-31

## Context

A wrong turn of the lock pick broke it on a coin flip, a placeholder, and the game's own lock picking script never ran: no messages and no chance that a breaking pick is heard. Gothic 2 lets only a hero with the lock picking talent pick locks.

## Decisions

1. **In Gothic 2 only a hero with the talent picks locks;** without it the game's refusal plays. In Gothic 1 everybody can.
2. **The pick breaks by the engine's chance:** in Gothic 2 the hero's dexterity in a hundred holds, in Gothic 1 the talent's value is the chance to break. That chance starts at a hundred and training lowers it, to 60 and then 20.
3. **Every turn runs the game's lock picking script,** which plays the sounds, shows the messages and in Gothic 2 may let a breaking pick be heard.
4. **Opening with the key stays as it was.**

## Consequences

- An untrained Gothic 1 hero breaks a pick with every wrong turn, as in the original, so lock picks run out fast until he learns the talent.
