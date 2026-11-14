# ADR: VR swing detection

- Date: 2026-11-13
- Technical details: Docs/vr-melee.md

## Context

A sword swing in VR opens Gothic's attack window when the weapon moves fast enough.
Running forward with a sword in hand opened it too: the player's own movement wasn't taken out of the weapon's speed, and sprinting alone was faster than the 2 m/s threshold.
A trembling or flicked hand could also start a swing, and the 0.5 s average made real swings start late.
Ancient Dungeon VR is the reference for a swing that feels right: it measures both the handle and the tip of the weapon, ignores the player's movement and asks for a real swing path.

## Decisions

1. **A swing is measured relative to the player's play space, not the world.** Running, falling, turning and teleporting move the play space with the player, so they never count as a swing. Not chosen: taking the player's speed out of the weapon's world speed, which was wrong whenever the movement system moved the player more than once a frame.
2. **A swing needs a fast hand, a faster tip, for a moment, over a distance.** Hand and blade tip both above their speed, kept for a short time, and the tip has travelled a real path. Two-handed weapons need a little less speed. Not chosen: a single speed threshold, which let flicks of the wrist, slow pushes and tracking spikes through.
3. **A thrust along the blade counts as a swing.** A stab moves the tip no faster than the hand, so it has its own speed along the blade. It is higher than the push that finishes an unconscious NPC: a thrust attack is a strike, the finishing push is slow and deliberate.
4. **The values can be tuned in the Marvin menu.** The feel needs testing in the headset; the defaults are a starting point.

## Consequences

- Swings start sooner, and walking or running with a weapon in hand no longer attacks.
- The stab finish uses the same measurement, so running into an unconscious NPC no longer stabs.
- Weapon skill doesn't change the thresholds yet.
