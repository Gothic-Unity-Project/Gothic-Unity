# ADR: Game profiles - pick the game or mod at start

- Status: proposed
- Date: 2026-10-31
- Technical details: Docs/architecture.md (configuration); GameSettings.json, DeveloperConfig "Context" and "Mod"

## Context

Testing Gothic 1, Gothic 2 and mods meant editing the configuration and restarting every time, and a build could only start what it was built with. The original games start one game per installation; GothicStarter lists the game inis in the installation's system folder - the game's own one plus one per installed mod (some mods bring several, e.g. difficulty levels).

## Decisions

1. **The installations decide, no new settings.** At start we look at the Gothic 1 and Gothic 2 folders already set in GameSettings.json and list each game ini of their system folders, like GothicStarter does. Not chosen: a hand-written list of profiles in GameSettings.json - players would have to maintain paths and ini names that are already on disk.
2. **The order of checks.** First the developer config: a preselected game (with or without its mod) starts directly. Otherwise both installations are scanned. Exactly one game with one game ini starts directly, more show a list to pick from, none shows the installation error.
3. **Mods in their own folder stay a developer setting.** The developer config's mod path keeps working for the editor; players install mods into the game folder, where the scan finds them.

## Consequences

- One build tests G1, G2 and every mod installed into them; switching needs a restart (a restart entry in the VR menu comes later).
- A player with one game and no mod sees no list and starts straight away, as before.
- GameSettings.json is unchanged; the profile settings of earlier versions of this branch are gone.
