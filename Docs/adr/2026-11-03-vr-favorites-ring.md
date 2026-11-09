# ADR: VR favorites ring - Gothic's spell selection with weapons and favorite items

- Status: proposed
- Date: 2026-11-03
- Technical details: VrFavoritesService with VrFavoritesPutInDomain and VrFavoritesGestureDomain (contents, put-in rules, favorite items, gesture), VRFavoritesRing and VRFavoritesRingSlotViews (visuals, hands), VRFavoritesRingSpell, models in Models/FavoritesRing (VR); ItemInstanceExtension.GetEquipConflicts (applied in EquipDomain), PlayerService.DropItem/GetItemAmount, PlayerInventoryOwner, TradeService.CurrencyName (Core); no DeveloperConfig flags - two player preferences in Gothic.ini: `favoritesRing` (Immersion, 1 = on, menu "Favorites ring") and `favoritesRingZoom` (Accessibility, 0...1 of 0.5x...2x, menu "Favorites ring size")

## Context

In Gothic, holding the magic key shows the equipped runes and scrolls in a circle around the hero. In VR there's no key. Taking a rune out of the backpack and holding it with both hands works (PR #482), but it's slow in a fight.

## Decisions

1. **A gesture opens the ring:** both empty hands above the head and both triggers held for 1 s. A hand with a readied ring spell isn't empty. The "Favorites ring" Immersion option (ini `favoritesRing`) can switch it off. There is no DeveloperConfig flag for the ring or its parts.
2. **The ring circles the player at eye level.** The slot the head looks at is selected, and the hand turns the ring like a carousel that spins on after you let go. Not chosen: the branch's first layout, a small ring in front of the chest that snapped to the nearest slot, which felt strange in play.
3. **What's in it:**
   - up to 8 equipped spells;
   - the equipped melee weapon and bow/crossbow (always on);
   - up to 3 favorite items and the money (always on).

   Only what's in the backpack shows.
4. **A spell is taken, not grabbed.** Grip puts an invisible rune into the hand, and the hand casts it alone (`VRRuneCaster.RingHand`). Weapons and items are the real items, taken out of the backpack.
5. **An item let go at a slot goes in.** A rune or scroll is equipped. A weapon is equipped and replaces the one of its kind. Anything else becomes a favorite.
6. **One melee weapon and one bow/crossbow are equipped, like Gothic.** The rule lives in `ItemInstanceExtension.GetEquipConflicts`, applied in `EquipDomain`, so every equip path follows it.
7. **An equipped weapon stays equipped while it's in the inventory, like Gothic.** That means in a hand, a holster or the backpack, so taking it out of the ring or the backpack doesn't unequip it. It comes off when the last one is dropped into the world (`PlayerService.DropItem`). Armor still comes off when it's taken out of the backpack (ADR 2026-10-31).

8. **The ring size is a player preference.** The right stick (simulator: mouse wheel) zooms the open ring between 0.5x and 2x and locks the stick teleport meanwhile. The value goes into Gothic.ini (`favoritesRingZoom`, Accessibility, a 0...1 slider value like `heightOffset`) when the stick is released or the ring closes, and the Accessibility menu has a "Favorites ring size" slider for it.

## Consequences

- The favorite items aren't saved yet. A save section comes with main's save/load at the final rebase.
- The rules live in `VrFavoritesService` (VR), which also owns the hero's favorite items. `VRFavoritesRing` (about 1,000 lines) still holds the carousel, hover, grab and put-in handling; splitting it further and putting in via `VrDropZone` is a follow-up.
- Opening the ring scans the VOB cache per slot. Caching it and measuring on Quest is a follow-up.
