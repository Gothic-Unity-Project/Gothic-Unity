# Branch review: `experimental/game-completion-candidate-vibecoded-borobongo`

Review notes for maintainers. The branch started as `experimental/save-load-magic-movers` and was renamed
before pushing. Goal: make Gothic 1 and Gothic 2 completable in VR — a "game completion candidate" build.

| | |
|---|---|
| Compared against | `main` at `e91bc09c` (current `main`, already merged into the branch) |
| Commits | 260 on the branch that `main` doesn't have |
| Files | 290 changed (147 added, 143 modified), +29,040 / −1,095 lines |
| C# | 190 files (62 new) |
| Period | 2026-06-14 → 2026-10-05 |
| Tested on | PCVR, G1 (vanilla + Mroczne Tajemnice mod), G2 NotR. **Not** built or tested: Quest/Pico, Flat mode |

## Author's note

This branch is **my individual vision** of where the project could go, and any of it may change for the full
release. My goal was to patch the holes so the games can be **finished**, and finished **enjoyably**: effects aren't
strictly needed to complete the game, but a game without stimuli isn't a game to me.

Please read the changes with two kinds in mind:
- **VR-specific solutions** (how you charge a spell, split a stack, trade at a counter, the hero body…) are mostly my
  **opinion** of how it should feel in VR. Open to discussion.
- **Code that reproduces Gothic's own behavior** (Daedalus externals, perceptions, fight and knockout rules, mana,
  rooms…) is meant as **fact**: it follows the original scripts and OpenGothic as reference. I can still be wrong
  there — corrections welcome.

---

Almost everything new is behind a `DeveloperConfig` flag (**116 new fields**), so features can be switched off one by
one. The production config (`Production.asset`) has them on except cheats, NPC ranged combat and save/load.

## How to read this

Each theme has its own page with: what changed, key commits, main files, config flags, review notes and how to
test it. Start with **Must fix before merge** below, then skim the themes you own. Testers: start with
[How to test](14-how-to-test.md).

| # | Theme | Size | Page |
|---|---|---|---|
| 1 | Combat: VR weapons, NPC melee, knockouts | ~45 commits | [01-combat.md](01-combat.md) |
| 2 | Magic: VR casting, NPC casters, summons, transformations | ~25 commits | [02-magic.md](02-magic.md) |
| 3 | NPC AI: perceptions, targeting, routines | ~35 commits | [03-npc-ai-perception.md](03-npc-ai-perception.md) |
| 4 | NPC movement: navmesh, water, falling, ladders | ~10 commits | [04-npc-movement.md](04-npc-movement.md) |
| 5 | VR hero: body, IK, items in hand, docs/maps | ~25 commits | [05-vr-hero-interaction.md](05-vr-hero-interaction.md) |
| 6 | Inventory, trade, stacks, NPC loot | ~20 commits | [06-inventory-trade-loot.md](06-inventory-trade-loot.md) |
| 7 | World: movers, triggers, MOBSI, rooms | ~25 commits | [07-world-vobs.md](07-world-vobs.md) |
| 8 | Rendering: lights, day/night, particles, Bink video | ~15 commits | [08-rendering-video.md](08-rendering-video.md) |
| 9 | Dialogs, UI, audio | ~20 commits | [09-dialogs-ui-audio.md](09-dialogs-ui-audio.md) |
| 10 | Save / load (WIP, off by default) | ~15 commits | [10-save-load.md](10-save-load.md) |
| 11 | Mods and robustness | ~20 commits | [11-mods-robustness.md](11-mods-robustness.md) |
| 12 | Dev tools, config, build, project settings | ~15 commits | [12-devtools-config-build.md](12-devtools-config-build.md) |
| — | Architecture and conventions check | whole branch | [13-architecture-conventions.md](13-architecture-conventions.md) |
| — | How to test: setup, dev config, G2 test items, traders | — | [14-how-to-test.md](14-how-to-test.md) |

## Headline features

- **Combat:** VR bows (real string), crossbows, projectile spells charged in the free hand and thrown with Gothic's
  invest levels; NPC melee with combos, ranged and magic attacks; engine-like knockouts.
- **NPCs feel alive:** engine-like perceptions (warnings, murder witnesses, sneaking, entering huts, owners guarding
  chests), runtime NavMesh chases, swimming/wading, falling, ladders, fleeing, berserk, NPCs drinking potions.
- **VR hero:** full body in armor under the headset with two-bone IK, transformation scrolls (you *are* the wolf),
  torches, smoking joints, equipping armor/amulets/rings, reading books and maps (with the hero arrow).
- **Inventory:** VR trading counter, stack splitting, NPC loot backpack, item details popup, backpack vacuum.
- **World:** movers, gates and winches, the Gothic trigger system, shrines/alchemy/rune tables/beds (MOBSI), G2
  treasure digging, original Bink videos in a VR cinema.
- **Look:** caves lit by torches, night falling on the world, particles at Gothic's rate, OpenGothic-like light pool.
- **Mods:** mod loading (G1 Mroczne Tajemnice, G2 Union mods partly), Ogg Vorbis dubbing, JSON mod switching.

## Must fix before merge

1. ✅ *Fixed:* `Bootstrap.unity` briefly pointed at a gitignored local config; it uses `Production.asset` again,
   as on `main`. Use your own config locally without committing the scene (see [How to test](14-how-to-test.md)).
2. **`Production.asset` is now written out in full** (all 171 fields serialized). That's correct, but every flag
   default now lives there too — review its values once, it defines what players get.
3. **Quest/Pico not verified.** The light pool uses 512 shader slots on mobile, the Bink decoder runs on a thread,
   NavMesh is baked at runtime — all untested on Android.
4. **Flat mode not built.** `Gothic-Flat` has no changes, but Core APIs it uses did change. Needs one compile check.

## Testing status

- Manually play-tested per feature in VR (see each page). Setup, test items and traders:
  [14-how-to-test.md](14-how-to-test.md).
- Automated: one new PlayMode test file, `Gothic-Tests/PlayMode/TradePricingTests.cs`. Nothing else is covered.

## Licensing

- **Bink decoder** (`Assets/Gothic-Core/Scripts/Bink/`): C# port of OpenGothic `common/bink` (MIT) whose codec comes
  from FFmpeg (LGPL-2.1+). Full attribution and the MIT notice are in its `README.md`; LGPL 2.1 §3 allows it in our
  GPLv3 project. No RAD code, no videos shipped.
- **NVorbis** (`Assets/Plugins/NVorbis/`): MIT, license file included. Committed directly via a `.gitignore`
  exception, not via the binary-dependencies repo (see page 12).

## Process notes

- The branch was developed with heavy AI assistance ("vibecoded"), feature by feature, each confirmed in VR
  before committing. Commit messages describe behavior, not code.
- Commit style follows `feat:/fix:/chore:/docs:`.
- Early commits (June) are merges of small feature branches (`feat/zs-dead`, `feat/marvin-cheats`,
  `stack/05-*`) that were never merged to `main` separately.
