# ADR: The loading screen stays until the world is shown

- Status: proposed
- Date: 2026-11-02

## Context

Since the world around the player is built before the view opens (see 2026-10-28-load-pop-in), the headset showed the loading screen, then plain black, then the world. Destiny of Myrtana waits about 3 seconds there, the vanilla games one to three. Black without any sign of progress feels like the game hung.

## Decisions

1. **The loading screen stays visible during the wait.** When the player is put at the spawn point, Gothic's loading picture and bar go along and stay where they were in front of the player. The world around stays black. In VR the picture is drawn above the black like the game menu. It disappears right before the world fades in. Not chosen: a new "please wait" sign, or keeping the player in the loading room (the objects around the spawn point are only built once the player is there).
2. **The bar shows the building of the surroundings.** It gets a fourth part for the objects and people around the spawn point, which fills as they are built.

## Consequences

- From the loading picture until the world fades in, the player sees the picture and a moving bar, with at most a frame of black when arriving at the spawn point.
- Texts which were already shown above the black (new diary entry, dialog lines) now appear next to the loading picture.
- The bar now ends full: its part for the objects of the world counted more objects than it got and stopped at about half. It is completed once they are all created. Level changes and loaded saves count their people as the bar's people part, which they never filled before.
