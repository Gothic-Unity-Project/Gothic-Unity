# 9. Dialogs, UI, audio

[← Overview](README.md)

## What changed

**Dialogs**
- Free movement during dialogs within range; a "dialog bubble": while someone talks you can't leave (skip first),
  with the choices shown leaving ends the dialog. Far NPCs don't open dialogs.
- Dialog box and subtitles drawn on top (never cut by walls or NPCs), subtitles hidden beyond 12 m, removed when
  the speaker dies. Dialog closes when the NPC draws a weapon.
- Robustness: null audio, missing subtitle blocks, VM exceptions guarded; G2/mod SVM lines unknown to ZenKit play.

**UI**
- Script print events reach VR (`PrintScreen` messages, 2D sounds, taken items); repeated messages don't lag.
- Status menu: right protection values, talent rows, weapon ranks and percentages in MT and G2.
- Menu pictures with an alpha mode (the G2 "Noc Kruka" logo) use plain alpha blending: `ToTransparentMode` relied on
  the `_ALPHATEST_ON` variant, which builds strip for runtime-made materials (the logo looked different in builds).
- HP bars reflect live damage/healing and the loaded health; chapter screen waits for its jingle.

**Audio**
- WAV parser reads chunks by declared size; 8-bit WAV conversion fixed.
- Ogg Vorbis decoding via NVorbis for mod dubbing; dmusic crash escape hatch (`EnableMusic`).

**Added 2026-10-10 – 2026-10-11**

- **Monster dialogs:** the hero is told apart by instance, not `id == 0` (monsters have id 0 too - the undead dragon's
  lines went to the hero's queue); `Npc_CheckInfo(important=0)`; dialog range and voice range grow with the body size;
  `Npc_ClearAIQueue` keeps the dialog partner's lines.
- **Hit numbers:** red/green `-X/+X HP` over NPC heads and next to the hero's bar (mana in blue), optional `HP/MAX` over
  NPC names, `MANA/MAX` on the mana bar (Immersion settings).
- **HUD on top:** HP/mana bars, screen messages, the chapter popup and menus draw on top of the world like the dialog
  box; VR hands and the UI laser draw above it.

**Added 2026-10-11 – 2026-10-13**

- **Music zones like the engine** (OpenGothic `WorldSound`): the zone comes from the player's position every 2 s (the
  current zone stays while inside, else the last zone containing us, else the default zone), themes fall back NGT→DAY,
  THR→FGT→STD and to the default zone, night follows the game time, fight music ends with the fight. G2's pirate camp
  kept the bandit music, night themes never played.
- **No more music crash:** a world's music starts on a fresh DirectMusic performance - dmusic crashed natively on the
  audio thread (`DmPerformance_renderPcm`) once world themes played after `gamestart.sgt` on the same performance
  (reproduced outside Unity). Segments are downloaded once and cached.
- **Dialogs on every game's VM:** `C_Info` instances were initialized once at boot, so what scripts write into them
  during the game went to the new VM's unbound symbols - the anvil's weapon choices were empty. Mob dialogs start after
  the calling script like the engine's queued `AI_ProcessInfos`.
- **Save names on an on-screen keyboard** (`VirtualKeyboard`): saving opens a panel in Gothic's font in front of the
  camera (letters, digits, shift, space, backspace, OK, cancel; the key under the laser lights up), an occupied slot
  suggests its old name, a physical keyboard types too. VR registers runtime canvases for the UI laser through
  `GlobalEventDispatcher.UiCanvasShown/Hidden`.
- **Menu zoom:** the right stick pulls the VR menu closer (down) or pushes it away (up), 1-8 m, kept for the session.
  Removed with the 2026-10-17 merge: `main`'s menu is 1.5 m away.

**Merged from `main` 2026-10-17** (`45661ade`, PR #398, details in [merge-2026-10-17.md](merge-2026-10-17.md)):
`main`'s dialog bubble (soft hold, END on walking away), on-top UI configured in prefabs (`VRUiOnTop` +
`VRUiOnTopService`), `VRFollowCamera`, `VRPrintScreen` and `Snd_Play` through `AudioService` replace the branch's
versions - `VRScreenMessages` and `VRMenuBillboard` are gone. Kept on top: the mob dialog anchor, the dialog range by
body size, polling HP bars and `VRUiOnTop.CopyTo` for the hero's bars and the chapter screen (the bar fill gets a sprite
of its material's texture - `main`'s shader samples the Canvas texture, the fill came out white).

