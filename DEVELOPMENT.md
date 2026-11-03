# Development
First run (Gothic path in `GameSettings.dev.json`, Bootstrap scene, `GameConfiguration`, Flat mode without a headset) is
covered by the wiki: [Developer Setup](https://github.com/Gothic-Unity-Project/Gothic-Unity/wiki/Developer-Setup).
This file only adds what the wiki does not have: the optional reference repositories next to this repo.

## Reference repositories (optional)
For development it helps to have the upstream sources next to this repo. They are **not**
required to build or play. Each one is symlinked into the repo root; all of these names are git-ignored.

| Symlink in repo root | Upstream | Use it for |
|---|---|---|
| `Gothic-Unity.wiki` | [Gothic-Unity.wiki](https://github.com/Gothic-Unity-Project/Gothic-Unity.wiki.git) | Project wiki |
| `ZenKit` | [GothicKit/ZenKit](https://github.com/GothicKit/ZenKit) | Native asset/script parser |
| `ZenKitCAPI` | [GothicKit/ZenKitCAPI](https://github.com/GothicKit/ZenKitCAPI) | C API between ZenKit and C# |
| `ZenKitCS` | [GothicKit/ZenKitCS](https://github.com/GothicKit/ZenKitCS) | C# bindings we call from Unity |
| `OpenGothic` | [Try/OpenGothic](https://github.com/Try/OpenGothic) | Reference engine behaviour |
| `Daedalus` | [VaanaCZ/gothic-1-classic-scripts](https://github.com/VaanaCZ/gothic-1-classic-scripts) | Gothic 1 Daedalus scripts |

Clone everything as siblings of this repo, then link it in:
