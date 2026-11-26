# ADR: Smoking a water pipe with your own hands in VR

- Date: 2026-11-26
- Technical details: Docs/mobs.md

## Context

The water pipes in Khorinis (and in Gothic 1) are mobs: NPCs sit down at them, pick up the mouthpiece and smoke, the pipe bubbles and smokes. The scripts give the player nothing there, it is a moment of atmosphere.

In VR you couldn't use them at all: grabbing the pipe did nothing.

## Decisions

1. **You grab the mouthpiece and the hose follows your hand.** The pipe's own hose bends towards the hand; pulled further than the hose is long, the mouthpiece slips out and swings back. Not chosen: a physics rope (livelier but costly and jittery on the headset) or a straight stretched line (looks wrong).
2. **At your mouth the pipe bubbles and smokes, and you blow out smoke like with a swampweed joint.** Nothing else happens, like in the original. Not chosen: the joint's effect, which the original doesn't give at a water pipe.
3. **You don't need to sit or crouch.** As long as the mouthpiece reaches your mouth, it works. Not chosen: only while crouched, which would make the hand's reach pointless.
4. **A pipe an NPC is smoking is busy.** Its mouthpiece can't be grabbed, like a mob in use in Gothic. Not chosen: taking it from him.

## Consequences

- The water pipes in Khorinis can be smoked in VR; on the flat screen nothing changes.
- Nothing is saved: the pipe is free again after a load.
- Pipes in mods with the same hose parts work the same way.
