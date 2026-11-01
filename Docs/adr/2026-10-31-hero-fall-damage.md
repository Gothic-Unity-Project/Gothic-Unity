# ADR: The hero takes fall damage

- Status: proposed
- Date: 2026-10-31

## Context

NPCs already took fall damage like in Gothic, but the VR hero could jump off any cliff unhurt. In Gothic a fall from high up hurts or kills the hero by the same rule as everybody.

## Decisions

1. **The hero takes the same fall damage as the NPCs:** the human guild's damage per meter beyond its safe height, less his fall protection.
2. **The fall is measured from the highest point in the air** down to where he lands, so a jump before the fall counts too.
3. **Landing in water, climbing and teleports never count as falls.** Not chosen: counting a teleport down a cliff as a fall - the teleport is the comfort way to move and must stay safe.
