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

**Added 2026-10-11 – 2026-10-13**

- Mroczne Tajemnice: the missing smithy furnace (fireplace children, [World](07-world-vobs.md)) and stone textures on
  palms/nets (texture array materials, [Rendering](08-rendering-video.md)) are fixed.

**Added 2026-10-15**

- Mroczne Tajemnice: 1024x1024 single mip textures (Siekacz's tavern, catapult, roofs) and the abandoned mine's
  dynamic lights work ([Rendering](08-rendering-video.md)); the web crawler bites frozen/close targets
  ([Combat](01-combat.md)).
- **`ANCIENTTEMPLE.ZEN` loads** (the portal from the abandoned mine, the Guardians quest): ZenKit's
  `ReadArchiveBinary::read_header` reads the second `END` with `read_line_then_ignore("\n")`, which also eats the
  first payload byte when it's `0x0A` - the world mesh's material count 266 (`0A 01 00 00`) was read one byte off and
  `ZkWorld_load()` died with `std::bad_alloc`. A world ZenKit can't load is retried with that header as `END\0` (the
  `0xB020` chunk and the `MeshAndBsp` blob shrink by one byte) - `ZenWorldArchiveFixDomain`. Normal worlds don't pay
  anything (only after a failed load).
- **Subtitles from the archives:** ZenKit's `CutsceneLibrary` has no VFS support; when no extracted `OU.BIN`/`OU.CSL`
  is on disk, the mounted archives' copy (the mod's wins) is written to Unity's temporary cache and loaded from there -
  extracting `OU.BIN` from `DM_Speech.mod` with VDFS by hand isn't needed anymore.
  ⚠️ This is a workaround on our side - the bug should be raised and fixed in ZenKit too (it hits any binary archive
  whose payload starts with `0x0A`, e.g. meshes with 10/266/522 materials). Issue/PR draft with repro and the suggested
  one-line fix: [zenkit-issue-material-count.md](zenkit-issue-material-count.md). Once ZenKit is fixed, the fallback
  never triggers and `ZenWorldArchiveFixDomain` + `EnableZenMaterialCountFix` can be removed.
**Added 2026-10-18** (Gothic II Renovation, 2026 release)

- **Mesh chunk lengths** (`6cc8e641`): Renovation's `DRAGONISLAND.ZEN` stores its materials chunk 177 bytes short
  (the last material left out). ZenKit seeks back to the stored end after reading the materials, reads material data as
  the next chunk and never returns - PreCaching froze with Unity "not responding". Before ZenKit loads a mod's ASCII
  world, `ZenWorldArchiveFixDomain.TryFixMeshChunkLengths` walks the `MeshAndBsp` chunks; a chunk not followed by a
  known one gets the length up to where the chain continues. BIN_SAFE worlds (vanilla) aren't touched. Checked on
  Renovation's and Mroczne Tajemnice's worlds: only the broken one changes.
  ⚠️ To be raised in ZenKit as well: [zenkit-issue-chunk-length.md](zenkit-issue-chunk-length.md).
- **Item instances by symbol** (`77bb432a`): items looked up by symbol index went through their lowercased name, and
  ZenKit matches names case-insensitively only for ASCII - Renovation's `ITAR_ŃASTOFFS` (Alrik's rags) wasn't found,
  `Mdl_SetVisualBody` threw inside `Wld_InsertNpc` and the whole world's NPC creation stopped. They're initialized from
  the symbol now; an armor that isn't an item leaves the NPC in its body with a warning.

- **Items where scripts expect an NPC:** NPC/item parameters of the externals are taken as instances and checked - G1's
  `B_FetchWeapon` calls `Npc_GetDistToNpc(self, item)` (the engine measures between any two VOBs), the item failed
  ZenKitCS' cast, the stack stayed broken and the VM crashed natively (Mroczne Tajemnice's giant skeleton by Fellan's
  armor). Loading a game also resets the item/SVM/spell instances cached from the old VM.

## Key commits

`5eb534aa`/`fca4712c` mod loading · `75287b0e` OU.BIN · `803816ee` extended classes · `1107c651` language ·
`77d74e77`/`4b801c12`/`50c3a989`/`0e6359ef` robustness · `a39b128c` Ikarus/LeGo · `33c5f200` JSON switching ·
`bc632361` Ogg · `54f97e9b` Wld_InsertNpc

2026-10-11/13: `5ce0cbcd` fireplace children · `4d39c699` texture array materials

2026-10-15: `23d763df` texture array mips · `25183f71` dynamic VOB lights · `075f9e69` crawler melee · `4dcdfff6`
items as NPC parameters

2026-10-16: `af632900` ZEN material count header · `f4989135` subtitles from the archives

2026-10-18: `6cc8e641` ZEN mesh chunk lengths · `77bb432a` item instances by symbol

## Main files

`Services/Config/ConfigService.cs` · `Services/Caches/ResourceCacheService.cs` · `Domain/Audio/SoundDomain.cs` ·
menu adapters · `Assets/StreamingAssets/GameSettings.json`

## Config flags

`PreSelectMod` (was `EnableMod`), `ModPath`, `FallbackLanguage`, `EnableMusic`, `EnableOggAudio`; mod ini (from
`main`): `unitySkipMusicPlayback`, `unityFallbackLanguage` (first; `FallbackLanguage` is the fallback);
`GameSettings.json` (builds): `GameVersion`,
`ModPath`, `ModIni`, `EnableMusic`, `EnableZSpyLogs`, `EnableOggAudio`, `EnableVRDeviceSimulator` — one build runs G1, G2 and their mods
(see [How to test](14-how-to-test.md)).

2026-10-15: `EnableVmCacheResetOnLoad`

2026-10-16: `EnableZenMaterialCountFix`, `EnableSubtitlesFromVfs`

2026-10-18: `EnableZenChunkLengthFix`, `EnableItemDataBySymbol`

## Review notes

- ✅ Null guards are targeted at real mod failures (each commit names the mod/case), not blanket try/catch.
- ⚠️ Mod-specific behavior is partly hardcoded per mod name (a deliberate choice: the tested mod list is small).
  Document it if it grows.
- ⚠️ `GameSettings.json` gained new keys; check the release/packaging scripts copy the new defaults.

## How to test

- Point `ModPath` at Mroczne Tajemnice; new game, talk, sleep, fight.
- Vanilla G1 and G2 still boot with `ModPath` empty.

- Mroczne Tajemnice: smugglers' beach smithy complete, palms and nets textured.

- Mroczne Tajemnice: Siekacz's tavern and the catapult textured; the abandoned mine lit blue/teal.
- Mroczne Tajemnice abandoned mine: let the giant skeleton by Fellan's armor pick up the sword - no crash.
- Mroczne Tajemnice abandoned mine: the portal takes you to the ancient temple (log: "header fixed").
- A mod without an extracted `OU.BIN` under `VDFS/`: subtitles show (log: "Subtitles OU.BIN read from the archives").
- Gothic II Renovation (2026, fresh install, own profile): pre-caching passes Dragon Island (log: "has wrong chunk
  lengths - fixed: chunk 0xB020 length 75890 -> 76067"), a new game loads, Alrik wears his rags.
