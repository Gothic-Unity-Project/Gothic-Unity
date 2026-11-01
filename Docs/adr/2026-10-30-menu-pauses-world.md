# ADR: The world waits while the menu is open

- Status: proposed
- Date: 2026-10-30

## Context

Opening the menu during play left the world running: dialogs went on talking, monsters kept attacking, a knocked out hero got up meanwhile. In Gothic the menu stops the game until it is closed. Videos already paused the world, but on their own, so a menu and a video could undo each other's pause.

## Decisions

1. **The menu pauses the world while it is open during play.** Time stands still for NPCs, fights, knockouts and the clock, and the world's sounds pause. The menu at the start of the game, before a world is loaded, pauses nothing.
2. **One place holds the pause, with the reasons menu and video.** The world runs again once every reason has ended. Not chosen: each part pausing time on its own, which is how a closing menu could have restarted the world during a video.
3. **The music goes on in the menu, but not in a video.** As in Gothic; a video has its own sound. Menu clicks play while paused.
4. **The VR hands follow the controllers directly while paused.** Their physics doesn't run then, so they would freeze in the air otherwise.

## Consequences

- Nothing happens to the player while they are in the menu.
- Items held in a hand stay where they are until the menu closes, then catch up with the hand.
- NPC scripts don't run while paused, also during videos, which matches the original game.
