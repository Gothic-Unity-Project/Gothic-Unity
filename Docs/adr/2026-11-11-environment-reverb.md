# ADR: Echo in caves, mines and temples

- Date: 2026-11-11
- Technical details: Docs/architecture.md

## Context

Gothic gives caves, mines and temples an echo: the world stores reverb zones with a sound environment each. Our asset loader (ZenKit) doesn't read those zones, so every sound was dry everywhere.

## Decisions

1. **The echo is measured around the player instead of read from the world.** Every half second: is there a ceiling of the world above the player (not a tree or a roof on poles)? Then a cave's echo, or a stone corridor's when the walls are near. Outside, no echo. Not chosen: waiting for ZenKit to read the zones (unknown when), or hand-made zones per world (doesn't work for mods).
2. **No setting.** It is how the game sounds.

## Consequences

- Houses with a roof of the world mesh echo a little too, the original only did that where the world builder placed a zone.
- Nine short ray checks twice a second, also on the Quest.
- Once ZenKit reads the reverb zones, the measured echo can be replaced by them.
