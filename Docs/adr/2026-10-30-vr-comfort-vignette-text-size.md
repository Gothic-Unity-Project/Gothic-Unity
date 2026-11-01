# ADR: A comfort vignette and a text size option

- Status: proposed
- Date: 2026-10-30

## Context

Moving with the stick and turning smoothly makes some players sick in VR. A common help is a vignette: the edges of the view darken while the body moves without the player walking. Subtitles and dialog choices had one fixed size, too small for some players. The original game has neither, as it isn't played in VR.

## Decisions

1. **The vignette reacts only to movement the player doesn't make with their body.** Smooth movement, smooth turning and climbing darken the edges; real head movement, teleports and snap turns don't, since they don't cause the sickness. Not chosen: reacting to every movement of the view, which would darken the view whenever the player looks around.
2. **The vignette is off by default, with the strengths light and strong.** Players who don't need it see no change. Not chosen: on by default, because it narrows the view for everyone.
3. **The text size option sits below the subtitles in the game menu.** VR hides the other rows of that menu, while the VR accessibility and immersion menus are full once ledge climbing is in. Not chosen: a new VR menu page, which would be another place to look for a single option.
4. **Text size scales subtitles and the dialog box as a whole (normal, large, larger).** The layout stays the same, only bigger. Not chosen: bigger fonts inside the same boxes, which would cut off long lines.

## Consequences

- Players who get sick from stick movement can play longer without teleporting.
- Bigger subtitles cover more of the speaking NPC.
- The vignette needs a check in the headset, on Quest 2 as well: it draws one more see-through layer over the view while moving.
