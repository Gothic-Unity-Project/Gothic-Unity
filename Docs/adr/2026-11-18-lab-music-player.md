# ADR: Lab music player for Gothic 1 and Gothic 2

- Date: 2026-11-18
- Technical details: Docs/music.md

## Context

To listen to and record Gothic's music themes and how they change into each other, a developer needs a simple player. The Lab could already play one theme of the running game. In the original games a theme change waits for the next beat or bar and may play a short fill or ending piece, as the music scripts say for each theme.

## Decisions

1. **The Lab plays the themes of both games, Gothic 1 and Gothic 2, from one list.** The second game is read from its installation path in the game settings. Not chosen: restarting the Lab with the other game, which makes comparing and mixing the two slow.
2. **Themes are switched by hand: pick a theme, press Play.** Not chosen: a timed list of changes in a file, more than needed for now.
3. **A change uses the transition the music scripts give the theme, like the game.** Not chosen: choosing the transition in the Lab.
4. **A change from a Gothic 1 theme to a Gothic 2 theme also waits for the beat or bar.** Untested so far; if it fails, such a change starts the music fresh, a hard cut. Not chosen: always a hard cut between the games.
5. **Favorites are picked in the Lab: a theme from the list goes into a second list of buttons, one click plays it.** The list lives only while the Lab runs and is not saved. Not chosen: a developer setting that shortens the list, which means editing a config before every session.
6. **The Lab doesn't record.** The music is recorded with an outside tool. Not chosen: a record button, more code for little gain.

## Consequences

- The Lab loads the other game's archives once at start, a short extra wait.
- In-game music is unchanged.
