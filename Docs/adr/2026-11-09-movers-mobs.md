# ADR: Movers, triggers and mobs from the experimental branch

- Status: proposed
- Date: 2026-11-09
- Technical details: Docs/architecture.md

## Context

Most of the world's logic objects came to main in its own, newer form: triggers, trigger lists, code masters, movers and their state in save games. The experimental branch still had a few things main lacked: objects that react to being shot, gates that stand away from their recorded path, the orc mummies you loot without a lid, bookstands showing their page, and touch plates that could not be grabbed.

## Decisions

1. **Main's triggers, code masters and mover saves stay, the branch's versions are not brought over.** They follow the engine and store their state in Gothic's own save fields. Not chosen: the branch's separate save file for them, main's save replaces it.
2. **Triggers and movers that react to damage work when shot, like in the engine.** Arrows, bolts and spells set them off, who may do it follows the object's settings. The damage threshold is not checked, a projectile does not know its damage yet.
3. **A gate's path counts from where the gate stands.** Like in the engine, not from the absolute recorded points. Gothic 2's Irdorath gates stand away from their path. Our saves keep the gate's original position, so loading a save gives the same result.
4. **Containers without a lid open as a loot backpack next to them.** The orc mummies have nothing to swing open, so they work like looting a body. What you take is saved with the world.
5. **A bookstand shows its page above it, facing the player.** It closes on the next touch or when walking away.
6. **No settings, and no mark on shootable objects.** These are fixes, the game should work like this. A green X on shootable objects that show nothing was tried and removed: it was in the way, and aiming help already exists.

## Consequences

- Gothic 2's Irdorath puzzles (shot switches, moving gates, touch plates) are playable.
- Not tested yet on the Quest, and not in every mod. A mod object standing away from its path now moves from where it stands, which is the engine's rule.
- A mover caught in the middle of a move in a save of the original game may start slightly off.
