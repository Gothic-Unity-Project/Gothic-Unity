# ADR: The music follows the time of day and the danger

- Status: proposed
- Date: 2026-10-30

## Context

Gothic's music has themes per area for day and night, and for calm, threat and fight. We only played the area's calm day theme. A first attempt switched to the fight theme when an NPC chose the hero as its target, but never switched back, and night themes never played.

## Decisions

1. **The engine's rule decides, checked every two seconds.** Night themes play from 21:00 to 4:00. While an NPC attacks the hero with a weapon drawn, the fight theme plays, or the threat theme as long as the hero has no weapon drawn himself. When the fight ends, the calm theme comes back. The original checks every five seconds; two react faster to a fight and cost next to nothing.
2. **Missing themes fall back like in the engine.** Night falls back to day, threat to fight, both to calm, and an area without a fitting theme to the world's default music.
3. **A dead hero keeps the danger music.** Like in the engine, until a game is loaded.
4. **A new world starts calm.**
5. **The area is picked by position, like the engine.** The area playing stays while the player is inside it, else the last area containing him in the world's order plays, else the default music. Picking by priority played the big area around Gothic 2's pirate camp instead of the camp's own music. (Found by BoroBongo.)
6. **Each world's music starts on a fresh music engine.** The DirectMusic library crashed the game natively when world themes followed the menu music on the same engine, for example when leaving Gothic 2's pirate camp. (Found and fixed by BoroBongo.)

## Consequences

- Fights and nights sound like in the original.
- The check walks over all NPCs of the world every two seconds, a few hundred at most, which is cheap.
