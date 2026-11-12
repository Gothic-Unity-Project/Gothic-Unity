# ADR: A dialog pins the hero where he stood until it ends

- Date: 2026-10-30

## Context

In Gothic, an NPC that wants something from the hero starts talking to him on its own: a guard at a gate warns him, a quest giver calls him over. The NPC notices the hero within a few meters, turns to him and opens the dialog about a second later. The original game stops the hero at that moment and opens the dialog wherever he stands, up to 20 meters away. During the dialog the player can't move.

In VR the hero could walk freely during dialogs: inside a five meter bubble around the NPC, and walking out of it ended the dialog. A dialog also only opened when the hero was within those five meters. A player walking past an NPC was usually farther away by the time it spoke, so the dialog never opened. At the Old Camp castle gate the guards' warnings were never given, and without the warnings the guards never attack. Every NPC that starts a talk on its own was affected.

## Decisions

1. **A dialog pins the hero to the spot where he stood when it opened, until it ends.** Teleporting is off. Walking (stick or a physical step) stays possible inside a small radius around that spot and slows down to a stop at its edge (changed 2026-11-08: the stick was off too, so on the simulator the hero couldn't move at all). The player has to finish the dialog, as in the original. Not chosen: opening the dialog farther away but keeping free movement, because walking off a dialog broke too much.
2. **Turning stays on.** An NPC behind the player can still be faced, and turning doesn't take the hero away. Not chosen: locking turning too, as the old lock did.
3. **An NPC can open a dialog up to 8 meters away.** The original allows 20 meters, but in VR that was far too wide: Bloodwyn talked to the player from across the yard. Farther away it doesn't open and the NPC goes back to what it did. The choices then appear a few meters in front of the player, on the way to the NPC, so they stay readable. Not chosen: no limit, because a script quirk could pull the player into a dialog with somebody out of sight.
4. **Free movement in dialogs is removed, with its DeveloperConfig flag.** Not chosen: keeping it as an option that is off by default, because nobody needs it and it doubled the dialog code. In that mode NPCs couldn't reach a hero who walked on anyway.

## Consequences

- Gate guards warn, warn again and attack, as in the original. NPC-started talks reach the hero.
- The player can no longer walk out of a dialog; ending it needs the END choice or the NPC ending it. As in the original there is no other way out: a dialog that never ends is a bug and needs loading a save game. A debug action to end it can follow.
- If the hero can't be held (falling, moved by a script), the dialog ends.
- An NPC far away is heard, but its subtitles are hidden beyond twelve meters as before.
- Still open: the shared player lock has no owner, so the end of a dialog also releases a lock somebody else set (for example a seat). This was already the case before.
