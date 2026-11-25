# ADR: Mobs play their own animations, sounds and particles while they are used

- Date: 2026-11-25
- Technical details: Docs/mobs.md

## Context

In Gothic, a mob (an anvil, a forge, a grindstone, a water bucket, a lever) has its own animations for its states, not only the person using it. While a smith works, the anvil throws sparks and rings, the forge roars and glows, the grindstone turns and sparks, the bucket steams.

We only played the person's animations. The mobs stood still and silent: no turning grindstone, no sparks, no sound of the smithy in Khorinis.

## Decisions

1. **Every mob whose state an NPC or the hero changes plays its own state animation, with its sounds and particles.** That is what the engine does, and it covers the smithy, stoves, cauldrons, levers and the like at once. Not chosen: only the smithy's four mobs, which would leave the other working places silent.
2. **When the hero forges himself, the anvil, forge and grindstone work while his smithing window is open.** They start when it opens and stop when it closes, as if he were one of the smiths. Not chosen: leaving the hero's own forging silent.
3. **Doors, chests and beds stay as they are.** In VR they move physically when the player grabs them; playing their recorded animation as well would fight the hand. Not chosen: all mobs including doors and chests.
4. **The forge gives no extra light while used, like in the original.** Its particles don't light anything in Gothic either; the room's light comes from the world's lamps. Not chosen: a glowing light added while the forge is in use; it can still come later if the smithy looks too dark.
5. **It works in Gothic 1 and 2, in VR and on the flat screen, without a switch.** It is the original's behaviour, not an option.
6. **A mob only animates while it is being used, with a small player of its own.** An unused mob costs nothing, and a used one only moves its few rigid parts and plays its effects, so many working places in a town stay cheap on the headset. Not chosen: giving every mob the full animation system the characters use, which is built for skinned bodies and costs far more per object.

## Consequences

- The smithy in Khorinis (and the smiths in Gothic 1) can be heard and seen working: sparks, steam, a turning grindstone.
- Nothing new is saved: the mob's state already is, and its animation starts again with the next use after a load.
- Mobs without their own animation file (Fellan's planks) don't change.
- An NPC killed at a mob, or gone out of sight, lets go of it at once: the mob falls silent and is free for others. Before, a dead smith kept his anvil.
- A lever or wheel left in its other state shows its resting pose after a load until it is used again; that is still open.
