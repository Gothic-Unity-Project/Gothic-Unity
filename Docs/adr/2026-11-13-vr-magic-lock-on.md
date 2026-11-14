# ADR: VR magic - locking a target for thrown spells

- Date: 2026-11-13
- Technical details: Docs/vr-magic.md

## Context

Thrown spells (fireball, firebolt, ice lance, ...) fly where the hand's swing sends them, and only a strong swing close to the right direction homes in (decision 3 of [VR magic](2026-11-03-vr-magic.md)). Far away, sitting, or with limited movement, hitting is mostly luck.
In Gothic the hero focuses an NPC and the spell flies to that target. VR thrown spells have no such focus.
In throw mode the hand holding the rune has a free trigger: only the other hand charges and throws.

## Decisions

1. **The rune hand points at an NPC and holds its trigger to lock it; the other hand throws as before.** It brings back Gothic's focus without taking the throw away. Not chosen: always homing on the nearest NPC, which removes aiming entirely.
2. **A spell released while locked always flies to the target.** It starts toward the target, without falling, and follows it. The swing still sets the speed, but even a gentle release arrives. Not chosen: needing a swing as well, which brings back the problem the lock solves.
3. **The lock stays while the trigger is held, even when the hand points elsewhere.** It ends when the target dies or is knocked out, gets out of the spell's reach, or is out of sight for about a second (seen the way Gothic's NPCs see: leaves and bushes don't block). A magically frozen target stays locked. Not chosen: keeping the hand on the target, which is tiring and shaky at a distance.
4. **The lock reaches as far as the spell flies.** A locked spell can always arrive.
5. **A locked spell turns toward its target as fast as other homing spells.** A target that moves quickly can still dodge.
6. **The locked NPC stays highlighted with its name, and the rune hand vibrates when the lock is made or lost.** It is the same highlight and name as pointing at an NPC, but brighter and with the name large enough to read at the spell's range, and it stays wherever the hands point until the lock ends.
7. **Always on, no option.** It only does something when the rune trigger is pressed. Not chosen: another Immersion option, which adds a setting without need.
8. **Only for thrown spells with a rune or scroll in the hand.** Favorites-ring spells have no rune hand, and the mouse and keyboard simulator already throws where you look.

## Consequences

- Decision 3 of [VR magic](2026-11-03-vr-magic.md) stays in force for throws without a lock.
- Works the same in Gothic 1, Gothic 2 and mods, because a spell counts as thrown from its own effect data.
- Ring spells have no lock. A free hand could lock for them later.
