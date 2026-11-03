# ADR: Damage zones

- Status: proposed
- Date: 2026-10-31

## Context

The worlds hold damage zones: boxes that hurt whoever stands in them, such as fire, spikes and the traps of the addon's temples. There are about 36 in Gothic 1's other worlds and about 50 in the addon. They were skipped when a world loaded, so every trap was harmless.

## Decisions

1. **A damage zone hurts the hero and NPCs inside it** right away and again after each of its repeat delays, like the reference engine. The damage counts once for each of its damage types, minus the victim's protection against that type. A type the victim is immune to does nothing.
2. **No knockout and no reaction:** at zero hit points the victim dies, like a fall.
3. **A zone without a repeat delay hurts ten times a second,** not every frame, so the damage doesn't depend on the frame rate.
4. **Zones are built when the world loads,** not when the hero comes near: they have nothing to see, and NPCs walk into them anywhere.
