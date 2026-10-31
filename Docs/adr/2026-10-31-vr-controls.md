# ADR: Choosing the VR controller buttons

- Status: proposed
- Date: 2026-10-31

## Context

Gothic lets the player bind its keys, the reference engine too. In VR the buttons were fixed: the left controller moves, sprints and opens the menu, the right one turns, teleports, jumps and crouches. Left-handed players and players with other habits (jumping with the upper button, the menu on the controller's own menu button) had no choice.

## Decisions

1. **Three settings instead of free binding:** handedness (all tasks of the left controller on the right one and the other way round), the jump button (lower, upper or stick click, crouching takes the other face button) and the menu button (upper, lower or the controller's menu button). Free binding of every action needs a binding screen with "press a button" capture, which Gothic's menus can't show, and grip and trigger stay for grabbing and using anyway.
2. **They sit in rows 3 to 5 of Gothic's game menu,** where the original has its mouse settings, which VR hides. The VR menus have no free rows.
3. **Applied as overrides of the input bindings,** so every part reading the menu, jump, crouch, sprint, moving, turning and teleporting follows without knowing about it. Grabbing, using and the hand buttons themselves are not remapped. The keyboard keys of the simulator stay.
4. **The menu is read through the remappable menu action** instead of the left upper button directly.
5. Stored in Gothic.ini, its own section for the VR controls.
