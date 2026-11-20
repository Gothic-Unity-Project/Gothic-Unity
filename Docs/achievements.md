# Achievements

Local achievements for Gothic 1 and Gothic 2, in VR and Flat. No Steam, no server: unlocks are a file on the
player's machine. Decision record: `Docs/adr/2026-11-20-achievements.md`. Official lists (G1 Classic, G2 Complete
Classic) for reference: wiki page "Achievements". We don't use their titles, texts or icons.

## How it works

1. **Definitions** are a JSON file per game: `StreamingAssets/Achievements/g1.json`, `g2.json`. With a mod active
   these are off; a mod may ship `Achievements.json` in its folder, or `system/<ini>.achievements.json` next to its
   ini (same format), and gets its own profile.
2. **Facts** are announced once, where they happen (`GlobalEventDispatcher`): log topic status set
   (`StoryService`), hero's true guild set (`NpcAiService.ExtSetTrueGuild`), dialog info told (`DialogService`),
   NPC killed (`FightService.Kill`: victim + killer; "by hero" is `GetCreditedAttacker(killer).IsHero`), hero's
   items changed (`NpcInventoryService`), lock picked, document shown, Marvin mode changed, hero died.
3. **`AchievementService`** keeps the definitions indexed by the fact that can complete them. A fact only checks
   that small subset. Global variables have no fact: their ~20 conditions are re-checked every 2 s while in a world.
4. **Unlocks** go to `<persistentDataPath>/Achievements/<ConfigService.ModCacheSuffix>.json`, written to a temp
   file and renamed (a frame after the unlock, not in the hit frame), like save games.
5. **Run data** (run id, save number, tier, counters) goes into the save game's `UNITYSAVE.json` as section
   `core.achievements`.

## Definition format

Example (texts and icon are placeholders):

```json
{
  "id": "g1.join_old_camp",
  "title": { "en": "In service to the Ore Barons", "de": "Im Dienste der Erzbarone" },
  "description": { "en": "Join the Old Camp", "de": "Tritt dem Alten Lager bei" },
  "icon": { "item": "ItMw_1H_Sword_Old_01" },
  "hidden": false,
  "condition": { "logTopic": "CH1_JoinOC", "status": "success" }
}
```

Condition kinds (one per definition, `all`/`any` combine):

| Kind | JSON | Checked on |
|---|---|---|
| Log topic status | `{ "logTopic": "CH1_JoinOC", "status": "success" }` (topic = script constant name, resolved to its text) | `Log_SetTopicStatus` |
| Global variable | `{ "variable": "Kapitel", "op": ">=", "value": 3 }` | every 2 s while in a world, only when the value changed since world load |
| True guild | `{ "trueGuild": ["GIL_SLD"] }` | `Npc_SetTrueGuild` on the hero |
| Info told | `{ "infoTold": "DIA_Gilbert_First" }` | dialog info told |
| Item count | `{ "item": "ItMiNugget", "count": 15000 }` | hero items changed |
| Kill | `{ "killed": ["MinecrawlerQueen"], "count": 1, "byHero": true }` | NPC killed |
| Counter | `{ "counter": "lockpick.success", "count": 20 }` | the counter's fact |

Texts are in the file (not in the Unity string table), so a mod can bring its own. Missing language: English.
Icons name an item instance (rendered like the inventory does) or a texture from the installation (`"texture":
"..."`). Nothing of the original games ships with Gothic-Unity.

A definition whose script symbols don't exist (renamed variable, missing info) loads as unavailable and logs one
warning (`LogCat.Achievements`) - it never throws.

## Tiers: Innos, Adanos, Beliar

Every run has a tier, every unlock remembers the tier it was earned in. A later unlock in a better tier upgrades it.

| Tier | Meaning | Lost when |
|---|---|---|
| Innos | no cheats, never went back | loading a save older than the run's newest save, or loading after the hero died since the last save |
| Adanos | normal play with saves and loads | Marvin mode is turned on |
| Beliar | Marvin mode was on in this run | never (for the saves written afterwards) |

- A new game gets a new run id. Every save of that run carries the run id and a rising save number.
- The profile file remembers per run: the newest save number (written after the save succeeded), and whether the
  hero died after it ("spent").
- A save without achievement data (older saves) starts as Adanos with a new run id.
- Loading a save restores its run's tier; then the rule above may lower it to Adanos. Loading a clean save of the
  same run after cheating gives back that save's tier - cheats only taint the saves written while tainted.
- Turning Marvin mode on lowers the running game to Beliar immediately, even if it is turned off again. Marvin mode
  survives loading, so it is checked at every new game and load too, not only when it is toggled.

