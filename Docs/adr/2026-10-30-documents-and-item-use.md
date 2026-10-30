# ADR: Reading documents and using items

- Status: proposed
- Date: 2026-10-30

## Context

Books, letters and maps couldn't be read: the scripts build them page by page with the document functions, and none of these existed. Eating and drinking removed the item, but its own script never ran, so potions didn't heal and food didn't feed.

## Decisions

1. **Using an item runs its script like in the engine.** The item's use function runs with the hero as the user and the item as the current item. Eating and drinking in VR now do this, so potions heal. Who uses the item takes care of the item itself: eaten food is gone, a letter stays.
2. **The document functions build a document, showing it hands it to the platform.** A page setting for all pages also counts for pages added later.
3. **In VR, the trigger on a held book, letter or map reads it.** Its pages appear side by side in front of the player, each with its background and the lines in the page's Gothic font within its margins. The trigger again or letting go closes it. Not chosen: reading by bringing the item close to the face, which is easy to trigger by accident.
4. **A map marks the hero only where the map says which part of the world it shows.** Gothic 2 maps do; Gothic 1 maps carry no coordinates, so they show no mark for now.

## Consequences

- Letters, books and maps can be read in both games; potions and food work.
- Documents aren't consumed by reading, like in the original.
- Gothic 1 maps need calibrated edges per world before they can show the hero.
