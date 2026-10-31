# ADR: Fat NPCs and see-through ghosts

- Status: proposed
- Date: 2026-10-31

## Context

The scripts make some NPCs fatter or thinner (most humans in Gothic 2 have a fatness between -1 and 2), and mark ghosts such as Quarhodron and Rhademes in the addon, and the fire shadowbeast, as see-through. Neither showed.

## Decisions

1. **Fatness pushes body, head and armor out along their surface,** half a centimetre per step like the reference engine, done by the NPC shader. What the NPC holds stays as it is.
2. **Each NPC already has materials of its own,** so its fatness costs nothing extra to draw.
3. **Ghosts are drawn half see-through** with the transparent version of the NPC materials that the item highlighting already uses. Not chosen: the reference engine's refracting ghost look, which needs a copy of the screen, too expensive on the Quest.
