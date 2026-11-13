# ADR: What the VR player sees when the hero dies

- Date: 2026-11-13
- Replaces: decision 5 of [What the VR player sees while the hero is knocked out or dead](2026-10-30-vr-knockout-view.md)

## Context

When the hero dies, the original game shows the dead hero lying there from a circling camera until the player opens the menu. In VR there is no outside camera. Until now the view fell with the body like a knockout and then closed to black from the edges. In play tests that black circle felt like a tunnel, or like the game itself had ended, instead of lying there dead.

## Decisions

1. **When the hero dies, the view falls with the body like a knockout and a dark grey veil covers it within three seconds and stays until a game is loaded.** It is the knockout's grey veil, only darker; if the hero was knocked out first, the veil darkens from where it is, without a jump. The world stays faintly visible, which matches the original showing the dead hero lying there. The menu still opens by itself, and the veil hides while the menu is open, so it can be read and pointed at. Not chosen: the view closing to black from the edges (the earlier decision), which felt like a tunnel and like the end of the game rather than being dead in the world.

## Consequences

- The player still sees their hands move the dead body's arms faintly. Accepted: the darkness hides most of it.
- The comfort vignette is only about movement again; the hero's death no longer uses it.