**Added 2026-10-18**

- **Dialog until AI_StopProcessInfos** (`17bcdbf0`, `EnableDialogUntilStopProcessInfos`): an NPC drawing his weapon
  mid-dialog (Lukis, Rust) closed it at once (main) - his last lines ran without the dialog bubble and couldn't be
  skipped by grabbing him. It stays open while lines or the stop are queued.
- **Pointer line on top** (`1978c18e`, `EnablePointerLineOnTop`): the red UI pointer line (queue 3000) was hidden by the
  on-top in-game menu (3900); like the hands it's drawn in 3950 while a menu or dialog is open.

**Added 2026-10-22**

- **NPC faces** (`e077c111`, `EnableFaceAnimations`): the talking mouth was a stub (`PlayHeadAnimation` FIXME) and
  `Mdl_StartFaceAni` / `Mdl_ApplyRandomFaceAni` weren't registered. Head morphs are now blended like the engine: each
  running MMB animation adds its offset from the rest pose, weighted by intensity and its blend in/out (MMB values are
  weight per ms - a mood fades in over 1 s, a blink in 40 ms), one animation per MMB layer (moods/T_EAT/T_HURT 1,
  VISEME 2, R_EYESBLINK 4). Before, each morph set the whole mesh, so a blink wiped the mouth - that's why blinking
  (`EnableNpcEyeBlinking`) was WIP and off; with the flag the head blinks on its own. VISEME loops for an NPC's line,
  MDS morph events (T_HURT loops) are held 1 s. `VobAnimateMorph` keeps the old path.

**Added 2026-10-23**

- **Sound effects from SOUND/SFX** (`c9b61548`, `EnableSfxFromSfxFolder`): the VFS finds files by name; G1 has
  `SVM_2_DEAD.WAV` in `sound.VDF` (`_WORK/DATA/SOUND/SFX`, 63928 B) and in the newer `Speech.vdf` (`SOUND/SPEECH`,
  33852 B), so the speech recording won. SFX.DAT maps the SVM pain/death sounds onto three sets (`SVM_15_DEAD` ->
  `svm_2_dead.wav`). A C_SFX file is now resolved in `_WORK/DATA/SOUND/SFX` first (clip cache key `SFX/<name>`).
- **Numbered SFX variants** (`ca48b1e9`, `EnableSfxNumberedVariants`): variants were only `NAME_A1, _A2...`; the pain
  screams are `SVM_x_AARGH, SVM_x_AARGH_1 ... _4` - only the first one played.
- **Dialog body turn** (`dbf99c7f`, `EnableDialogBodyTurn`): the dialog head look-at stops at 80 degrees, so with the
  hero behind him the NPC looked straight ahead. Beyond it he turns his body (180 deg/s) until the hero is within 10
  degrees, playing the engine's turn animation (`RotL`/`RotR` like `AI_TurnToNpc`) so the feet step; not while using a
  mob. The turn animation swings the head to the side - while it plays and blends out the head keeps its pose relative
  to the body, the look-at goes on top.
- **Hero pain sound** (`51550b1c`, `EnableHeroHurtSound`): `SVM_<voice>_AARGH` from the hero's voice source - he has no
  `NpcSound`, nothing was heard.

## Key commits

`ee4dbc2a` dialog bubble · `c1a4c1ad` free movement · `d109a6aa`/`cd10002c` on top · `bf3703ca` subtitles on death ·
`2d308ea2` SVM · `b1d68e70` print events · `bac73b4d` message lag · `59661daf`/`3bc6ca69` status menu ·
`f3968086`/`5c3c26d6` HP bars · `669f194d`/`98a928a1` WAV · `bc632361` Ogg

