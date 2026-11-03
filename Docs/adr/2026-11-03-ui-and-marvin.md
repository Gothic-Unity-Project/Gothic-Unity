# ADR: UI and Marvin

- Status: proposed
- Date: 2026-11-03
- Technical details: Docs/architecture.md (events, adapters)

## Context

Gothic's Marvin mode has console commands for testing: god mode, teleporting forward (key K), killing the NPC in focus, inserting items. Ours had a few commands, and in VR the Marvin panel was glued to the left hand. Gothic shows no damage numbers; many VR players asked for them, at least as an option. The original game's ending closes the game after the credits.

## Decisions

1. **The Marvin option in the Immersion menu is off every time the game starts.** Marvin mode and its panel give cheats; a player has to turn the option on in each session, so nobody plays with them by accident. Developers can start with it on (a developer setting). Not chosen: remembering the option like the other ones.
2. **More Marvin commands, each behind its own setting:** god mode, 4 m forward through walls, kill (the NPC a hand pointed at last, else the one in the middle of the view; immortal ones like Diego stay alive, like Gothic's), item lists, NPC groups, named command lists and commands ready in the recent list. Testers type less, and a build can leave any of them out. Not chosen: buttons on the Marvin panel for each cheat (the panel was full, and the console already completes names).
3. **Falling out of the world in Marvin mode brings you back** to the last place you stood on, or to the world's start. Teleporting forward goes through walls, so it can drop you into the void. Only in Marvin mode, so normal play never teleports you.
4. **The Marvin panel and the on-screen keyboard can be grabbed by a bar at their side,** like SteamVR's windows. Let go in the air, the panel floats there and slowly turns after your head. Let go above the left hand, it goes back to the hand. Not chosen: a separate setting for the head follow (one behaviour, fewer settings).
5. **Damage and heal numbers are an option in the Immersion menu (VR), off by default,** over NPC heads and next to your own bar ("-12 HP", mana in blue), plus an option for "HP/MAX" on the bars and a size slider. They are off by default because the original game has none. They come from the game's changes of hit points or mana (weapon, spell, arrow, potion, a script's change, Marvin's kill), not from each attack. A knockout setting the hit points to 1 and a rune's mana taken piece by piece while charging show no extra number. Not chosen: each NPC checking its hit points every frame (the author's first version), which costs too much with hundreds of NPCs on a Quest.
6. **The Immersion menu gets the options whose rules are already in the game:** death (or only knocked out), ranged knockout, XP for every death, light refresh, plus a "Restart game" button to pick another game profile (not on Quest or Pico: a headset app can't start itself again). They use main's pages with arrows. Not chosen: the author's scrolling menu (main's pages already do it).
7. **After the ending's videos the game starts again at the main menu (or game profiles) instead of closing.** You then don't have to start the game again. This is a setting, so off closes it like the original.
8. **Small fixes are always on, without a setting:** the keyboard's Clear key, the laser drawn above the keyboard, the "(Trigger) skip" hint under a video, the monitor view dark while a video plays.

## Consequences

- God mode, the panel's position and the Marvin commands typed last aren't saved: they last for the session. The menu options are in Gothic.ini like the other Immersion options.
- On Quest and Pico there is no "Restart game" button. The ending there closes the game even with the setting on.
- The "(Trigger) skip" word is translated for German, English and Polish; other languages show "skip".
- Not in this record: the game's ending and restart for the flat screen mode, which ignores the ending (follow-up).
- Not in this record: the hero's bars drawn on top of the world (the author's branch copies the on-top look onto them), checked separately.
