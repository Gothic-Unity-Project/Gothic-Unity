# ADR: VR magic - no two-hand charge

- Date: 2026-11-14
- Replaces: decision 4 of [VR magic - runes held in both hands, thrown spells, transformations](2026-11-03-vr-magic.md)
- Technical details: Docs/vr-magic.md

## Context

Charged spells (fireball, fire rain, wind fist, ...) grew only while both hands were together, like a kamehameha. The Immersion option "Two-hand charge" could switch it off, so one hand was enough.

## Decisions

1. **One hand is enough to charge a spell.** The hand that holds the rune charges it with its trigger, as for every other spell. The option was a setting few needed, and the menu and the spell code are simpler without it. Not chosen: keeping the option, switched off by default.

## Consequences

- The Immersion menu has no "Two-hand charge" row any more, and Gothic.ini's `twoHandSpellCharge` is ignored.
