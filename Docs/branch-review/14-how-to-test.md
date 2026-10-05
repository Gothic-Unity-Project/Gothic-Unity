# 14. How to test

[← Overview](README.md)

Setup for testers, then where to look. Each theme page also has its own "How to test" section.

## 1. Point the game at your Gothic (and mod) folder

Create `Assets/StreamingAssets/GameSettings.dev.json`. It's gitignored and overrides `GameSettings.json` field by
field, so it only needs the keys you change:

```json
{
    "Gothic1Path": "C:/Program Files (x86)/Steam/steamapps/common/Gothic",
    "Gothic2Path": "C:/Program Files (x86)/Steam/steamapps/common/Gothic II",
    "ModPath": "",
    "ModIni": "",
    "LogLevel": "Message"
}
```

- **Paths:** forward slashes, or double backslashes (`"C:\\Games\\Gothic"`). Without a path, the Steam default
  folder is used.
- **Vanilla:** leave `ModPath` and `ModIni` empty.
- **Mod:** `ModPath` is the mod's own game folder (a full Gothic install with the mod: `Data/`, `System/`, …).
  `ModIni` is the mod's ini inside `ModPath/System/`. Example for Mroczne Tajemnice (G1):
  ```json
  "ModPath": "C:/Games/Gothic MT",
  "ModIni": "DM_E.INI"
  ```
  Then pick Gothic 1 in the game menu.
