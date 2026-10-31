# ADR: Effects the scripts play

- Status: proposed
- Date: 2026-10-31

## Context

The scripts start visual effects of their own: Gothic 2 more than a hundred times (Abuyin's teleport, the stone guardians waking up, the Dementors darkening the world while they talk, Beliar's claw, the circle at the end on Irdorath), Gothic 1 once, for the Sleeper's fireball at the end of the game. Nothing showed. Some of these effects are not particles but act on the whole screen: a tint, an earthquake, slowed time, a wobbling view.

## Decisions

1. **Script effects play like the engine's,** at the bone they name on the NPC or item: their particles, sound, the effects they create, and their cast part (the teleport's flash), since a played effect is cast at once.
2. **A flying effect (the Sleeper's fireball) follows its target at its own speed and hurts it like a spell:** the damage of each damage type minus the victim's protection. A wall in between stops it. The reference engine leaves this out.
3. **A screen tint covers the VR view** with its color or animated texture, below the screen fade, so dialogs stay readable.
4. **An earthquake rumbles both controllers** while the hero is within its radius. Not chosen: shaking the view, which makes VR players sick.
5. **Slowed time slows the whole world, the player included,** because the VR player moves by the same clock. In Gothic swampweed slows the world and the player alike; the addon's slow motion keeps the player at full speed, which VR can't do.
6. **The wobbling view is left out** in VR for the same reason as the shake.

## Consequences

- Dementors darken the view while they talk to the hero, until the scripts end it.
- The pause menu sets the time scale too; whichever branch merges second keeps a running slow time when the menu closes.
- The effects' lights are not shown yet.
