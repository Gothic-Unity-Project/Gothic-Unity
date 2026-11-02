# Third-party notices

Gothic UnZENity is licensed under the GNU GPL v3 (see `LICENSE`). It uses or contains the third-party work listed
below. No Gothic game data (meshes, textures, sounds, videos, scripts) is shipped: everything is loaded at runtime from
the player's own Gothic installation. "Gothic" is a trademark of THQ Nordic; "Bink" is a trademark of RAD Game Tools /
Epic Games - both names only describe what the project works with.

## Code contained in this repository

| Component | Where | License | Notes |
|---|---|---|---|
| Bink decoder (port of OpenGothic `common/bink`, itself based on FFmpeg) | `Assets/Gothic-Core/Scripts/Bink/` | LGPL-2.1-or-later (FFmpeg) + MIT (OpenGothic) | Origin, every copyright holder and the MIT notice: `Assets/Gothic-Core/Scripts/Bink/README.md`. Used under GPL v3 via LGPL 2.1 §3. |
| NVorbis (Ogg Vorbis decoder) | `Assets/Plugins/NVorbis/` | MIT | `Assets/Plugins/NVorbis/LICENSE.txt` |
| Uber Logger | `Assets/UberLogger/` | MIT | `Assets/UberLogger/LICENSE` |

## Dependencies fetched by the Unity Package Manager

They are not part of this repository; each package carries its own license file.

| Component | License | Source |
|---|---|---|
| ZenKit / ZenKitCS (Gothic asset parser) | MIT | https://github.com/GothicKit/ZenKit, https://github.com/GothicKit/ZenKitCS |
| dmusic (DirectMusic reimplementation) | MIT | https://github.com/GothicKit/dmusic |
| Binary dependencies (native ZenKit/dmusic builds) | GPL-3.0 (repository) | https://github.com/Gothic-Unity-Project/binary-dependencies |
| Reflex (dependency injection) | MIT | https://github.com/gustavopsantos/reflex |
| MyBox | MIT | https://github.com/Deadcows/MyBox |
| Newtonsoft Json.NET (`com.unity.nuget.newtonsoft-json`) | MIT | https://github.com/JamesNK/Newtonsoft.Json |
| Unity packages (URP, XR, Localization, TextMesh Pro, ...) | Unity Companion License / Unity terms | Unity Package Manager |
| Unity MCP (editor tooling only, not in builds) | MIT | https://github.com/CoplayDev/unity-mcp |

## Behavior references

Many systems reproduce the original engine's behavior and use **OpenGothic** (https://github.com/Try/OpenGothic,
MIT, Copyright (c) 2019 Try) as the reference for how the engine works - e.g. perceptions, rooms, movers and their
timing, triggers, code masters, interactive objects, the light pool. That code is our own C# written after studying
OpenGothic and the Gothic scripts; where a value or table is taken over (e.g. the mover rotation speed scaling), the
code comment says so. The OpenGothic MIT notice is included in `Assets/Gothic-Core/Scripts/Bink/README.md`.
