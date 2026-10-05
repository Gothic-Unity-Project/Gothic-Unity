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
- HP bars reflect live damage/healing and the loaded health; chapter screen waits for its jingle.

**Audio**
- WAV parser reads chunks by declared size; 8-bit WAV conversion fixed.
- Ogg Vorbis decoding via NVorbis for mod dubbing; dmusic crash escape hatch (`EnableMusic`).

## Key commits

`ee4dbc2a` dialog bubble · `c1a4c1ad` free movement · `d109a6aa`/`cd10002c` on top · `bf3703ca` subtitles on death ·
`2d308ea2` SVM · `b1d68e70` print events · `bac73b4d` message lag · `59661daf`/`3bc6ca69` status menu ·
`f3968086`/`5c3c26d6` HP bars · `669f194d`/`98a928a1` WAV · `bc632361` Ogg

## Main files

`Services/Player/DialogService.cs` (+255) · `Domain/Npc/Actions/AnimationActions/Output.cs` · VR
`UI/VRUiOnTop.cs`, `UI/VRScreenMessages.cs`, `UI/VRGothicText.cs`, shader `GothicUIOnTop.shader` ·
`Domain/Audio/SoundDomain.cs` · `Adapters/UI/StatusBars/StatusBarAdapter.cs`

## Config flags

`EnableDialogFreeMovement`, `DialogMaxDistance`, `NpcDialogStopDistance`, `EnableDialogAlwaysOnTop`,
`EnableScreenMessages`, `EnableImportantInfoOrder`, `HideHealthBar`, `EnableMusic`, `EnableOggAudio`.

## Review notes

- ⚠️ **`VRScreenMessages` and `VRCinema` use static `_instance` singletons** instead of DI. They should be
  registered/injected like other services/adapters.
- ⚠️ `VRPlayerService.TelekinesisDeactivated` is a plain C# `event`; `CLAUDE.md` asks for `GlobalEventDispatcher`.
- ✅ `GothicUI` and `GothicUIOnTop` shaders are in Always Included Shaders (needed, they're loaded via
  `Shader.Find`).

## How to test

- Talk to an NPC and walk around; walk away during choices. Kill an NPC mid-sentence.
- A mod with Ogg dubbing (G2 New Balance) — voices play.
