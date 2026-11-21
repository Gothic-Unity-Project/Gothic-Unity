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
   items changed (`NpcInventoryService`), lock picked (`LockPickComboFinished` with a lock pick as the tool - it
   also fires for keys), document shown (`DocShow`, key `DocModel.Identity`: the item instance, or a hash of a mob
   page's textures and text), Marvin mode changed, hero died.
3. **`AchievementService`** keeps the definitions indexed by the fact that can complete them. A fact only checks
   that small subset. Global variables have no fact: their ~20 conditions are re-checked right after the script
   moments that change story variables. No polling, no tick. The moments are facts raised where our code calls the
   script, after it returned:
   - `DialogInfoFinished` - a dialog info's or choice's function (`DialogService.CallInformation`),
   - `NpcDeathScriptsFinished` - `ZS_Dead` + `ZS_Dead_Loop` (`NpcDeathDomain.RunZsDead`; G2's dragon counter sets
     `MIS_AllDragonsDead`, `UndeadDragonIsDead`, `RavenIsDead` there),
   - `ItemFunctionFinished` - an item's `on_state[0]`, `on_equip`, `on_unequip` (`ItemUseService.CallItemFunction`,
     also for `AI_UseItem`),
   - `MobStateFunctionFinished` - a mob's `[onStateFunc]_S1` (`MobInteractionService`, VR dig spots),
   - `TriggerScriptFinished` - an `oCTriggerScript`'s function (`TriggerScriptHandler`),
   - `ChapterIntroduced` - the `IntroduceChapter` external (`StoryService`).

   A moment only marks the variables dirty (`AchievementVariableWatchDomain`); one check runs a frame later
   (`UnityMonoService` coroutine), however many moments came in that frame, and after the calling script has
   finished. Nothing is dirty, nothing runs: zero per-frame cost. Fallback checks (`AchievementService.RecheckNow()`)
   catch a variable set by a script we don't hook: the VR help panel opens (`VRHelpTabs`), the achievements page
   opens, and right before a save is captured (`CaptureRun`). Flat has no help panel yet.
   `AchievementService.Init()` subscribes to the facts and moments and a teardown removes the listeners; facts from
   per-world services reach the ProjectScope service only through `GlobalEventDispatcher`.
4. **Unlocks** go to the profile `<persistentDataPath>/Achievements/<GameVersion><ModCacheSuffix>.json`, e.g.
   `Gothic1.json`, `Gothic2_mod_DM_E.json`. Same key as `StaticCacheService` (`ContextGameVersionService.Version` +
   `ConfigService.ModCacheSuffix`, the suffix is empty for vanilla), so G1 and G2 never share a file and a mod's
   "own profile" is this key too. One profile writer (`Domain/Achievements/`) writes it to a temp file and renames
   it, a frame after the change (not in the hit or death frame), like save games.
5. **Run data** (run id, save number, tier, counters) goes into the save game's `UNITYSAVE.json` as section
   `core.achievements` (`AchievementRunSaveSection`). The section only captures and restores; `AchievementService`
   owns the run state and decides the run on the first `WorldSceneLoaded` after `LoadGameStart` (new game, the
   restored section, or none). It is registered in `SaveGameService.Init()` like Core's other sections, so
   `Reset()` runs on a new game and before every load.
6. **Save committed:** `SaveGameService.SaveCurrentGame()` calls `AchievementService.OnSaveCommitted()` after the
   slot folder was replaced successfully; the service commits the run id and save number its `Capture()` wrote.
   `Capture()` runs before success is known, so a failed save never counts as the run's newest.
7. **Unlock event:** `GlobalEventDispatcher.AchievementUnlocked(definition, tier)`, raised only by
   `AchievementService`, also when a tier is upgraded. The toast and the menu page listen to it and read the shared
   read model; nobody polls the service.
