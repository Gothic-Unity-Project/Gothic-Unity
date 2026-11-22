# ADR: VR help panels take the place of the menus

- Date: 2026-11-22
- Replaces: decision 3 of [VR help panels above the menu](2026-11-13-vr-help-panels.md)
- Technical details: Docs/vr-help-panels.md

## Context

Clicking the Gothic logo in the in-game VR menu showed the three help panels above the menu ring, tilted towards the player.
Reading them meant looking up for a long time: tiring for the neck, and the text sat at an angle above the menus.
The original game never stacks screens: a sub menu replaces the main menu, the diary replaces the view. One page in front of the player at a time.

## Decisions

1. **The logo swaps the menus for the help panels.** The status screen, the open menu and the quest log disappear, and the controls, the hero's slots and the help tabs appear in their places at eye level, facing the player. Clicking the logo again brings the menus back. Not chosen: panels in front of the dimmed menus - two layers close together are tiring to focus in a headset; panels below the ring - the hands and controllers get in the way of the text.
2. **The logo stays visible and is the only way back.** No extra button. Not chosen: a back button on the panels - one more element for something the logo already does.
3. **The swap is instant.** Gothic's own menus switch without a transition. Not chosen: a short fade - softer, but no gain in readability.
4. **The menu always opens on the Gothic menus**, even if the help was showing when it was closed. Not chosen: remembering the help - the menu is mostly opened to play, not to read the help again.
5. **The main menu before loading a game swaps the same way**: the logo replaces the menu with the three panels at eye level. Not chosen: keeping the panels above the main menu - the same strain on the neck as in game, and two behaviours for one logo.

## Consequences

- Help and menus can't be read side by side in game any more.
- Pressing back in a sub menu while the help is shown returns to the menus.
- Coming back from the help, the quest log shows its topic list again, not the quest that was open. Accepted: keeping it would mean hiding the menus a second, more fragile way.
