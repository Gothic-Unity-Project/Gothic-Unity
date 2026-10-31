# ADR: Game profiles - pick the game or mod at start

- Status: proposed
- Date: 2026-10-31
- Technical details: Docs/architecture.md (configuration); GameSettings.json, DeveloperConfig "Game Profiles"

## Context

Testing Gothic 1, Gothic 2 and mods meant editing the configuration and restarting every time, and a build could only start what it was built with. The original games start one game per installation; mods bring their own ini files, sometimes several (difficulty levels).

## Decisions

1. **A list of profiles to pick from at start.** Each profile is a game (Gothic 1 or 2) with an optional mod folder and ini. Builds read it from GameSettings.json, the editor from the developer config. Only profiles whose folders exist are shown. Not chosen: always asking for the game - most players have one installation and want to start straight away, so the list is switched off by default.
2. **A mod with several inis can be asked for its ini.** "*" lists the mod's game inis, a list like "A.INI;B.INI" only these ones.
3. **Which source wins.** A picked profile wins over everything. Without one, the editor uses the developer config (preselected game, mod), a build uses GameSettings.json (game, mod path and ini). Not chosen: GameSettings.json in the editor too - developers switch in the developer config.
4. **A build can start a game or mod directly** (GameSettings.json game, mod path and ini), without the list.

## Consequences

- One build tests G1, G2 and mods; switching needs a restart (a restart entry in the VR menu comes later).
- New GameSettings.json keys; old files without them keep working.
