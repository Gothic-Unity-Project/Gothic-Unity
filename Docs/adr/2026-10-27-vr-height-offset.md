# ADR: VR height offset

- Status: accepted (PR #451)
- Date: 2026-10-27

## Context

In VR the camera sits where the headset is. When the player's seated or standing height doesn't fit, they see the world from the wrong height and can't fix it.

## Decisions

1. **A slider the player sets, on top of sit and stand.** It moves the view up or down from -1 m to +1 m in 5 cm steps, the middle means no change. It works with every sitting and standing mode and is saved with the other settings.
2. **It applies when the player lets go of the slider.** Not chosen: moving the view while dragging (the first version), which shifted the player away from the slider they were still holding.
3. **The range is a full metre each way.** The first version allowed half a metre and was widened.
4. **It sits under Sit/Stand in the accessibility menu.** Gothic's menus fit six rows, so the spectator smoothing option moved to the immersion menu to make room. Its saved value stays the same, existing settings keep working.
5. **Only for the new VR setup** (XR Interaction Toolkit). The old HurricaneVR setup ignores it.

## Consequences

- The accessibility menu is full: new options need another menu, or the menus need more room.
