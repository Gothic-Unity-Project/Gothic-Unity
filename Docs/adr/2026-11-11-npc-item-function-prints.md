# ADR: NPC item functions don't print on the player's screen

- Status: proposed
- Date: 2026-11-11
- Technical details: Docs/architecture.md

## Context

When an item is equipped, its script function runs for the one who wears it. Some mods write these functions for the hero only: Mroczne Tajemnice (a Gothic 1 mod) checks the hero's strength and armor in its helmet and armor functions and prints "too little strength" or "no helmet with this armor". The original engine rarely runs them for NPCs at a time the player sees (it creates NPCs before the hero and doesn't run them again when loading). We run them when NPCs put on their equipment, also after loading a save, so the player got a pile of messages.

## Decisions

1. **A text printed while an NPC's (not the hero's) item function runs is only logged, not shown.** The messages are about the hero and make no sense for an NPC. Not chosen: not running NPC item functions on load (they set NPC attributes and protection that scripts rely on), or a per-mod fix (every mod with hero-only item functions would need one).

## Consequences

- A mod that wants to tell the player something from an NPC's equip function isn't heard - none is known.
- The hidden texts are in the log (`[PrintScreen] Not shown - from <NPC>'s item function`).
