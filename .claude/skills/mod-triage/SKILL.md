---
name: mod-triage
description: Checklist for getting a Gothic 1/2 mod (Ikarus, LeGo, Union based) to boot and play in Gothic-UnZENity, plus how to decide a mod is blocked by native/engine gaps. Use when someone asks to run/test a mod, a mod crashes at boot/menu/world load, or a mod's menu/dialog/text is broken.
---

# Mod triage

Mods are selected via `DeveloperConfig` (EnableMod / ModPath / ModIni) in the Editor or `GameSettings.json` in
builds. Work through the boot in order - each fix usually reveals the next failure.

## Boot-order checklist

1. **Archives / VFS**
   - Respect the mod ini `[FILES] vdf=` list - multi-language mods ship several `.mod` files and mounting all of
     them mixes languages.
   - `.mod` files mount alphabetically, last wins - check which archive actually provides a file.
   - The loose file on disk is not necessarily the effective one; always check what the mounted VFS resolves.
   - Compressed VDFs and Union's own archives can't be mounted by our ZenKit build - logged and skipped, content
     inside is missing.
   - World paths in the ini can have a subfolder prefix (`NewWorld\NEWWORLD.ZEN`) - the VFS is flat.
2. **Daedalus VM load**
   - Union mods can extend Daedalus classes (bigger arrays) - native ZenKit is fixed-size, VM load fails. There is a
     fallback to the loose compiled DAT.
   - Ikarus/LeGo: always override functions through the `SafeOverride` helpers (symbol may not exist in this mod).
   - Language detection probes a known string (e.g. `MOBNAME_CRATE`); mods rename it - add the new value to the list.
3. **Menu**
   - Mods rename the "really start a new game?" confirm dialog (`MENU_NEW_GAME`, `MENU_WIRKLICH_NEUES`,
     `MENU_PLAY_SELECT`, `MENU_NG`, ...). Add the new name to the existing hardcoded list in `MenuHandler` -
     the project prefers a finite name list over generic detection.
   - Menu items/backgrounds may be missing because the mod draws them with engine hooks - fall back, don't NRE.
4. **Precaching / world load**
   - Look for force-unwrapped (`!`) results of `TryGet*` (model scripts like `Humans.mds`, worlds, colliders) -
     mods break assumptions vanilla never did. Null-check and log.
   - `WorldScene.LoadWorldContentAsync` has a broad try/catch: "world loaded instantly but empty" usually means an
     earlier step threw and everything after it was skipped. Find the first exception.
   - Texture count can exceed `SystemInfo.maxTextureArraySlices`.
5. **Gameplay**
   - Mid-game `Wld_InsertNpc` (dialog-introduced NPCs) must spawn immediately, not only during initial load.
   - Dialogue: subtitles (`OU.BIN`) may sit inside an archive; voice files may be Ogg Vorbis with a `.wav` name.
   - Music: a broken DirectMusic segment can crash natively - use the music toggle to isolate.
   - Saves from the original engine are not loadable; never feed them to ZenKit save loading (native OOM).

## When to stop: native / engine gaps

Declare the mod blocked (and write down why) when the remaining failures are one of:
- ZenKit native parse failure on an unknown VOB class / archive entry type (world fails to load)
- unrecognised MDS keyword (animations missing)
- compressed archives our ZenKit build doesn't support
- LeGo hook engine / Ikarus raw engine-memory access (`HookEngine*`, raw class pointers) - there is no native
  engine to patch, so menu/dialog/UI patches silently don't install
- Union C++ plugin externals (they don't exist in our engine)
- uncatchable native crashes (dmusic, Burst animation jobs on a mismatched skeleton)

These need upstream ZenKit work or a design decision, not a C# one-liner. Report them with a repro.

## Always

- Regression-check vanilla G1, G2 and at least one known-working mod after a mod fix.
- Log the mod-specific fallback you added with a clear `LogCat` warning.
