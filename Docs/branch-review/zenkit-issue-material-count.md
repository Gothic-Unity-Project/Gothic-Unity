# ZenKit issue / PR draft: binary archive header eats a `0x0A` first payload byte

Target: https://github.com/GothicKit/ZenKit (issue + small PR). Found 2026-10-15 while loading Mroczne Tajemnice's
`ANCIENTTEMPLE.ZEN` (Gothic 1 mod). Our workaround (commit `af632900`): `ZenWorldArchiveFixDomain` (Gothic-UnZENity, flag `EnableZenMaterialCountFix`) -
still worth raising upstream, so the workaround can be removed.

---

## Title

`ReadArchiveBinary::read_header` skips the first payload byte when it is `0x0A` (world with 266 materials fails with
`std::bad_alloc`)

## Description

`ZkWorld_load()` / `World::load` fails with `std::bad_alloc` on a world whose mesh has exactly **266** materials. The
same happens for any binary archive whose first payload byte is `0x0A` - for the world mesh's material archive that's a
material count of 10, 266, 522, 778, ...

The world mesh's materials are stored as a small `BINARY` archive inside the `0xB020` chunk of `MeshAndBsp`:

```
ZenGin Archive\nver 1\nzCArchiverGeneric\nBINARY\nsaveGame 0\nEND\nobjects 266      \nEND\n\n
0A 01 00 00            <- u32 material count = 266 (0x10A)
4F 43 4F 44 ...        <- first material name, ...
```

`ReadArchiveBinary::read_header` (`src/archive/ArchiveBinary.cc`) reads the second `END` with

```cpp
if (read->read_line_then_ignore("\n") != "END") {
```

`read_line_then_ignore` (`src/Stream.cc`) keeps skipping **every** character in the set after the line terminator,
so after `END\n` it consumes the blank `\n` **and** the `0x0A` low byte of the material count. The count is then read
from `01 00 00 4F` (= 1 325 400 065) and the following `std::vector` reservation throws `std::bad_alloc`.

A world with 26 materials (`1A 00 00 00`) loads fine, so this only hits specific counts.

## Reproduction

1. File: `ANCIENTTEMPLE.ZEN` from the Gothic 1 mod *Mroczne Tajemnice* (ASCII world archive, `MeshAndBsp` version
   `0x2090000`, mesh v9, 266 materials). Any world/mesh with 10 or 266 materials should reproduce it.
2. `zenkit::World world; world.load(read.get(), GameVersion::GOTHIC_1);` (or `new ZenKit.World(path)` via ZenKitCS).
3. Result: `ZkWorld_load() failed: std::bad_alloc`.

Proof that it's only the header: replacing `END\n\n` before the count with `END\0` (and shrinking the `0xB020` chunk
length and the `MeshAndBsp` blob size by 1) makes the same file load with 266 materials, 2273 polygons, 1627 vertices,
8709 BSP polygons and 70 waypoints - exactly the counts stored in the chunks. Padding the materials to 270 (`0E 01 00 00`)
also loads.

## Expected

The header ends after exactly one empty line (ZenGin writes `END\n` + `\n`). The payload must not be touched.

## Suggested fix (PR)

`src/archive/ArchiveBinary.cc`, `ReadArchiveBinary::read_header`:

```cpp
// Before
if (read->read_line_then_ignore("\n") != "END") {
    throw ParserError {"ReadArchiveBinary", "second END missing"};
}

// After: consume the END line and exactly one following newline - the payload may start with 0x0A.
if (read->read_line(false) != "END") {
    throw ParserError {"ReadArchiveBinary", "second END missing"};
}
if (read->read_char() != '\n') {
    read->seek(-1, Whence::CUR);
}
```

Worth checking the same pattern in `ArchiveBinsafe` and the header reader in `Archive.cc` (`read_line(true)` skips
`" \t\r\n\v\f"`): any header line directly followed by binary data can lose a leading `0x09`-`0x0D`/`0x20` byte.

Test idea: a binary archive whose first payload value is `u32 266` (`0A 01 00 00`) must read back as 266.

## Environment

- ZenKit as shipped in ZenKitCS (`com.gothic.core.binaries@17149dd198e7`), Windows x64.
- Loading as `GameVersion::GOTHIC_2` fails the same way.
