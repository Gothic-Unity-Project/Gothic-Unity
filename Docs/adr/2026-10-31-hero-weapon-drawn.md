# ADR: A melee weapon in the VR hero's hand is a drawn weapon

- Status: proposed
- Date: 2026-10-31

## Context

In Gothic, drawing a weapon is a clear act: guards in the camps and cities warn the hero to put it away and attack if he doesn't, and others back off. In VR there is no draw key. The hero simply holds a sword in his hand, and until now the NPCs never noticed.

## Decisions

1. **A melee weapon in a hand counts as drawn, letting go of it as put away** (found on BoroBongo's branch). This is the closest VR has to Gothic's draw key. Not chosen: a separate gesture or button, which the original doesn't have and which a player can forget.
2. **The NPCs react like in Gothic.** The ones around notice the drawing and the putting away, and those who see the hero armed react as their scripts say: a warning, a fight, or nothing.
3. **Only melee weapons for now.** A held rune already counts as readied magic; bows follow when they report themselves the same way.

## Consequences

- Walking through a camp with a sword in hand gets the guards' warnings, as in Gothic. Weapons belong on the belt or the back while there.
- Picking a weapon up from the ground or a chest briefly counts as drawing it.
