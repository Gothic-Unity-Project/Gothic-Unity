# ADR: NPCs climb ledges like in Gothic

- Status: proposed
- Date: 2026-10-31
- Builds on: the VR ledge climbing record (2026-10-28-vr-ledge-climbing.md), whose ledge rules it reuses

## Context

NPCs and monsters walking or chasing stopped at every crate, rock and low wall: they slid along it or stood still. In the original game an NPC that runs head-on into a wall checks whether it can climb it, with the same rules as the hero's jump, and pulls itself up if the ledge is low enough. BoroBongo's experimental branch climbs ledges with its runtime navigation mesh; this does it the engine's way on our own movement.

## Decisions

1. **An NPC climbs only when it walks head-on into a wall,** like the engine. Sliding along a wall at an angle stays as it is.
2. **The same ledge rules as the hero's jump decide,** with the NPC's own guild values: below the low jump height it takes a big step up, below the mid jump height it pulls itself up. Higher walls, which need a jump, a hang and a pull-up, are not climbed by NPCs.
3. **The model's own climb animations play,** and the NPC rises to the top while they play and stands on top at their end. Models without them, like most monsters for the pull-up, don't climb.
4. **A wall that can't be climbed is checked again only after a second,** so an NPC standing at a wall doesn't test it every frame.

## Consequences

- Chasing NPCs follow the hero over low walls and crates, humans and orcs also over chest-high ones.
- Climbing runs on top of the hero's ledge rules, so this branch depends on the VR ledge climbing one.
- Still open: NPCs jumping down after the hero, the jump with hang for high walls, ladders on the way network.
