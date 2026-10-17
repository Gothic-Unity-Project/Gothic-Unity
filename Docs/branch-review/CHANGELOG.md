# Changelog: experimental build

Branch `experimental/game-completion-candidate-vibecoded-borobongo` (BoroBongo's experimental build). Newest first.
Each entry lists what players notice; the technical details and commits are on the [theme pages](README.md).

Almost everything is behind a `DeveloperConfig` flag and on in `Production.asset`. Tested on PCVR (SteamVR / Steam
Link), not on Quest/Pico standalone.

---

## In testing (not in the build yet)

- The backpack vacuum takes a dropped rune again.
- A light flick throws a spell weakly, only a real swing throws it hard.

## 2026-10-17 (midnight): main merged, knocked out NPCs, anvils

- `main`'s new dialogs and UI are in (see [the merge notes](merge-2026-10-17.md)): walking away from a dialog slows
  you down at the edge and ends it; the dialog box, subtitles, menu and screen messages are drawn over walls and
  follow your head turns; the menu is 1.5 m in front of you instead of 5 m (the stick zoom is gone with it).
- A knocked out NPC stays down until he stands up - before he drew his weapon, fled or joined fights while lying.
- The NPC who beats you says "Try that again and you'll regret it" once, not twice, then takes your ore.
- Mroczne Tajemnice's anvils open their smithing dialog again (with the sledgehammer in the backpack). Swinging a
  sword anywhere in the Old Camp no longer "hit" an anvil and made you invisible to every NPC: guards ran up, said
  their line and stood still, nobody cared about drawn weapons.

## 2026-10-17 (night): dialogs that end in a fight, items moved by hand

- An NPC who attacks you at the end of a dialog says all its lines first (and you yours), then attacks - the dragon
  turns into Algirion after the talk too. After such a dialog everybody talked to you and fought you again; before
  you stayed "invincible": nobody talked to you and attacked NPCs didn't fight back.
- Items from the world moved by hand (an arrow in the ground put aside) are saved where they lie.

## 2026-10-17 (late): new game at the start, NPCs on scaffolds

- A new game after loading a save from another world (Old Mine, Mroczne Tajemnice's temple) starts next to Diego again,
  not in the middle of the Old Camp.
- After loading, NPCs on scaffolds (Old Mine miners) stay on them instead of falling down.

## 2026-10-17 (evening): miners dig

- Miners in the mines dig: the pickaxe hits with a sound and ore splinters fly (they stood frozen in front of the vein).
- Miners spawned next to each other spread to different ore veins instead of hacking at the same one; NPCs don't
  share a mob anymore unless it has room for more (a bench with two seats).

## 2026-10-17: items, chests and loot are saved

- Taken items stay taken after loading (a beer in the backpack was lying in the world again), also eaten right from
  the ground. Dropped items, NPC drops and arrows lie where they were, stacks keep what's left.
- Chests keep what you took out and what you put in.
- A looted NPC stays looted.
- Saving puts what your hands hold into the backpack.
- All of it per world: a mushroom eaten in the Old Mine is still gone after going out and in again.

## 2026-10-16 (late): Sheer'Ghar's duel, mod subtitles without VDFS

- An NPC who beats you in a duel knows it won - Mroczne Tajemnice's Sheer'Ghar talks about honor afterwards and the
  Seven Steps path goes on.
- Mod subtitles are read straight from the mod's archives - no more extracting `OU.BIN` with VDFS.

## 2026-10-16 (night): main merged - Marvin console, Marvin mode for everyone

- Marvin mode can be switched on in game by everyone, like in Gothic: open the menu, type `marvin` (or click the
  Gothic-Unity logo 3 times), close the menu - "testmode on". `42` (or the 3 clicks again) switches it off.
- The Marvin tab is a command console like Gothic's F2 (on the on-screen keyboard, with completions): `goto waypoint`,
  `insert <item or NPC> [amount]`, `set time`, `cheat full/level/guild`, and ours: `cheat god`, `goto forward` (4 m
  through walls), `team irdorath`, `team helpers` (SH/CH).
- Saving asks for the name on the new on-screen keyboard; the slot shows what you type.
- Scripts' screen messages from Marvin show in Gothic's font in front of you.
- Mods: a mod installed into the Gothic folder (only its ini) counts as a mod too; caches are kept per mod folder (the
  first start of each game/mod rebuilds them once). A mod's ini can turn music off (`unitySkipMusicPlayback=1`) or set
  the language (`unityFallbackLanguage`).

## 2026-10-16: Mroczne Tajemnice's ancient temple and the Ancients

- Mroczne Tajemnice: the abandoned mine's portal takes you to the ancient temple (the Guardians quest) - ZenKit
  couldn't read the world because of a header bug, we fix the header before loading.
- The way back from the temple works: the loading screen no longer hangs when a portal's arrival spot is missing in
  the world (the mod's temple portal) - you arrive next to the portal back.
- The Black Dragon turns into Algirion after the talk instead of vanishing - the Ancients' quest goes on.

## 2026-10-15 (evening): dodge spells, skin colored hands, no crash by Fellan's armor

- NPC spells can be dodged with a step aside: they follow you only a little and fly on when they miss (a crawler's
  web, a fireball).
- VR hands keep a skin color like the hero's arm and look matte; light and time of day tint them only a little (they
  turned navy, light blue or red).
- Mroczne Tajemnice: no crash when the giant skeleton picks up a sword by Fellan's armor in the abandoned mine.

## 2026-10-15: caves and mines like in Gothic, Mroczne Tajemnice textures and crawlers

- Caves, mines and houses are lit like in Gothic: their lightmaps are drawn, torches and fires light the walls, the
  mines have no sun and no fog, stones, scaffolds and stairs inside are as dark as the walls next to them.
- A new Immersion setting "Light refresh" (Low/Medium/High): the torch's light follows your hand smoothly.
- Mroczne Tajemnice: the abandoned mine glows blue/teal instead of being pitch black; Siekacz's tavern, the catapult,
  roofs and other mod objects have their textures (no more stone patterns and black parts).
- Mroczne Tajemnice's crawlers cast their web from afar and bite when you're frozen or close, a frozen NPC is no
  longer treated as knocked out.
- Falling out of the world (Marvin) brings you back to the last spot you stood on.

**Known:** the abandoned mine's portal to the ancient temple (the Guardians quest) can't be used yet - ZenKit can't
load `ANCIENTTEMPLE.ZEN` (a native parser error).

## 2026-10-13 (night): visible NPC spells, sleep on you, save names, menu zoom

- NPC spells are visible: a fireball or an ice block flies from the caster's hand to you and hits when it arrives.
- Sleep and freeze work on you too: you can't move for a while, a hit wakes you up, and the caster stops re-casting.
  G2 Xardas' sleep no longer hurts.
- Fireballs come again when you squeeze grip + trigger to throw (the throwing hand grabbed the rune), and fly at half
  the speed.
- Fists stay closed over NPCs and items further away.
- Name your saves on an on-screen keyboard in Gothic's font (the key under the laser lights up).
- Pull the menu closer or push it away with the right stick - the save list is readable now.

## 2026-10-13 (evening): thrown spells like a ball, fists you hear, Marvin teams

- Thrown spells (fireball, ...) fly where you throw them, as fast as you swing: a weak throw arcs down, letting go
  without a swing drops the spell, only a real throw homes on a target.
- Fists make the fist swing sound and vibrate; a punch vibrates harder.
- Marvin: "Irdo team" spawns the whole Irdorath crew including Torlof, "Cheat friends" spawns the scripts' test helpers.

## 2026-10-13: harmless spells stay harmless, guides wait

- Sleep, light, charm and the other spells that aren't attacks no longer hurt their target (only "bad" spells deal
  damage, like in the original).
- Guides wait for you again (Baal Parvez, Lares, Jack Alligator) instead of running off to the destination.
- No more short freeze when a guide walks on after waiting (faster path finding for all NPCs).
- Guides fight the monsters on the way (Baal Parvez attacks a lurker himself).
- Fireplaces have their fire and smoke again; no smoke column at the Old Camp's gate.
- Torches and fireplaces burn at night and are out by day like in Gothic, always burn in mines and houses, and can be
  lit or put out by touching them; far away fires no longer smoke.
- Rain falls around you instead of somewhere in the middle of the world.

## 2026-10-12: smithing, bookstands, VR fists, music zones, saves keep more

**Fighting**
- **VR fists:** grip + trigger on an empty hand makes a fist; swing it into an NPC to punch (fist damage like Gothic).
- Knocked out NPCs fall down and lie instead of standing or T-posing.
- Monsters and NPCs fight in a normal rhythm - no more lurker biting 4 times a second (combos end like in the original).
- Your summons fight for you: their wins count as yours (Buster's duel), guards blame you for their hits, and they
  stop hitting someone who's down.
- Knocked out you can still turn with the right stick.
- Talking works again: NPCs no longer stay out of the hands' reach.

**Magic**
- Charged spells can't be spammed: releasing early makes a fireball weaker, fire rain or a summon fizzles out.
- Aiming a summon at someone no longer hurts them or makes them hostile.

**World and crafting**
- **Smithing:** the forge heats raw steel, the anvil forges named weapons.
- **Bookstands** show their book above them; touch again or walk away to close.
- NPCs saw wood, turn the roast spit and use their work spots instead of standing next to them.
- Walk through bushes and leaves, bump into tree trunks.
- Active teleports glow, cauldrons bubble; palms and fishing nets have the right textures (G2 addon, Mroczne Tajemnice).
- Mroczne Tajemnice: the smugglers' beach smithy is complete.

**Music**
- Music follows where you are, like the original (pirate camp, cities, night themes); fight music ends with the fight.
- Fixed a crash when the music changed (leaving the pirate camp).

**Saves**
- Activated teleports, the Irdorath bridge progress, NPCs spawned during the game and the NPCs of every visited world
  survive saving, loading and travelling between worlds.

**Loot**
- **Quick loot:** a green X in a corpse's or mummy's loot takes everything at once.

## 2026-10-11: G2 can be finished, movers survive saves

**Gothic 2: Irdorath and the ending**
- The whole Irdorath island works: shooting the bridge targets, the gate levers and wheels, the touchplate puzzle rooms,
  the four pillars, the main door's lock and the last door behind the Innos statue.
- Shootable switches that are invisible get a **green X** where to aim; arrows, bolts and spells home in on them.
- The touchplates (stone buttons) can be highlighted and pressed.
- Code puzzles work like in the original: a wrong order is judged after all presses and triggers the "wrong" event.
- Dragons talk: their lines are heard with subtitles, also from far away (they're huge).
- The undead dragon's death plays its video and teleports you on; after the ending's videos and credits the game goes
  back to the main menu instead of closing.
- Treasure X marks are visible in builds again.

**Movers and the world**
- Movers (gates, doors, bridges, wheels) move through all their keyframes at the original speed.
- Nested movers move together (the Jharkendar portal opens fully).
- Levers that need an item (the portal ring) refuse without it ("I'm missing something"); letters you must read first
  work too (Irdorath's last door).
- **Saves keep the world:** opened gates, doors, pillars and portals stay open after loading and when you come back to
  a world; one-time switches stay used. Before, loading closed everything again and could lock you in.
- Loading a save while playing no longer breaks levers and wheels until a restart.

**Video and stream**
- Videos play in both eyes and show on the monitor/stream too.

**Marvin (cheat panel)**
- God mode, "Friends" (spawns e.g. Diego, Gorn, Milten and Lester), the panel scrolls when it has many options.

## 2026-10-10: NPCs sense and chase like in Gothic, fire, hit numbers

**NPCs and monsters**
- Senses like the original: NPCs see only in front of them, hear you only when you walk or run, smell you up close.
  Sneaking behind an orc works.
- Orcs threaten before they chase you; chasing guards and orcs give up and go back to their routine.
- Orcs are friends with each other, monster packs don't fight among themselves; attack one orc and its friends come
  for you. The Ulu-Mulu makes orcs tolerate you, in the hand or at the belt.
- NPCs and monsters strike out of the run; archers and crossbowmen shoot once per attack and give up when you're out of
  range; NPC mages cast single spells.
- Sleeping resets NPCs to their routine for the new time.
- NPC bodies keep 1 m away from your head, attacks no longer push your hands.
- The G2 wisp amulet works (the wisp finds items).

**Combat and magic**
- Fire spells set NPCs on fire (flames on the whole body, burn dance); the fire storm burns everyone around the impact.
- Every hit does at least the game's minimum damage.
- Arrows, bolts and spells trigger switches (Sleeper's temple).

**UI and options**
- Hit numbers (`-X HP` over heads), HP/MAX and MANA/MAX on bars (Immersion settings).
- Health/mana bars, messages and menus draw on top of the world, your hands on top of them.
- Game profiles: pick G1, G2 or a mod when the game starts. "Restart game" in the settings.
- Marvin can be switched on/off in the settings; "TP +4 m" (Gothic's Marvin K) with fall rescue.

**Loot**
- Orc mummies (containers without a lid) can be looted like corpses.

## Up to 2026-10-09: the base

The branch started from `main` with these big blocks (details per theme in the [overview](README.md)):
- **Combat:** VR bows with a real string, crossbows, spells charged in the free hand and thrown; NPC melee with combos,
  ranged and magic attacks; knockouts like the engine.
- **NPCs:** engine-like perceptions (warnings, murder witnesses, owners guarding chests), NavMesh chases, swimming,
  falling, ladders, fleeing, berserk, NPCs drinking potions.
- **VR hero:** full body in armor with IK, transformation scrolls, torches, joints, armor/amulets/rings, books and maps.
- **Inventory:** VR trade counter, stack splitting, NPC loot backpack, item details.
- **World:** movers and triggers, winches, shrines/alchemy/rune tables/beds, treasure digging, original Bink videos in a
  VR cinema.
- **Look:** torch-lit caves, day and night, particles, light pool.
- **Mods:** G1 Mroczne Tajemnice, G2 Union mods (partly), Ogg Vorbis dubbing.
- **Save/load** (off by default, work in progress).
