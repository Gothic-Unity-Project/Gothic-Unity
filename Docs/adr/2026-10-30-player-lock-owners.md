# ADR: Every lock on the player has an owner

- Status: proposed
- Date: 2026-10-30

## Context

Several parts of the game stop the player from moving: loading a world, a dialog, sitting on a bench, a video. Each of them locked and unlocked the controls on its own. Whoever unlocked last won: a dialog that ended while the player sat on a bench let the player walk away from the seat, and leaving the water switched teleporting back on during a lock. The video player worked around it by saving and restoring the controls itself.

In the original game this can't happen, because the hero's state decides what he can do.

## Decisions

1. **Each lock names its owner, and the player is free once every owner has unlocked.** Ending a dialog only removes the dialog's lock, a bench keeps its own. Not chosen: a counter of locks, because a forgotten or doubled unlock would shift it for good and nobody could tell which part holds the player.
2. **A lock says which controls it blocks.** Most block moving, turning and teleporting; a lock can leave turning free, which the pinned dialogs need. Not chosen: one fixed kind of lock, because then each new kind would need its own way around it again.
3. **Water decides about teleporting separately from the locks.** Teleporting is off in water whatever the locks say, and comes back on land only if no lock blocks it.
4. **A new world starts free.** Loading clears all locks except the one of a video that still plays, as before.

## Consequences

- A dialog on a bench, a video in a dialog or a dialog in water no longer free the player by accident.
- The log shows which owners hold the player, which helps when someone gets stuck.
- Knocked out heroes (the NPC melee change) and pinned dialogs become owners too once those changes are merged.
