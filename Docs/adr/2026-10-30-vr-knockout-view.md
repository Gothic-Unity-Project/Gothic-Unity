# ADR: What the VR player sees while the hero is knocked out

- Status: proposed
- Date: 2026-10-30

## Context

When the hero is beaten in a fight, he lies on the ground for a while and can't move. In the original game the camera shows him lying there. In VR the player's view is their own head, so nothing showed the knockout except that moving stopped working, which feels like a bug.

## Decisions

1. **The view goes down to the ground behind a quick fade, and is never turned.** The player looks around from where the hero lies. Not chosen: forcing the view to look up at the sky, which means tilting the whole world around the player and makes many players sick, worst of all right after a hit.
2. **A grey veil over the view clears while the knockout runs - it is the timer.** Heavy right after the fall, light just before getting up. Not chosen: a number or a bar counting down, which is a menu element inside the world; and a grey filter alone at full height, which doesn't feel like lying on the ground. Real desaturation was left out because a full-screen effect costs too much on Quest 2.
3. **A slow heartbeat pulses in both controllers while lying.**
4. **Getting up fades out and back at the normal height.** Loading a game during a knockout ends it at once.

## Consequences

- The knockout reads as one: fall, lie, come to, stand up.
- The hero's death will reuse it later: the same fall, with the veil going to black and a menu to load a game.
- The player's body follows the lowered view (like crouching), which nothing can touch while the player is locked anyway.
