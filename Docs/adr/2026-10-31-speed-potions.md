# ADR: Speed potions

- Status: proposed
- Date: 2026-10-31

## Context

Gothic 1's three haste potions and Gothic 2's speed potion lay a sprint animation over the hero for a few minutes. The command behind them didn't exist, so the potions did nothing.

## Decisions

1. **A timed animation overlay works like the engine's:** it stays for its time, then the one before comes back. A newer overlay put on meanwhile stays.
2. **The VR hero has no animations, so he moves as much faster as the overlay's walk and run do,** measured from the animations themselves.
3. **A second potion starts the time anew** instead of adding up.

## Consequences

- The faster movement isn't saved: a game loaded during a potion's time starts at normal speed.
