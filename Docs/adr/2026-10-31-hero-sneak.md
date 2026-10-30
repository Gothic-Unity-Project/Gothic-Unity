# ADR: The VR hero walks, runs and sneaks for the NPCs

- Status: proposed
- Date: 2026-10-31

## Context

Gothic's scripts look at how the hero moves: a running hero gets away from a chase, a sneaking one is suspicious, and his footsteps can be heard. In Gothic the movement keys decide this, and sneaking needs the sneak talent. In VR the hero had no such state at all, so the scripts always saw him standing and nobody heard him.

## Decisions

1. **The hero's movement decides, like in Gothic.** Moving with the stick is Gothic's run, moving slowly (walking around in the room) is walking, not moving is standing. A teleport or a fall is none of them.
2. **Crouching is sneaking, and only with the sneak talent** (found on BoroBongo's branch). Crouching counts by the crouch button or by the player's own head going down. Without the talent the hero just walks crouched, as in Gothic.
3. **Every step but a sneaking one makes a quiet noise** the NPCs around can hear. Gothic 2's NPCs turn to look; Gothic 1's scripts only react to noises with an item, as in the original.

## Consequences

- Sneaking past guards works with the talent; NPCs notice a hero running or walking around them.
- The step length for the noise is an estimate (about a metre), as VR has no step animation.
- Sitting, lying and swimming keep their own states.
