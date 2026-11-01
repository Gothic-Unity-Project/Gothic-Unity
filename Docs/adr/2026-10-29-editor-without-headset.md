# ADR: The editor keeps playing while the headset shows nothing

- Status: proposed
- Date: 2026-10-29

## Context

In the editor, the game stood still around the player as soon as the headset was taken off, fell asleep or lost its connection. Unity only works out what is near the camera while the camera draws a picture for the headset. Without those pictures, no NPC or object near the player was built, and NPCs walking in from afar never appeared. Tests without wearing the headset weren't possible. This was seen on Linux with a Quest streamed through WiVRn. Other headset software may keep sending pictures while the headset is off; then nothing changes there.

## Decisions

1. **In the editor, the camera draws to the Game view once the headset has shown nothing for a second.** Everything around the player keeps loading, and the Game view shows what the player would see. Not chosen: turning VR off in such a play session, because putting the headset back on would need a restart.
2. **The headset's next picture switches back by itself.** Putting the headset on again continues in VR without a restart.
3. **Only in the editor, with a developer setting to turn it off (on by default).** A built game on a headset never needs it, so it costs nothing there.

## Consequences

- Play sessions in the editor can be tested with the headset lying on the desk, or asleep.
- Without any VR running (no headset connected at all), the camera draws to the Game view as before.
