# Music

Gothic's music is DirectMusic (`.sgt` segments), played with dmusic. The music scripts (`MUSIC.DAT`, class
`C_MUSICTHEME`) name each theme's file and how a change into it sounds: `transType` (fill, break, end, ...) and
`transSubType` (now, next beat, next bar). `MusicDomain` turns them into dmusic's embellishment and timing.
Decision record: [Lab music player](adr/2026-11-18-lab-music-player.md).

## Lab music player

To listen to the themes of both games and how they change into each other, for example to record them with an
outside tool (OBS, Audacity).

- Open the Lab scene and play it. The music dropdown lists the themes of Gothic 1 and Gothic 2, named
  `G1 · OC_DAY_STD`, `G2 · NW_DAY_STD` and so on. Pick one and press Play.
- A change uses the theme's own transition from the scripts, like the game: it waits for the next beat or bar and
  may play a short fill or ending first.
- Both games are read from their installation paths in `GameSettings.json` (`Gothic1Path`, `Gothic2Path`). The
  running game is `DeveloperConfig.GameVersion`; if the other game isn't installed, only the running game's themes
  are listed (warning in the log, category `Audio`).
- Favorite adds the selected theme to the favorites list below Play and Favorite (each theme once). A click on a
  favorite plays it like Play does, so changes between a handful of themes are one click each. The list lives only
  as long as the Lab scene; it isn't saved.
- Each change is logged: `[Music] Lab: G1 · OC_DAY_STD -> G2 · NW_DAY_STD (switch between the games)`.

### Changes between the games

A change from a Gothic 1 theme to a Gothic 2 theme (or back) plays on the same DirectMusic performance and waits for
the beat or bar like any other change. Each game's segments come from that game's own files, so it is untested how
well the two games' instruments and grooves mix. If such a change crashes or sounds broken, the fallback is a fresh
performance on each game change (a hard cut).

### The other game's data

The running game's files come from the injected `ResourceCacheService`. For the other game the Lab creates a second
`ResourceCacheService` (not registered in Reflex) and mounts that game's VDFs and `_work` folder once at Lab start,
which takes about a second. It is used for music only, loaded vanilla (no `.mod` archives), and lives as long as the
Lab scene, together with each game's `MUSIC.DAT` VM that owns the theme instances.
