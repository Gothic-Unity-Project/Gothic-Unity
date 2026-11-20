# ADR: Achievements

- Date: 2026-11-20
- Technical details: Docs/achievements.md

## Context

We want achievements. Gothic-Unity isn't released on Steam or any other store, so there is no platform to keep them: they have to live on the player's machine.

The original PC games have none. The console ports (Gothic Classic, Gothic II Complete Classic) have lists of 51 and 55 achievements; the wiki page "Achievements" collects them for reference. Most of the Classic ones match a fact the original scripts already track: a quest log entry, a chapter, a guild, a dialog that was held, an item. A few need the engine to count something (kills, picked locks, stolen pockets).

## Decisions

1. **Our own curated list, for Gothic 1 and Gothic 2, with our own names and texts.** The moments are inspired by the Classic lists, because those were made for exactly the content we run, but every title and description is our own wording, in English and German. We never call them "official" achievements and use none of the official texts or icons; a quest achievement may show the quest's title from the player's own installation. Not chosen: copying an official list 1:1 - its titles, texts and icons are THQ Nordic's work, and calling them official would suggest an endorsement we don't have.
2. **Achievements belong to the player, not to a save game.** One local file per game (and per mod) keeps them, independent of saves, like a store would. Loading an old save keeps every achievement. Not chosen: storing them in the save, which would give no lasting record.
3. **Three tiers, named after the gods: Innos, Adanos, Beliar.** Innos: no cheats and the playthrough never went back to an earlier save (continuing from the newest save is fine; reloading after a death is not). Adanos: normal play with saving and loading. Beliar: cheats (Marvin mode) were used. A save remembers the tier it was written in, so loading a clean save makes the game clean again, and an achievement earned in a better tier later is upgraded. This follows RetroAchievements' hardcore and casual modes. Not chosen: blocking achievements while cheating, which punishes testers and curious players; ignoring cheats, which makes the list meaningless.
4. **Icons come from the player's installation.** Each achievement names an item or a texture of the game, loaded at runtime like everything else. Not chosen: the official icons, which are THQ Nordic's and can't ship with the project; our own artwork, which nobody has drawn yet.
5. **With a mod, the base list is off.** A mod can change the story so that a Gothic condition fires wrongly or never. A mod (or the community) may bring its own list in the same format, kept in its own file. Not chosen: keeping the base list and switching off only the entries whose script names are missing - a mod can keep the names and change their meaning.
6. **The list is a readable data file per game, texts in all our languages inside it.** The community can curate it without a programmer, and mods can copy the format. Not chosen: one C# class per achievement, which only programmers can change and mods can't extend.
7. **Tamper-evident, not tamper-proof.** The achievement file and the achievement data in each save carry a seal that only official builds can make, tied to the player's device. Editing a file or copying it from someone else breaks the seal: the achievements stay visible but count as unsealed (greyed out, broken seal) until they are earned again. Nothing is ever deleted. Builds made from the source code have their own seal and only trust their own files. Not chosen: encryption or hidden code - the project is open source, so it would only look secure and would make bug reports harder to read; no protection at all, which lets anyone mark everything as earned with a text editor.

## Consequences

- The game announces a few new facts (quest log status, guild change, dialog told, who killed whom, items the hero got) that other features can use too.
- Achievements that need the engine to count something it doesn't count yet (pickpocketing, hiding in a barrel, forging) are listed but stay locked until that fact exists.
- Cheats now leave a mark in save games.
- Someone who builds the game from source with their own seal can still fake achievements for that build; this is accepted.
- A new device (or a reset headset) shows existing achievements as unsealed until they are earned again.
- Still open: achievements that span several playthroughs, unlocking what an old save already achieved, and the final curated list.