8. **Switch:** `GameSettings.json` `"AchievementsEnabled"` (default `true`). Off: no facts are checked,
   nothing is written to the profile or the save section, no pop-ups. Not a `DeveloperConfig` field (Editor only).
   The wiki Player Guide ("Configuration") needs it once it is built.

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
| Global variable | `{ "variable": "Kapitel", "op": ">=", "value": 3 }` or `"value": "LOG_SUCCESS"` | a frame after a script moment (dialog info, death, item, mob, trigger, chapter) and the fallbacks (help panel, achievements page, save), only when the value differs from the snapshot |
| True guild | `{ "trueGuild": ["GIL_SLD"] }` | `Npc_SetTrueGuild` on the hero |
| Info told | `{ "infoTold": "DIA_Gilbert_First" }` | dialog info told |
| Item count | `{ "item": "ItMiNugget", "count": 15000 }` | hero items changed |
| Kill | `{ "killed": ["MinecrawlerQueen"], "count": 1, "byHero": true }` | NPC killed |
| Counter | `{ "counter": "lockpick.success", "count": 20 }` | `AchievementService.Increment(counterId)` |
| Distinct | `{ "distinct": "document.shown", "count": 10 }` (N different X) | `AchievementService.AddDistinct(counterId, key)` |
| NPC aivar | `{ "npcAivar": "GRD_251_Kirgo", "aivar": "AIV_WASDEFEATEDBYSC", "value": 1 }` | never alone: read at the moment of the other fact in its `all` |

Texts are in the file (not in the Unity string table), so a mod can bring its own. Missing language: English.
Icons name a texture from the installation (`"texture": "..."`, works from day one) or an item instance
(`"item": "..."`). There is no render-to-texture for items yet, so an `item` icon falls back to the definition's
texture or a placeholder until it exists. Nothing of the original games ships with Gothic-Unity.

Items, NPCs and kills are keyed by script instance names (`ItMiNugget`, `MinecrawlerQueen`), never by display
names (`ItemInstance.Name`). The item fact is hero-only and reads the hero's packed inventory
(`NpcInventoryService`, `GetPacked().UnpackItems()`): an NPC holding the same item (e.g. `ItArRuneTeleport3` in
`PC_Rockefeller.d`) doesn't count. An NPC aivar is read from the NPC found by its instance name; no such NPC in the
world means the condition is false.

