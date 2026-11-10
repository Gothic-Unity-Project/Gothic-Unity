# ADR: UI and Marvin

- Status: proposed
- Date: 2026-11-03
- Technical details: Docs/architecture.md (events, adapters)

## Context

Gothic's Marvin mode has console commands for testing: god mode, teleporting forward (key K), killing the NPC in focus, inserting items. Ours had a few commands, and in VR the Marvin panel was glued to the left hand. Gothic shows no damage numbers; many VR players asked for them, at least as an option. The original game's ending closes the game after the credits.

## Decisions

1. **Marvin mode stays switched only like on main: typing "marvin"/"42" or poking the Gothic-Unity logo 3 times in the menu,** plus the developer setting to start with it on. MarvinService alone holds the state, so every switch shows the same. Not chosen: a Marvin option in the Immersion menu, off at every start (the author's first version) - the option in Gothic.ini was a second copy of the state that each new switch had to keep in sync.
2. **More Marvin commands, always there in Marvin mode:** god mode, 4 m forward through walls, kill (the NPC a hand pointed at last, else the one in the middle of the view; immortal ones like Diego stay alive, like Gothic's). Marvin mode itself is the switch, like in the original. Not chosen: a developer setting per command (Marvin mode already gates them); item lists, NPC groups, named command lists and commands ready in the recent list from developer settings (the author's first version - more to keep up than they save, `insert` covers items and NPCs); buttons on the Marvin panel for each cheat (the panel was full, and the console already completes names).
3. **The Marvin panel and the on-screen keyboard can be grabbed by a bar at their side,** like SteamVR's windows. Let go in the air, the panel floats there and slowly turns after your head. Let go above the left hand, it goes back to the hand. Always on. Not chosen: a setting for the bars or a separate one for the head follow (one behaviour, fewer settings).
4. **Damage and heal numbers are an option in the Immersion menu (VR), off by default,** over NPC heads and next to your own bar ("-12 HP", mana in blue), plus an option for "HP/MAX" on the bars and a size slider. They are off by default because the original game has none. They come from the game's changes of hit points or mana (weapon, spell, arrow, potion, a script's change, Marvin's kill), not from each attack. A knockout setting the hit points to 1 and a rune's mana taken piece by piece while charging show no extra number. Not chosen: each NPC checking its hit points every frame (the author's first version), which costs too much with hundreds of NPCs on a Quest.
5. **The Immersion menu gets the options whose rules are already in the game:** death (or only knocked out), ranged knockout, XP for every death, light refresh, plus a "Restart game" button to pick another game profile (not on Quest or Pico: a headset app can't start itself again). They use main's pages with arrows. Not chosen: the author's scrolling menu (main's pages already do it).
6. **After the ending's videos the game starts again at the main menu (or game profiles) instead of closing,** always, without a setting. You then don't have to start the game again. Not chosen: a developer setting to close it instead (the author's first version).
7. **Mods in their own game copy are offered in the game selection too** (GameSettings.json `ModFolders`: the game and the folder). Main's selection scans only the games' own system folders; a mod like Mroczne Tajemnice or Renovation lives in a separate copy of the game. Each folder's game inis are listed under its game like the installed mods. Not chosen: main's removed profile list with names and inis per entry (the scan finds them).
8. **Walking during dialogs is an Immersion option ("Walk in dialogs"), off by default.** The dialog pins the hero like the original ([2026-10-30-dialog-pins-hero](2026-10-30-dialog-pins-hero.md)); with the option on he moves inside a 5 m bubble around the NPC, walking out of it chooses END, and a dialog only opens within those 5 m. That record removed free movement as unneeded; since then the NPC turns his head and body to the hero during a dialog, which only pays off when the player can move, so it comes back as a player's choice. Not chosen: a developer setting (players want to pick it).
9. **Small fixes are always on, without a setting:** the keyboard's Clear key, the laser drawn above the keyboard, the "(Trigger) skip" hint under a video, the monitor view dark while a video plays.

## Consequences

- God mode, the panel's position and the Marvin commands typed last aren't saved: they last for the session. The menu options are in Gothic.ini like the other Immersion options.
- On Quest and Pico there is no "Restart game" button. The ending there closes the game.
- The "(Trigger) skip" word is translated for German, English and Polish; other languages show "skip".
- Not in this record: the game's ending and restart for the flat screen mode, which ignores the ending (follow-up).
- Not in this record: the hero's bars drawn on top of the world (the author's branch copies the on-top look onto them), checked separately.