## Seal: tamper-evident, not tamper-proof

The goal is that a text editor isn't enough to fake an achievement - not real security (the project is open source).

- **What is sealed:** the profile file, and the `core.achievements` section of each save (tier, run id, save
  number, counters). Without the second, deleting the Beliar mark in `UNITYSAVE.json` would give Innos again.
- **How:** HMAC-SHA256 over the content. Key = build key + device id (`SystemInfo.deviceUniqueIdentifier`). The
  save section's seal also covers its run id and save number, so it can't be copied into another save.
- **Build key:** not in the repository. CI writes it from a GitHub secret into a generated file at build time;
  Editor and self-made builds use a fixed dev key and only trust their own files.
- **Stored** as gzip + base64 (not readable at a glance); decoding is a few lines for bug reports.
- **Hash chain:** every unlock carries its run id, save number and the hash of the previous unlock, so inserting
  or reordering entries breaks the chain.
- **Plausibility:** an Innos unlock needs a known run whose save numbers are consistent.
- **Broken seal:** nothing is deleted. Affected unlocks show as "unsealed" (greyed, broken seal icon) and don't
  count for completion; earning them again re-seals them. A save with a broken section plays as Beliar.
- **Cost:** one hash on load and on write, never per frame.

## Our list (proposal, to be curated)

Own titles and wording in English and German, never the official ones (see the ADR, decision 1). The moments are
inspired by the Classic console lists (wiki "Achievements"); the names speak like the colony speaks.
For quest achievements the description may instead be the quest log topic text from the player's own installation,
loaded at runtime.

### Gothic 1 - detectable with the planned facts

| Condition | English | German |
|---|---|---|
| log `CH1_JoinOC` success | **Gomez' Shadow** - Become a Shadow of the Old Camp. | **Gomez' Schatten** - Werde ein Schatten im Alten Lager. |
| log `CH1_JoinPsi` success | **Disciple of the Sleeper** - Become a novice of the Brotherhood in the swamp. | **Jünger des Schläfers** - Werde Novize der Bruderschaft im Sumpf. |
| log `CH1_JoinNC` success | **Lares' Crew** - Become a rogue of the New Camp. | **Lares' Leute** - Werde Bandit im Neuen Lager. |
| true guild `GIL_SLD` while `Kapitel == 1` | **Hired Early** - Join Lee's mercenaries before the first chapter ends. | **Früh angeheuert** - Werde noch im ersten Kapitel Söldner bei Lee. |
| variable `Lares_vorbeigemogelt == 1` | **Past the Gatekeepers** - Get to Lares without being let in. | **Vorbeigemogelt** - Schaffe es zu Lares, ohne dass man dich vorlässt. |
| `Kapitel >= 3` | **The Great Invocation** - Be there when the Brotherhood calls the Sleeper. | **Die große Anrufung** - Sei dabei, wenn die Bruderschaft den Schläfer ruft. |
| log `CH3_OrcGraveyard` success | **Bones and Silence** - Lead Baal Lukor through the orc graveyard and come back alive. | **Knochen und Stille** - Führe Baal Lukor durch den Orkfriedhof und kehre lebend zurück. |
| log `CH3_BringFoci` success | **Five Stones for the Barrier** - Bring every focus stone to the Water Mages. | **Fünf Steine für die Barriere** - Bringe alle Fokussteine zu den Wassermagiern. |
| item `ItArRuneTeleport3` | **Hearts of Stone** - Take the golems' hearts and earn the way to Xardas. | **Herzen aus Stein** - Nimm den Golems die Herzen und verdiene dir den Weg zu Xardas. |
| log `CH4_UluMulu` success | **Friend of the Orcs** - Carry the orcs' sign of friendship. | **Freund der Orks** - Trage das Freundschaftszeichen der Orks. |
| log `CH5_Uriziel` success (first) | **The Blade Awakens** - Give Uriziel back its power. | **Die Klinge erwacht** - Gib Uriziel seine Kraft zurück. |
| log `Baloros_Waffe` success | **Fed Up** - Find out what Baloro's wonder weapon really is. | **Bedient** - Finde heraus, was Baloros Wunderwaffe wirklich ist. |
| infos `DIA_Scatty_KirgoSuccess` + `DIA_Scatty_KHARIMSuccess` | **Pride of the Arena** - Beat Kirgo and Kharim in Scatty's arena. | **Stolz der Arena** - Besiege Kirgo und Kharim in Scattys Arena. |
| info `DIA_GRD_219_Stone4` | **Forged Anew** - Have Stone strengthen your ore armor. | **Neu geschmiedet** - Lass Stone deine Erzrüstung verstärken. |
| variable `Jesse_PayForMe == LOG_SUCCESS` | **Debts Settled** - Pay what Jesse owes. | **Schulden beglichen** - Begleiche Jesses Schulden. |
| info `DIA_Gilbert_First` | **Hidden from Gomez** - Find the man who has hidden from Gomez for years. | **Versteckt vor Gomez** - Finde den Mann, der sich seit Jahren vor Gomez versteckt. |
| info `DIA_Mud_Nerve_19` | **Patience of a Saint** - Let Mud talk until he has nothing new to say. | **Engelsgeduld** - Lass Mud reden, bis ihm nichts Neues mehr einfällt. |
| log `CH1_HoratiosTeachings` success | **Gentle Strength** - Convince Horatio, who swore off fighting, to train you. | **Sanfte Kraft** - Überrede Horatio, der dem Kämpfen abgeschworen hat, dich zu trainieren. |
| item `ItMiNugget` x 15000 | **Worth Your Weight in Ore** - Own 15,000 nuggets of ore. | **Erz wie Heu** - Besitze 15.000 Erzbrocken. |
| item `ItMi_Stuff_Pipe_01` | **Someone Smoked Here** - Find the lost smoking pipe. | **Hier hat jemand geraucht** - Finde die verlorene Pfeife. |
| kill `MinecrawlerQueen` | **Into the Nest** - Kill the minecrawler queen. | **Ins Nest** - Töte die Minecrawler-Königin. |
| kill `Meatbug` x 10 by hero | **Pest Control** - Squash 10 meatbugs. | **Kammerjäger** - Zertritt 10 Fleischwanzen. |
| counter `lockpick.success` x 20 | **Steady Hands** - Pick 20 locks. | **Ruhige Hand** - Knacke 20 Schlösser. |
| 10 different documents shown | **Reader in the Colony** - Read 10 different books or letters. | **Leseratte** - Lies 10 verschiedene Bücher oder Briefe. |

