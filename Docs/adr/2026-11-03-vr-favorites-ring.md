# ADR: VR favorites ring - Gothic's spell selection with weapons and favorite items

- Status: proposed
- Date: 2026-11-03
- Technical details: VRFavoritesRing, VRFavoritesRingSpell (VR); NpcInventoryService.GetConflictingEquippedItems, PlayerService.DropItem, PlayerInventoryOwner, NpcProperties.FavoriteItems (Core); DeveloperConfig EnableVrSpellRing, EnableRingAroundPlayer, EnableRingWeaponSlots, EnableRingItemSlots, EnableRingZoom

## Context

In Gothic, holding the magic key shows the equipped runes and scrolls in a circle around the hero. In VR there's no key. Taking a rune out of the backpack and holding it with both hands works (PR #482), but it's slow in a fight.

## Decisions

1. **A gesture opens the ring:** both empty hands above the head and both triggers held for 1 s. A hand with a readied ring spell isn't empty. The "Favorites ring" Immersion option can switch it off.
2. **The ring circles the player at eye level** (`EnableRingAroundPlayer`). The slot the head looks at is selected, and the hand turns the ring like a carousel. Off: a small ring in front of the chest. The playtest decides which layout stays.
3. **What's in it:**
   - up to 8 equipped spells;
   - the equipped melee weapon and bow/crossbow (`EnableRingWeaponSlots`);
   - up to 3 favorite items and the money (`EnableRingItemSlots`).

   Only what's in the backpack shows.
4. **A spell is taken, not grabbed.** Grip puts an invisible rune into the hand, and the hand casts it alone (`VRRuneCaster.RingHand`). Weapons and items are the real items, taken out of the backpack.
5. **An item let go at a slot goes in.** A rune or scroll is equipped. A weapon is equipped and replaces the one of its kind. Anything else becomes a favorite.
6. **One melee weapon and one bow/crossbow are equipped, like Gothic.** The rule lives in `NpcInventoryService.GetConflictingEquippedItems`, so every equip path follows it.
7. **An equipped weapon stays equipped while it's in the inventory, like Gothic.** That means in a hand, a holster or the backpack, so taking it out of the ring or the backpack doesn't unequip it. It comes off when the last one is dropped into the world (`PlayerService.DropItem`). Armor still comes off when it's taken out of the backpack (ADR 2026-10-31).

## Consequences

- The favorite items aren't saved yet. A save section comes with main's save/load at the final rebase.
- `VRFavoritesRing` is 1,700 lines. It should be split into a `VrFavoritesService` (contents, put-in rules, the favorites and their save section) and gesture, carousel and slot-view adapters. The put-in should use `VrDropZone`. This is a follow-up.
- Opening the ring scans the VOB cache per slot. Caching it and measuring on Quest is a follow-up.
