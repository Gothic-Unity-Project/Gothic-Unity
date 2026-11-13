# ADR: VR documents and the equipped slot background

- Date: 2026-11-13
- Replaces: the [E] badge in decision 2 and the document reading in decision 4 of [VR backpack, equipping and items in the hands](2026-10-31-vr-backpack-and-items.md)

## Context

The backpack record marked equipped items with an [E] letter, and it read books, maps and letters with both hands holding them.

Gothic marks an equipped weapon or armor with a blue background behind the item, both in the original game and in OpenGothic. The letter did not look like the game.

Holding a page with both hands turned it with the grip, and the hand covered the text.

## Decisions

1. **Equipped weapons and armor are marked with Gothic's blue equipped slot background instead of an [E] letter.** It is the mark the original game uses, so the backpack and the loot views look like the game. Equipping and taking off work as before. Not chosen: keeping the [E] letter.

2. **Books, maps and letters are read one-handed with the trigger of the hand that holds them.** Press that hand's trigger to open the item, and press it again, let go, or put the item in a socket to close it. If both hands hold it, either trigger opens it, and if the reading hand lets go, the other hand takes over. Holding it with both hands no longer opens it by itself. Not chosen: reading with both hands, because the pages turned with the grip and the hand covered the text.

3. **A page is held by its lower corner and stands upright, facing the head, in either hand.** Its held size is about 0.6 m on the longest side, down from about 1.2 m. The pages extend up and to the side away from the holding hand, so the hand sits clear of the text. Not chosen: the old full size in the hand.

## Consequences

- Sealed letters are still used up when the reading ends, not when they are first opened.
- Bookstands are unchanged.
- The loot backpack's take-all control is a text button, "Take all", instead of a green X. The text is translated in every supported language.
- If the equipped background image is missing, equipped items show no mark and one warning is logged.
- The rest of the 2026-10-31 record stays in force: equipping, use, the other small gestures and the switches.