### Gothic 2 - draft, conditions still to be mapped (`daedalus-scout` on the G2 scripts)

| Moment | English | German |
|---|---|---|
| join the militia | **Lord Andre's Man** - Join the city militia. | **Lord Andres Mann** - Tritt der Stadtmiliz bei. |
| become a novice | **Behind Monastery Walls** - Become a novice in the monastery of Innos. | **Hinter Klostermauern** - Werde Novize im Kloster Innos'. |
| become a mercenary | **Onar's Blade** - Become one of Lee's mercenaries on Onar's farm. | **Onars Klinge** - Werde Söldner auf Onars Hof. |
| proof from Garond | **Word from the Valley** - Bring Lord Hagen proof of the dragons. | **Nachricht aus dem Tal** - Bringe Lord Hagen den Beweis für die Drachen. |
| Eye of Innos repaired | **The Eye Made Whole** - Restore the Eye of Innos at the stone circle. | **Das Auge geheilt** - Stelle das Auge Innos' am Steinkreis wieder her. |
| all dragons of the valley | **Four Fewer Dragons** - Slay the dragons in the Valley of Mines. | **Vier Drachen weniger** - Erschlage die Drachen im Minental. |
| sail to Irdorath | **Cast Off** - Take a ship to Irdorath. | **Leinen los** - Segle nach Irdorath. |
| undead dragon | **Beliar's Champion Falls** - Defeat the undead dragon. | **Beliars Streiter fällt** - Besiege den untoten Drachen. |
| enter Jharkendar | **Beyond the Portal** - Step through the portal into Jharkendar. | **Jenseits des Portals** - Betritt Jharkendar durch das Portal. |
| join Greg's pirates | **Greg's Crew** - Join the pirates. | **Gregs Mannschaft** - Heuere bei den Piraten an. |
| defeat Raven | **The Raven Falls** - Stop Raven in his temple. | **Der Rabe fällt** - Halte Raven in seinem Tempel auf. |
| join the thieves' guild | **Down in the Sewers** - Find the thieves' guild beneath Khorinis. | **Unter der Stadt** - Finde die Diebesgilde unter Khorinis. |
| black troll | **The Black Giant** - Defeat the black troll. | **Der schwarze Riese** - Besiege den schwarzen Troll. |
| 50 orcs killed | **Orcbane** - Kill 50 orcs. | **Orkschreck** - Töte 50 Orks. |
| 50 locks picked | **No Chest Is Safe** - Pick 50 locks. | **Keine Truhe ist sicher** - Knacke 50 Schlösser. |
| strength from apples | **Fruit of Strength** - Grow stronger by eating apples. | **Kraftfutter** - Werde stärker durch Äpfel. |

