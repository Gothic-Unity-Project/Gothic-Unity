# ADR: Hit sounds by material

- Status: proposed
- Date: 2026-10-31

## Context

A hit only made the victim cry out and bleed. In Gothic every hit also sounds like what hits on what is hit, a sword on metal armor or a wolf's bite into flesh, with sparks on metal.

## Decisions

1. **A hit plays the engine's collision sound of weapon and armor material,** like the reference engine: a weapon's material on metal, wood or flesh; a monster's jaws or a fist on the same.
2. **Only metal and wooden armor sound different,** as the game ships sounds only for those; any other armor counts as flesh.
3. **The sparks are the game's particle effect of the same name,** else the one of the armor's material, and nothing if the game has none.
