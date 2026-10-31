# ADR: Sound effects come from the sound effects folder first

- Status: proposed
- Date: 2026-10-31

## Context

The game's sounds sit in two folders of its archives: sound effects in one, speech (dialog lines and the NPCs' shouts) in another. Some files exist in both under the same name. In Gothic 1 the hero's death scream and pain sounds are such files: the sound effect and a spoken line share the name. We found every sound by its file name alone, so whichever archive was newer won, and the hero screamed with the speech file instead of the sound effect. The original game looks for a sound effect in the sound effects folder.

## Decisions

1. **Every sound is looked for in the sound effects folder first, then by its name anywhere.** This is the default, so a new sound (footsteps, a new menu sound) gets it without thinking about it. A file that isn't in that folder is still found by its name, so nothing that played before goes silent. Not chosen: asking for the sound effects folder only where we knew of a clash, which left most sound effects (menu clicks, rain, drawing a weapon, the backpack) on the old lookup and every new one too.
2. **Speech asks for itself explicitly and is found by its name only.** Dialog lines and NPC shouts never pick a sound effect of the same name.

## Consequences

- The hero's death scream and pain sounds in Gothic 1 are the sound effects again.
- A sound used both as a sound effect and as speech is kept twice in memory. Few files are.
- Still open: whether speech should look in the speech folder first, so a mod's sound effect of the same name can't win the name lookup. Not needed for the original games.