Counters are public: `AchievementService.Increment(counterId)` (and `AddDistinct(counterId, key)` for "N different
X", e.g. document instance names) is called by Core services and by modules (VR, Flat, mods), so a new counter needs
no switch in Core. Core's counter ids are constants in one file (`Models/Achievements/AchievementCounterIds.cs`);
modules prefix their own (`vr.*`, `{modId}.*`). Counter values and distinct keys are run data.

A log topic constant is resolved to its text through the VM symbol (ZenKit decodes it with the installation's code
page), the same string path the quest log gets from `Log_SetTopicStatus`, and compared to the text the scripts
produce - never to hand-typed text. Umlauts in script strings broke before (#526).

Variables are int script variables only (float and string variables aren't supported). `value` is an int literal
or the name of a script constant, resolved through the VM symbol table when the definitions load.

A fact handler reads the variables its definitions reference synchronously, at the moment of the fact: "Hired
Early" reads `Kapitel` inside the guild fact, so it can't see a later chapter. The variable check after script
moments only serves definitions that are a pure variable condition.

A definition whose script symbols don't exist (renamed variable, missing constant, missing info) loads as
unavailable and logs one warning (`LogCat.Achievements`) - it never throws.

## Tiers: Innos, Adanos, Beliar

Every run has a tier, every unlock remembers the tier it was earned in. A later unlock in a better tier upgrades it.

| Tier | Meaning | Lost when |
|---|---|---|
| Innos | no cheats, never went back | loading a save older than the run's newest save, or loading after the hero died since the last save |
| Adanos | normal play with saves and loads | Marvin mode is turned on |
| Beliar | Marvin mode was on in this run | never (for the saves written afterwards) |

- A new game gets a new run id. The run has a save counter that rises by one on every successful save, and each
  save carries the run id and its number. Every save counts: manual slots (today slots 1-15), and quick and auto
  saves once they exist. Overwriting a slot just writes a higher number.
- The profile remembers per run the newest committed save number (`OnSaveCommitted`) and whether the hero died
  after it ("spent").
- Loading a save with a lower number than the run's newest means the player went back: Adanos. A copied save keeps
  its run id and number, so the same rule applies. Deleting saves never raises a tier and never breaks the check:
  the profile keeps the newest number even when that save is gone.
- The profile decides when save and profile disagree: a run the profile doesn't know, or a save number above the
  profile's newest, plays as Adanos.
- "Spent" is recorded on `HeroDied` through the same profile writer as unlocks (a frame later, before the death
  screen can be left). Pending writes are flushed when the game quits or is paused (headset taken off). A crash in
  that one frame loses the mark or an unlock written in it - accepted.
- A save without achievement data (older saves) starts as Adanos with a new run id.
- Loading a save restores its run's tier; then the rule above may lower it to Adanos. Loading a clean save of the
  same run after cheating gives back that save's tier - cheats only taint the saves written while tainted.
- Turning Marvin mode on lowers the running game to Beliar immediately, even if it is turned off again. Marvin mode
  survives loading, so it is checked at every new game and load too, not only when it is toggled.

## Seal: tamper-evident, not tamper-proof

The goal is that a text editor isn't enough to fake an achievement - not real security (the project is open source).

- **What is sealed:** the profile file, and the `core.achievements` section of each save (tier, run id, save
  number, counters). Without the second, deleting the Beliar mark in `UNITYSAVE.json` would give Innos again.
- **How:** HMAC-SHA256 over the content. Key = build key + device id. The save section's seal also covers its run
  id and save number, and the save's own SAVEINFO date and play time, so it can't be copied into another save.
- **Device id:** a random id generated once and stored in the persistent data folder
  (`<persistentDataPath>/Achievements/device.id`), not `SystemInfo.deviceUniqueIdentifier` - that one can be
  `unsupportedIdentifier` or change after an OS update on Quest/Pico. Only deleting the app data or a new device
  makes a new id.
- **Build key:** a dev key file is committed (`Gothic-Core/Resources/Achievements/AchievementsSealKey.txt`, a
  `TextAsset`); CI overwrites it from the GitHub secret `ACHIEVEMENTS_SEAL_KEY` before the build (no secret, e.g.
  forks: the dev key stays). Editor and self-made builds keep the dev key and only trust their own files. No `#if`
  and no git-ignored code file, so every checkout compiles.
- **Who does what:** `AchievementService` reads the build key and the device id and passes the bytes to
  `AchievementSealDomain`, which is pure (no `SystemInfo`, no file access) and EditMode-testable.
- **Stored** as gzip + base64 (not readable at a glance); decoding is a few lines for bug reports.
- **Hash chain:** every unlock carries its run id, save number and the hash of the previous sealed unlock, so
  inserting or reordering entries breaks the chain. Unsealed entries stay outside the chain (no hash) until earned
  again; the chain is renewed over the sealed entries on every write.
- **Plausibility:** an Innos unlock needs a known run whose save numbers are consistent.
- **Broken seal:** nothing is deleted. Affected unlocks show as "unsealed" (greyed, broken seal icon) and don't
  count for completion; earning them again re-seals them. A broken profile seal also forgets the profile's runs
  (their saves play as Adanos). A save with a broken section plays as Beliar.
- **Cost:** the seal is verified once when the profile or a save is loaded and the result is cached; one hash per
  write, never per frame.

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
| all: infos `DIA_Scatty_KirgoSuccess` + `DIA_Scatty_KHARIMSuccess`, `AIV_WASDEFEATEDBYSC` of `GRD_251_Kirgo` + `SLD_729_Kharim` | **Pride of the Arena** - Beat Kirgo and Kharim in Scatty's arena. | **Stolz der Arena** - Besiege Kirgo und Kharim in Scattys Arena. |
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
| distinct `document.shown` x 10 | **Reader in the Colony** - Read 10 different books or letters. | **Leseratte** - Lies 10 verschiedene Bücher oder Briefe. |

### Gothic 2 - conditions verified against the G2 NotR 2.6 scripts

| Moment | Condition | English | German |
|---|---|---|---|
| join the militia | trueGuild `GIL_MIL` | **Lord Andre's Man** - Join the city militia. | **Lord Andres Mann** - Tritt der Stadtmiliz bei. |
| become a novice | trueGuild `GIL_NOV` | **Behind Monastery Walls** - Become a novice in the monastery of Innos. | **Hinter Klostermauern** - Werde Novize im Kloster Innos'. |
| become a mercenary | trueGuild `GIL_SLD` | **Onar's Blade** - Become one of Lee's mercenaries on Onar's farm. | **Onars Klinge** - Werde Söldner auf Onars Hof. |
| proof from Garond | info `DIA_Garond_Success` | **Word from the Valley** - Bring Lord Hagen proof of the dragons. | **Nachricht aus dem Tal** - Bringe Lord Hagen den Beweis für die Drachen. |
| Eye of Innos repaired | variable `MIS_RitualInnosEyeRepair` == `LOG_SUCCESS` | **The Eye Made Whole** - Restore the Eye of Innos at the stone circle. | **Das Auge geheilt** - Stelle das Auge Innos' am Steinkreis wieder her. |
| all dragons of the valley | variable `MIS_AllDragonsDead` == 1 | **Four Fewer Dragons** - Slay the dragons in the Valley of Mines. | **Vier Drachen weniger** - Erschlage die Drachen im Minental. |
| sail to Irdorath | variable `Kapitel` >= 6 | **Cast Off** - Take a ship to Irdorath. | **Leinen los** - Segle nach Irdorath. |
| undead dragon | variable `UndeadDragonIsDead` == 1 | **Beliar's Champion Falls** - Defeat the undead dragon. | **Beliars Streiter fällt** - Besiege den untoten Drachen. |
| enter Jharkendar | variable `ENTERED_ADDONWORLD` == 1 | **Beyond the Portal** - Step through the portal into Jharkendar. | **Jenseits des Portals** - Betritt Jharkendar durch das Portal. |
| join Greg's pirates | trueGuild `GIL_PIR` | **Greg's Crew** - Join the pirates. | **Gregs Mannschaft** - Heuere bei den Piraten an. |
| defeat Raven | variable `RavenIsDead` == 1 | **The Raven Falls** - Stop Raven in his temple. | **Der Rabe fällt** - Halte Raven in seinem Tempel auf. |
| join the thieves' guild | info `DIA_Cassia_Aufnahme` | **Down in the Sewers** - Find the thieves' guild beneath Khorinis. | **Unter der Stadt** - Finde die Diebesgilde unter Khorinis. |
| black troll | kill `Troll_Black` by hero | **The Black Giant** - Defeat the black troll. | **Der schwarze Riese** - Besiege den schwarzen Troll. |
| 50 orcs killed | later: needs a kill-by-guild kind (orcs) | **Orcbane** - Kill 50 orcs. | **Orkschreck** - Töte 50 Orks. |
| 50 locks picked | counter `lockpick.success` x 50 | **No Chest Is Safe** - Pick 50 locks. | **Keine Truhe ist sicher** - Knacke 50 Schlösser. |
| strength from apples | later: needs a counter on apple use | **Fruit of Strength** - Grow stronger by eating apples. | **Kraftfutter** - Werde stärker durch Äpfel. |
| Eye of Innos found | item `ItMi_InnosEye_Broken_MIS` | **The Eye Found** - Recover the Eye of Innos. | **Das Auge gefunden** - Bringe das Auge Innos' zurück. |
| 20,000 gold | item `ItMi_Gold` x 20000 | **A Rich Man** - Own 20,000 gold. | **Ein reicher Mann** - Besitze 20.000 Gold. |
| sausages handed out | variable `Wurst_Gegeben` >= 13 | **Sausage for Everyone** - Hand every novice a sausage, missing nobody. | **Wurst für alle** - Gib jedem Novizen eine Wurst, ohne einen auszulassen. |

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
- **Defeat vs. kill.** Arena fights knock out, they don't kill: use the scripts' `AIV_WASDEFEATEDBYSC` flags.
- **Told is not won.** `DIA_Scatty_KirgoSuccess` / `_KHARIMSuccess` are told after a lost fight too
  (`DIA_GRD_210_Scatty.d`, "Und du hast dich besiegen lassen!"), so the told infos need the defeat flags in `all`.
- **Chapter 4 resets the guild** to `GIL_NONE`: guild achievements react to `Npc_SetTrueGuild`, not to the current
  guild.
- **Similar items.** `ItMi_Stuff_OldCoin_01` vs `_02`, `ItMi_Stuff_Pipe_01` vs the blue/yellow pipes.
- **Loading is not playing.** Restoring a save or entering a world runs scripts that set log topics, variables and
  items. Facts are ignored from `LoadGameStart` / `LoadingSceneLoaded` until the world is ready: `WorldScene` calls
  `AchievementService.OnWorldReady()` after the loading screen is hidden. That is after all
  `WorldSceneLoaded` listeners (e.g. `ScriptVarsSaveSection`, `MobStatesSaveSection` restore state there, in
  registration order), the `STARTUP_*` / `INIT_*` scripts and the surroundings build. The gate never opens inside
  `WorldSceneLoaded`. NPCs built lazily afterwards run their instance scripts on themselves; the hero-only item
  fact ignores them.
- **Loading a save vs. entering a world.** A new game or a loaded save (`LoadGameStart`, a fresh VM) takes the
  variable snapshot when its world is ready, so loading unlocks nothing by itself - no retroactive unlocks (ADR,
  decision 8). A level change within the running game keeps the snapshot from before it; when the new world is
  ready (after its `STARTUP_*` / `INIT_*` scripts), one check runs against that old snapshot, so values the new
  world's scripts set count (G2: `Kapitel` 6 set by Irdorath's startup script, `ENTERED_ADDONWORLD` entering
  Jharkendar). Facts while loading stay ignored; only this one variable comparison happens at world ready.
- **Info told comes before the info runs.** `DialogInfoTold` fires before the info function, so it can't see the
  variables the info sets; `DialogInfoFinished` (after the function) is the variable moment.
- **A script moment we don't hook** (e.g. a perception or daily-routine state setting a story variable) is seen at
  the next hooked moment or at a fallback check (help panel, achievements page, save) - never lost, maybe late.
- **Mods.** Base lists are off with a mod; symbol validation catches renamed scripts in a mod's own file.
- **Cost on Quest 2.** No per-frame work. A fact checks only its indexed definitions; variable reads are a handful
  of symbol lookups at most once per frame, and only in a frame after a script moment; the profile file is written
  only on an unlock, a committed save or a death.
- **Logging.** `LogCat.Achievements`, one-shot only: one warning per unavailable definition, one summary line per
  load for the facts ignored while loading, one warning per broken seal. Nothing per check or per fact.
- **Not detectable yet** (need an engine fact first): pickpocketing (engine-only in G1), hiding in a barrel,
  forging at the anvil, kill while transformed, kill with the victim's own weapon, control/rage spell kills, shrink.

## Where the code goes

| Part | Place |
|---|---|
| definitions, run state, profile data, the read model VR and Flat UI share (entry, best tier, sealed) | `Gothic-Core/Scripts/Models/Achievements/` |
| loading, condition checks, profile file | `Gothic-Core/Scripts/Domain/Achievements/` |
| save section `core.achievements` | `Gothic-Core/Scripts/Domain/SaveGames/` |
| `AchievementService` | `Gothic-Core/Scripts/Services/Achievements/`, ProjectScope |
| read model for the UI | `AchievementService.GetEntries()` (`AchievementEntry` list), `GetCompletion()`, `CurrentTier`; built by `AchievementReadModelDomain` |
| seal (sign, verify, encode) | `Gothic-Core/Scripts/Domain/Achievements/AchievementSealDomain.cs`; committed dev key file, overwritten by CI before the build |
| toast, menu page | one prefab each in `Gothic-Core/Resources/Prefabs/` (`PrefabType` + `ResourceCacheService`), adapters in Core; a VR Prefab Variant only where the placement differs |
| `LogCat.Achievements` | `Gothic-Core/Scripts/Logging/LogCat.cs` |
| definition files | `Assets/StreamingAssets/Achievements/` |
