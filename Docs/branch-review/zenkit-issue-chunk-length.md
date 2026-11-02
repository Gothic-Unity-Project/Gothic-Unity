# ZenKit issue draft: a world whose mesh chunk length is too short hangs `World::load`

Target: https://github.com/GothicKit/ZenKit (issue). Found 2026-10-18 while pre-caching the Gothic 2 mod *Renovation*
(2026 release). Our workaround: `ZenWorldArchiveFixDomain.TryFixMeshChunkLengths` (Gothic-UnZENity, flag
`EnableZenChunkLengthFix`) - still worth raising upstream, so the workaround can be removed. Related:
[zenkit-issue-material-count.md](zenkit-issue-material-count.md).

---

## Title

`World::load` never returns when a `MeshAndBsp` chunk stores a length shorter than its data (Renovation's
`DRAGONISLAND.ZEN`)

## Description

Loading `DRAGONISLAND.ZEN` from *Renovation* (`Renovation_base.mod`, ASCII world archive saved 24.8.2026) never returns:
one CPU core at 100 %, memory constant, no exception. The original Gothic 2 loads the world fine.

The world mesh's `0xB020` (materials) chunk stores a length of **75 890** bytes, but its 439 materials take **76 067**
bytes - the stored length leaves out the last material (`NW_MONASTERY_STEPS_20`, starting at offset 75 927 of the
chunk). The `MeshAndBsp` entry's stored size is short by the same 177 bytes. All other chunks are correct:

```
0xB000 19 · 0xB010 86 · 0xB020 75890 (real 76067) · 0xB026 8573412 · 0xB030 581968 · 0xB040 2833084 ·
0xB050 3957396 · 0xB060 0 · 0xC000 6 · 0xC010 443420 · 0xC040 62601 · 0xC045 9048 · 0xC050 2619 · 0xC0FF 1
```

`proto::read_chunked` (`include/zenkit/Stream.hh`) reads the materials (they're read one by one, so all 439 come out
right), logs `bytes overflowed in section b020`, then seeks back to `tell() + stored size` - into the middle of the last
material. The bytes there are read as the next chunk header (`0x4F4D`, size 1 414 742 350), and the reader most likely
loops on `while (!r->eof())` after seeking past the end of the buffer (not verified in a debugger).

## Reproduction

1. File: `DRAGONISLAND.ZEN` from `Data/ModVDF/Renovation_base.mod` of *Gothic II Renovation* (2026). The older
   `Renovation_beta2.mod` doesn't override this world.
2. `zenkit::World world; world.load(read.get(), GameVersion::GOTHIC_2);` (or `new ZenKit.World(vfs, "DRAGONISLAND.ZEN",
   GameVersion.Gothic2)` via ZenKitCS).
3. Result: the call doesn't return (killed after 240 s).

Proof that it's only the length: setting the `0xB020` chunk's length to 76 067 makes the same file load at once - 4 VOBs,
439 materials, 80 061 polygons, 48 497 vertices, 110 854 BSP polygons, 315 waypoints. The vanilla `DRAGONISLAND.ZEN`
(BIN_SAFE) is unaffected.

## Expected

Either load the world like the engine (it doesn't rely on the stored length after reading the materials), or fail with
an error. A loader should never hang on bad input.

## Suggestions

- In `read_chunked`: when the callback read **past** the stored end (`pos > target`), continue from `pos` instead of
  seeking back to `target` - the data was clearly longer than stored. (For `pos < target` seeking forward stays right.)
- Stop when a seek target lies beyond the end of the buffer (throw `ParserError`) instead of looping.

## Environment

- ZenKit as shipped in ZenKitCS (`com.gothic.core.binaries@5f250fba233f`), Windows x64.