- Other optional keys: `EnableMusic` (turn off if a mod's music crashes), `EnableOggAudio` (mod dubbing in Ogg),
  `EnableZSpyLogs` (Daedalus logging), `LogCategories`.

## 2. Pick a developer config (for cheats)

The Bootstrap scene uses `Resources/DeveloperConfigs/Production.asset` — the release settings, no cheats.
For testing:

1. Duplicate `Production.asset` into `Assets/Gothic-Core/Resources/DeveloperConfigs/Local/` (gitignored), or create one
   via *Create → Gothic → ScriptableObjects → DeveloperConfiguration* in that folder.
2. Open `Assets/Gothic-Core/Scenes/Bootstrap.unity`, select **GameManager**, and drag your config into
   **BootstrapAdapter → Developer Config**. **Don't commit this scene change.**
3. Useful fields:

   | Field | What it does |
   |---|---|
   | `PreselectGameVersion` / `GameVersion` | Skip the game selection and start G1 or G2 directly |
   | `ActivateMarvinMode` | Shows the Marvin cheat panel on the hand menu |
   | `EnableLevel5Cheat` | Marvin button: +5 levels (+50 LP, more HP, +100 mana) |
   | `EnableTimeSkip` | Marvin button: skip 30 minutes (routines, day/night) |
   | `MarvinTeleportWaypoint` | Marvin button: teleport to this waypoint (e.g. `START`) |
   | `MarvinSpawnNpcSymbol` | Marvin button: spawn this NPC next to you (e.g. `VLK_417_Constantino`) |
   | `MarvinGiveItems` | Marvin button: give these items (list below) |
   | `PlayerInventoryAddition` | Items given at game start, format `ItMi_Gold:500;ItRu_Light:1` |

   Note: changing a default in `DeveloperConfig.cs` does **not** change an existing config asset — set values in the
   asset itself.

4. Open the **Bootstrap** scene and press Play (VR headset, or the HVR simulator config `Lab.HVR.Simulator.asset`).

## 3. Which hero to play

- **Gothic 1:** you can play the developers' test hero `PC_ROCKEFELLER` (high stats, lots of items). In your Gothic
  folder, edit `System/GothicGame.ini` → `[SETTINGS]` → `player=PC_ROCKEFELLER` (back to `PC_HERO` afterwards).
  The Mroczne Tajemnice mod already uses `PC_ROCKEFELLER` by default; beds and shrines work with it.
- **Gothic 2: don't switch the hero instance.** Many G2 scripts and dialogs expect `PC_HERO`. Play the normal hero
  and give yourself test items with Marvin (below) plus the Level +5 button for mana and HP.

## 4. Gothic 2 test items (Marvin "Give items")

Paste into `MarvinGiveItems` (comma separated, optional `:amount`; names checked against the G2 NotR scripts):

```
ITMI_GOLD:2000, ITKE_LOCKPICK:20, ITLSTORCH:5, ITMI_JOINT:5, ITFO_APPLE:5, ITFO_BACON:3, ITFO_BEER:3,
ITPO_HEALTH_03:10, ITPO_MANA_03:20, ITPO_SPEED:3,
ITMW_SCHWERT1, ITMW_ZWEIHAENDER1, ITRW_BOW_L_01, ITRW_CROSSBOW_L_01, ITRW_ARROW:100, ITRW_BOLT:100,
ITAR_MIL_L, ITAM_PROT_POINT_01, ITRI_PROT_EDGE_01,
ITRU_LIGHT, ITRU_FIREBOLT, ITRU_INSTANTFIREBALL, ITRU_CHARGEFIREBALL, ITRU_ZAP, ITRU_ICEBOLT, ITRU_ICECUBE,
ITRU_THUNDERBALL, ITRU_LIGHTNINGFLASH, ITRU_WINDFIST, ITRU_ICEWAVE, ITRU_FIRESTORM, ITRU_FIRERAIN,
ITRU_SLEEP, ITRU_FEAR, ITRU_SHRINK, ITRU_SUMGOL, ITRU_SUMWOLF, ITRU_SUMSKEL, ITRU_TELEPORTXARDAS,
ITRU_TELEPORTSEAPORT, ITRU_WATERFIST, ITRU_GEYSER, ITRU_THUNDERSTORM,
ITSC_TRFWOLF:3, ITSC_TRFSHEEP:3, ITSC_TRFSCAVENGER:3, ITSC_LIGHT:3, ITSC_FEAR:3
```

What each group tests: gold and lock picks (trade, chests), torch and joint (VR torch, smoking), food and potions
(eating, mana for spells, speed potion), melee and ranged weapons (VR bow/crossbow, two-handers), armor/amulet/ring
(equipping), runes (casting, throwable spells, charge levels, summons, shrink, teleports, water magic),
transformation and other scrolls.

## 5. Traders for the trade test

Spawn them with `MarvinSpawnNpcSymbol` or walk to them. Some only offer trade after a quest or a condition in
their dialog.

| Game | Symbol | Who |
|---|---|---|
| G2 | `VLK_417_Constantino` | Alchemist, Khorinis (potions, plants) |
| G2 | `VLK_416_Matteo` | General store, Khorinis |
| G2 | `VLK_409_Zuris` | Potion seller, market square |
| G2 | `VLK_413_Bosper` | Bowyer (bows, arrows) |
| G2 | `VLK_412_Harad` | Smith (weapons) |
| G1 | `STT_311_Fisk` | Old Camp market |
| G1 | `STT_329_Dexter` | Old Camp (potions, scrolls) |
| G1 | `ORG_855_Wolf` | New Camp (bows, armor) |
| G1 | `GRD_210_Scatty` | Old Camp arena |
| G1 | `NOV_1357_Fortuno` | Swamp Camp (swampweed) |
| G1 | `KDW_605_Riordian` | Water mage (potions) |

## 6. Logs

- Editor: `%LOCALAPPDATA%/Unity/Editor/Editor.log`, or *Gothic → Debug → Uber Console*. Builds: `Gothic-Unity.log`.
- Useful log prefixes per feature: `[FightService]`, `[AttackPlayAni]` (NPC hits), `[PassivePerc]`, `[HeroPerc]`,
  `[Rooms]` (perceptions), `[StationaryLights]`, `[Sky]` (lights, night), `[VRDocViewer]` (map arrow),
  `[Mobs]` (mob owners), `[MobDialog]` (shrines, beds), `[GetNextTarget]` (NPC targeting).
- Daedalus externals not implemented yet log `Method >X< not yet implemented in DaedalusVM` — worth reporting
  when an AI or quest gets stuck.
