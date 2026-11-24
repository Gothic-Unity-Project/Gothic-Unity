# ADR: The favorites ring opens after half a second

- Date: 2026-11-24
- Replaces: decision 1 of [VR favorites ring](2026-11-03-vr-favorites-ring.md)
- Technical details: VrFavoritesGestureDomain and VRConstants.FavoritesRing (VR)

## Context

The ring opened with both empty hands above the head while both triggers were held for 1 s. In a playtest video the player stood with raised arms for a long time before the ring came - in a fight that breaks the flow.

## Decisions

1. **Both empty hands above the head and both triggers held for 0.5 s open or close the ring.** The rest stays: the controllers vibrate while it builds up and once more when the ring opens or closes, the triggers have to be let go before the next toggle, the backpack may stay in a hand. Not chosen: dropping the triggers (raising the hands to stretch or climb would open the ring).

## Consequences

- A hand dipping below the eyes or a trigger flickering keeps the hold time for 0.2 s (was 0.3 s), a fair part of the shorter hold.
