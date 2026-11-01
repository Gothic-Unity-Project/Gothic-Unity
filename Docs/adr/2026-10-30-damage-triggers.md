# ADR: Arrows and spells set off triggers that react to damage

- Status: proposed
- Date: 2026-10-30

## Context

Some of Gothic's triggers react to being hit instead of being touched. On Irdorath, the island of Gothic 2's last chapter, the big bridge only comes down when two switches across a chasm are shot; they are out of the hero's reach. Our triggers only reacted to the hero walking into them and to other triggers, so the bridge stayed up.

## Decisions

1. **A projectile flying through a trigger zone counts as damage.** Arrows, bolts and spells set off every trigger zone they pass that reacts to damage, once per flight, and fly on. A plain trigger fires its target, a script trigger calls its function. A moving part (mover) that reacts to damage moves when a projectile stops at it, like Gothic 1's switches in the Sleeper's temple. BoroBongo's branch found and does the same.
2. **Melee hits don't count yet.** The triggers that need damage in the original game are out of sword reach; a sword hit on a trigger zone can follow when a case needs it.

## Consequences

- The Irdorath big bridge can be opened with a bow or a spell, as in the original.
- The zones are invisible, so the player has to know where to shoot, like in Gothic.
