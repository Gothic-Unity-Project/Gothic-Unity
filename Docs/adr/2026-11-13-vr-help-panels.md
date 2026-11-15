# ADR: VR help panels above the menu

- Date: 2026-11-13
- Technical details: Docs/vr-help-panels.md

## Context

Gothic explains its controls in the keyboard settings; a VR player has no keyboard and many hand gestures the original never had (strokes to swim, drawing a bowstring, charging spells, the trade counter).
Clicking the Gothic logo in the menu already shows two panels above the menu ring: the controller with its buttons and the hero with his body slots. Most of their texts were empty, the controller showed the default layout even when the player changed the buttons, and two of the hero's slots shown don't exist.
The panels faced straight ahead at head height and above, so they were hard to read from below.

## Decisions

1. **The controller labels follow the player's control layout** (left-handed, jump, crouch and menu button). A left-handed player otherwise reads the wrong hand. Not chosen: always the default layout - simpler, but wrong for everyone who changed a button.
2. **A third panel above the quest log explains swimming and diving, trade, magic and bows/crossbows in tabs**, laid out like the quest log: topics left, content right. Each topic shows the controller with only the keys it needs and a few lines of text. Not chosen: one long text page - too much to read in a headset.
3. **The three upper panels tilt about 15° towards the player**, like a cockpit above the head. Not chosen: 10° (barely noticeable) or 25° (looks like a ceiling).
4. **The hero panel shows only the slots that exist**: both hips, the backpack on the left shoulder and a holster on the right shoulder.
5. **Texts in English and German first.** Other languages follow later.
6. **The left-over second copy of the panels under the quest log is removed.**

## Consequences

- Other languages show empty labels until translated.
- When a gesture changes (e.g. how to swim), its help text has to be changed with it.
- Changing a button while the panels are open shows the new layout the next time they open.