### Ideas that need an engine fact first (later PRs)

| Moment | English | German |
|---|---|---|
| G1 ending (Sleeper banished) | **The Sleeper Sleeps Again** - Banish the Sleeper. | **Der Schläfer schläft wieder** - Verbanne den Schläfer. |
| enemy dies of fall damage | **Watch Your Step** - Let an enemy fall to his death. | **Pass auf, wo du hintrittst** - Lass einen Gegner in den Tod stürzen. |
| kill with your bare fists (VR: real punches) | **Bare Knuckles** - Knock down 10 enemies with your bare fists. | **Blanke Fäuste** - Strecke 10 Gegner mit bloßen Fäusten nieder. |
| pickpocketing | **Quick Fingers** - Pick 25 pockets. | **Flinke Finger** - Bestiehl 25 Leute. |
| hide in a barrel | **Herring in a Barrel** - Hide in a barrel. | **Hering im Fass** - Versteck dich in einem Fass. |
| forge a sword | **Smith's Apprentice** - Forge your first sword. | **Schmiedelehrling** - Schmiede dein erstes Schwert. |
| finish without sleeping | **Sleep Is for the Dead** - Finish the game without ever sleeping. | **Schlafen kann ich, wenn ich tot bin** - Beende das Spiel, ohne je zu schlafen. |
| kill an orc dog while transformed into one | **Dog Eat Dog** - As an orc dog, kill an orc dog. | **Hund frisst Hund** - Töte als Orkhund einen Orkhund. |

## Careful about

- **Scripts first.** A condition reads script state or a fact the engine announces; it never re-implements a quest
  rule in C#. The Classic list was made for the original scripts, so most entries map to one log topic, variable,
  guild or info.
- **Same topic, two achievements.** `CH5_Uriziel` succeeds twice (charged, then turned into a rune): use the
  told info for the second one.
- **Kill facts.** Raised once in `FightService.Kill()`, never in `NpcDeathDomain.RunZsDead` (that also runs for
  corpses restored from a save). Falls, Marvin's kill and drowning all have the victim as its own killer, so a
  fall-damage achievement needs a death cause first.
- **Defeat vs. kill.** Arena fights knock out, they don't kill: use the scripts' `AIV_WASDEFEATEDBYSC` flags or the
  told infos.
- **Chapter 4 resets the guild** to `GIL_NONE`: guild achievements react to `Npc_SetTrueGuild`, not to the current
  guild.
- **Similar items.** `ItMi_Stuff_OldCoin_01` vs `_02`, `ItMi_Stuff_Pipe_01` vs the blue/yellow pipes.
- **Loading is not playing.** Restoring a save or entering a world runs scripts that set log topics, variables and
  items. Facts are ignored from `LoadGameStart` / `LoadingSceneLoaded` until `WorldSceneLoaded`. Variables are
  snapshotted then, so a save from before achievements existed unlocks nothing by itself (retroactive unlocks are
  an open decision).
- **Info told comes before the info runs.** Variables the dialog sets are seen by the next 2 s check.
- **Mods.** Base lists are off with a mod; symbol validation catches renamed scripts in a mod's own file.
- **Cost on Quest 2.** No per-frame work. A fact checks only its indexed definitions; variable reads are a handful
  of symbol lookups; the profile file is written only on an unlock.
- **Not detectable yet** (need an engine fact first): pickpocketing (engine-only in G1), hiding in a barrel,
  forging at the anvil, kill while transformed, kill with the victim's own weapon, control/rage spell kills, shrink.

## Where the code goes

| Part | Place |
|---|---|
| definitions, run state, profile data | `Gothic-Core/Scripts/Models/Achievements/` |
| loading, condition checks, profile file | `Gothic-Core/Scripts/Domain/Achievements/` |
| save section `core.achievements` | `Gothic-Core/Scripts/Domain/SaveGames/` |
| `AchievementService` | `Gothic-Core/Scripts/Services/Achievements/`, ProjectScope |
| seal (sign, verify, encode) | `Gothic-Core/Scripts/Domain/Achievements/AchievementSealDomain.cs`; build key file generated by CI |
| toast, menu page | `Gothic-VR` and `Gothic-Flat` adapters; icons textures-only at first (no render-to-texture for items exists yet) |
| definition files | `Assets/StreamingAssets/Achievements/` |
