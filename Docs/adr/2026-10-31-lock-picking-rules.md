# ADR: Lock picking by Gothic's rules

- Status: proposed
- Date: 2026-10-31

## Context

A wrong turn of the lock pick broke it on a coin flip, a placeholder, and the game's own lock picking script never ran: no messages and no chance that a breaking pick is heard. Gothic 2 lets only a hero with the lock picking talent pick locks.

## Decisions

1. **In Gothic 2 only a hero with the talent picks locks;** without it the game's refusal plays, once per try and not at every touch of the lock. In Gothic 1 everybody can.
2. **The pick breaks by the engine's chance:** in Gothic 2 the hero's dexterity in a hundred holds, in Gothic 1 the talent's value is the chance to break. Gothic 1's own scripts set it to 90 when a person is created, and each level of training takes 40 off, to 50 and then 10. The roll, the status menu and the scripts all read that one value as it is. Not chosen: treating a value of 0 or less as a fresh 100 when rolling, which the status menu and the scripts didn't know about.
3. **Every turn runs the game's lock picking script,** which plays the sounds, shows the messages and in Gothic 2 may let a breaking pick be heard. Scripts without it (some mods) still get the sounds.
4. **Opening with the key stays as it was.**

## Consequences

- An untrained Gothic 1 hero breaks a pick with nine wrong turns in ten, as in the original, so lock picks run out fast until he learns the talent.
- A hero whose scripts never set the value (a mod without Gothic 1's person template) never breaks a pick, as in OpenGothic.
