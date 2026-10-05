# 11. Mods and robustness

[← Overview](README.md)

## What changed

- **Mod loading:** a mod folder is a game root (`ModPath`, `ModIni` in `GameSettings.json`); mod `OU.BIN`/`CSL`
  for subtitles; per-ini VDF archive filtering; JSON-driven mod switching for standalone builds.
- **Daedalus robustness:** mods with extended classes no longer kill the boot; Ikarus/LeGo Trialoge functions
  stubbed safely; perception VM calls guarded; language detection survives modded `MOBNAME_CRATE`.
- **Content robustness:** null guards for resource lookups, menus that rename/omit vanilla assets, missing PFX
  and unparseable worlds during precaching, broken `Humans.mds`, missing morph mesh vertex mappings, VOB bounds
  cache and texture arrays.
- **Audio for mods:** Ogg Vorbis dubbing, dmusic crash escape hatch, mid-game `Wld_InsertNpc` spawns.
- Hero init race guards (`GlobalHero` null during init).

Status: G1 **Mroczne Tajemnice** is playable. G2 Union mods boot (Renovation); some (Dolina Zombie, Świat
Skazańców) are blocked by native ZenKit gaps or LeGo hooks — out of scope for this branch.

## Key commits

`5eb534aa`/`fca4712c` mod loading · `75287b0e` OU.BIN · `803816ee` extended classes · `1107c651` language ·
`77d74e77`/`4b801c12`/`50c3a989`/`0e6359ef` robustness · `a39b128c` Ikarus/LeGo · `33c5f200` JSON switching ·
`bc632361` Ogg · `54f97e9b` Wld_InsertNpc

## Main files

`Services/Config/ConfigService.cs` · `Services/Caches/ResourceCacheService.cs` · `Domain/Audio/SoundDomain.cs` ·
menu adapters · `Assets/StreamingAssets/GameSettings.json`

## Config flags

`EnableMod`, `ModPath`, `FallbackLanguage`, `EnableMusic`, `EnableOggAudio`; `GameSettings.json` (builds): `GameVersion`,
`ModPath`, `ModIni`, `EnableMusic`, `EnableZSpyLogs`, `EnableOggAudio` — one build runs G1, G2 and their mods
(see [How to test](14-how-to-test.md)).

## Review notes

- ✅ Null guards are targeted at real mod failures (each commit names the mod/case), not blanket try/catch.
- ⚠️ Mod-specific behavior is partly hardcoded per mod name (a deliberate choice: the tested mod list is small).
  Document it if it grows.
- ⚠️ `GameSettings.json` gained new keys; check the release/packaging scripts copy the new defaults.

## How to test

- Point `ModPath` at Mroczne Tajemnice; new game, talk, sleep, fight.
- Vanilla G1 and G2 still boot with `ModPath` empty.
