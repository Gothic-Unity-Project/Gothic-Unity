# ADR: Achievements

- Date: 2026-11-20
- Technical details: Docs/achievements.md

## Context

We want achievements. Gothic-Unity isn't released on Steam or any other store, so there is no platform to keep them: they have to live on the player's machine.

The original PC games have none. The console ports (Gothic Classic, Gothic II Complete Classic) have lists of 51 and 55 achievements (counted on the wiki page "Achievements", which collects them for reference). Most of the Classic ones match a fact the original scripts already track: a quest log entry, a chapter, a guild, a dialog that was held, an item. A few need the engine to count something (kills, picked locks, stolen pockets).

## Decisions

1. **Our own curated list, for Gothic 1 and Gothic 2, with our own names and texts.** The moments are inspired by the Classic lists, because those were made for exactly the content we run, but every title and description is our own wording, in English and German. We never call them "official" achievements and use none of the official texts or icons; a quest achievement may show the quest's title from the player's own installation. Not chosen: copying an official list 1:1 - its titles, texts and icons are THQ Nordic's work, and calling them official would suggest an endorsement we don't have.
2. **Achievements belong to the player, not to a save game.** One local file per game (and per mod) keeps the list of unlocked achievements, independent of saves, like a store would. Loading an old save keeps every achievement. Only the tier of a playthrough and its counters travel with its saves (decision 3). Not chosen: storing the unlocks in the save, which would give no lasting record.
3. **Two tiers for now, named after the gods: Adanos and Beliar.** Adanos: normal play with saving and loading. Beliar: cheats (Marvin mode) were used in this playthrough. A save remembers the tier it was written in, so loading a clean save makes the game clean again, and an achievement earned in a better tier later is upgraded. This follows RetroAchievements' casual and cheated play. A third tier, Innos, is kept free for a later permadeath mode (one life, the game saves itself): it needs no save game bookkeeping of its own. Not chosen: blocking achievements while cheating, which punishes testers and curious players; ignoring cheats, which makes the list meaningless; an Innos tier for "never went back to an older save" - it needed run ids, save numbers and death tracking in every save and profile, for a rule a permadeath mode states more simply.
4. **Icons come from the player's installation.** Each achievement names an item of the game, drawn like the item itself, or a texture of the game (a map, a loading screen, a creature); both are loaded at runtime like everything else. An item the scripts don't have falls back to the texture or a plain placeholder. Not chosen: the official icons, which are THQ Nordic's and can't ship with the project; our own artwork, which nobody has drawn yet.
5. **With a mod, the base list is off.** A mod can change the story so that a Gothic condition fires wrongly or never. A mod (or the community) may bring its own list in the same format, kept in its own file. Not chosen: keeping the base list and switching off only the entries whose script names are missing - a mod can keep the names and change their meaning.
6. **The list is a readable data file per game, texts in all our languages inside it.** The community can curate it without a programmer, and mods can copy the format. Not chosen: one C# class per achievement, which only programmers can change and mods can't extend.
7. **No protection against editing.** The achievement file is plain, readable JSON on the player's device, and so is the achievement data in a save. Anyone can edit them; this is accepted. Not chosen: a seal (HMAC with a build key and a device id) - the project is open source and has no store or leaderboard to protect, so it cost code, CI secrets and support ("why are my achievements unsealed?") for little gain; encryption or hidden code, which would only look secure and make bug reports harder to read.
8. **Only what you do while playing counts.** Nothing that happens while a save or a world is loading unlocks anything, and loading an old save unlocks nothing by itself: an achievement is earned when it happens in this playthrough. Not chosen: checking a save's state when it is loaded - it would hand out achievements not earned in this run, and could turn a cheated save into an Adanos unlock.
9. **Achievements can be switched off.** A game setting, on by default. Off: nothing is recorded and no pop-ups appear. Not chosen: only hiding the pop-ups, which still records in the background; no switch at all, which forces achievements on players who don't want them.

## Consequences

- The game announces a few new facts (quest log status, guild change, dialog told, who killed whom, items the hero got) that other features can use too.
- Achievements that need the engine to count something it doesn't count yet (pickpocketing, hiding in a barrel, forging) are listed but stay locked until that fact exists.
- Cheats now leave a mark in save games (the tier in the achievement section).
- Anyone can mark achievements as earned with a text editor; this is accepted.
- Players who already played far before achievements existed have to reach those moments again in a new playthrough.
- Still open: the Innos tier as a permadeath mode, achievements that span several playthroughs, and the final curated list.
