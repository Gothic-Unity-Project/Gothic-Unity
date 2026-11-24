# ADR: The favorites ring opens by raising the hands, no buttons

- Date: 2026-11-24
- Replaces: decision 1 of [VR favorites ring](2026-11-03-vr-favorites-ring.md)
- Technical details: VrFavoritesGestureDomain and VRConstants.FavoritesRing (VR)

## Context

The ring opened with both empty hands above the head while both triggers were held. In a playtest video the player stood with raised arms for a long time before the ring came - holding two triggers up there is awkward, and a gesture that takes that long breaks the flow of a fight.

## Decisions

1. **Raising both empty, open hands above the head for 1.5 s opens or closes the ring.** No button is needed. The controllers vibrate three times while it builds up and once more when the ring opens or closes.
2. **The hands have to come down before the next toggle,** so arms kept up don't flip the ring every 1.5 s.
3. **A gripping hand doesn't count, except when it holds the backpack.** Climbing a ledge grips it, and a hand holding a weapon or item isn't empty; the backpack may stay in a hand like before, so an item goes into the favorites quickly. Not chosen: keeping a button, which was the slow part.

## Consequences

- Raising both empty hands without gripping anything for 1.5 s - stretching, waving - opens the ring. Players who don't want it switch the ring off in Immersion ("Favorites ring").
