# ADR: Wearing armor, amulets, rings and belts

- Status: proposed
- Date: 2026-10-30

## Context

The VR hero could carry armor, amulets and rings but not wear them: no protection, no ring or amulet bonuses. In Gothic, wearing an item adds its protection, runs its script (an amulet of strength adds strength), and taking it off takes both back. Only one armor, one amulet and one belt fit, and two rings.

## Decisions

1. **The engine's rules decide.** An item that needs more strength or another attribute than the hero has can't be worn, and the game's own message says what's missing. A new armor, amulet or belt replaces the worn one; with two rings worn, a third doesn't fit. Wearing adds the item's protection and runs its script, taking it off undoes both.
2. **In VR, the trigger on a held armor, amulet, ring or belt puts it on or takes it off,** like a click in Gothic's inventory, with a short buzz in that hand. Not chosen: putting armor on by moving it onto the body, which needs a visible hero body first.
3. **What the hero no longer carries, he no longer wears.** An item dropped or sold is taken off. Moving an item between the hand and the backpack briefly takes it out of the inventory, so this is checked half a second later.

## Consequences

- Armor protects the hero, rings and amulets work, in both games.
- Without a hero body, the player doesn't see the armor; the status menu shows the protection.
- Armor the scripts give NPCs still adds no protection to them; that comes separately, because loading a game must not add it twice.