2026-10-10/11: `e8f50e2f`/`170e723b`/`b40e757d`/`ae8731a5` dragon dialogs · `8b402034`/`4a12c4e7`/`687a0b67` hit numbers
· `768d61a8` HUD on top

2026-10-11/13: `fd2195b1` music zones · `dd15ecdc` fresh DirectMusic performance · `03788c08` dialogs per game VM · `08aea420` save name keyboard · `c2d9f34c` menu zoom

2026-10-18: `17bcdbf0` dialog until AI_StopProcessInfos · `1978c18e` pointer line on top

2026-10-22: `e077c111` NPC faces (talking mouth, moods, blinking)

2026-10-23: `c9b61548` SFX from SOUND/SFX · `51550b1c` hero pain sound · `ca48b1e9` numbered SFX variants ·
`dbf99c7f` dialog body turn

## Main files

`Services/Player/DialogService.cs` (+255) · `Domain/Npc/Actions/AnimationActions/Output.cs` · VR
`UI/VRUiOnTop.cs`, `UI/VRScreenMessages.cs`, `UI/VRGothicText.cs`, shader `GothicUIOnTop.shader` ·
`Domain/Audio/SoundDomain.cs` · `Adapters/UI/StatusBars/StatusBarAdapter.cs`

## Config flags

`EnableDialogFreeMovement`, `DialogMaxDistance`, `NpcDialogStopDistance`, `EnableDialogAlwaysOnTop`,
`EnableScreenMessages`, `EnableImportantInfoOrder`, `HideHealthBar`, `EnableMusic`, `EnableOggAudio`.

2026-10-10/11: `EnableDialogHeroByInstance`, `EnableDialogRangeByBodySize`, `EnableDialogKeepsOutputs`,
`EnableHudAlwaysOnTop`

2026-10-11/13: `EnableMusicZonesByPosition`, `EnableDialogsPerGameVm`, `EnableSaveNameKeyboard` (removed 2026-10-16: `main`'s keyboard), `EnableMenuZoom`

Removed with the 2026-10-17 merge (`main` has them always on or replaced): `EnableDialogFreeMovement`,
`DialogMaxDistance`, `EnableDialogAlwaysOnTop`, `EnableScreenMessages`, `EnableMenuZoom`.

2026-10-18: `EnableDialogUntilStopProcessInfos`, `EnablePointerLineOnTop`

2026-10-22: `EnableFaceAnimations`

2026-10-23: `EnableSfxFromSfxFolder`, `EnableHeroHurtSound`, `EnableSfxNumberedVariants`, `EnableDialogBodyTurn`

## Review notes

- ⚠️ **`VRScreenMessages` and `VRCinema` use static `_instance` singletons** instead of DI. They should be
  registered/injected like other services/adapters.
- ⚠️ `VRPlayerService.TelekinesisDeactivated` is a plain C# `event`; `CLAUDE.md` asks for `GlobalEventDispatcher`.
- ✅ `GothicUI` and `GothicUIOnTop` shaders are in Always Included Shaders (needed, they're loaded via
  `Shader.Find`).

## How to test

- Talk to an NPC and walk around; walk away during choices. Kill an NPC mid-sentence.
- Look at an NPC's face up close: the mouth moves while he talks and stops after, he blinks; angry in a fight,
  frightened when fleeing (G1 MT: Drax from scavengers), pained for a moment when hit.
- A mod with Ogg dubbing (G2 New Balance) — voices play.
- G2: talk to a dragon (Irdorath: undead dragon) - his lines are heard with subtitles.
- Immersion settings: hit numbers and HP numbers on, hit an NPC and get hit.

- G2: walk into and out of the pirate camp several times - the music changes, no crash; at night night themes.
- G2 anvil: "Forge a sword" shows the weapon names (also after loading a save).
- Save the game: the keyboard shows, type a name, OK - the slot shows the name; X saves nothing.
- After the merge: the hero's HP/mana bars on the wrist have their colors and stay above the other hand; PrintScreen
  texts and the chapter screen follow the head; Diego on the bench talks seated.
- G1: Lukis (Zollo) draws his sword mid-dialog - you can't walk off, grabbing him skips his lines, then he attacks.
- In-game menu: the red pointer line reaches the entry you point at.
