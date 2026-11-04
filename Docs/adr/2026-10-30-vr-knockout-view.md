# ADR: What the VR player sees while the hero is knocked out or dead

- Status: proposed
- Date: 2026-10-30, updated 2026-11-04 (together with the VR hero body)

## Context

When the hero is beaten in a fight, he lies on the ground for a while and can't move. In the original game the camera shows him lying there. In VR the player's view is their own head, so nothing showed the knockout except that moving stopped working, which feels like a bug. When the hero dies, the original shows him from a circling camera until the player opens the menu.

## Decisions

1. **The view goes down to the ground and is never turned.** With the VR hero body, the camera follows the body's head as it falls and stands up (see the VR hero body record). Without the body, the view goes down behind a quick fade. The player looks around from where the hero lies. Not chosen: forcing the view to look up at the sky, which means tilting the whole world around the player and makes many players sick, worst of all right after a hit.
2. **A grey veil over the view clears while the knockout runs - it is the timer.** Heavy right after the fall, light just before getting up. Not chosen: a number or a bar counting down, which is a menu element inside the world; and a grey filter alone at full height, which doesn't feel like lying on the ground. Real desaturation was left out because a full-screen effect costs too much on Quest 2.
3. **A slow heartbeat pulses in both controllers while lying.**
4. **Getting up raises the view again**, with the body or behind a fade without it. Loading a game during a knockout ends it at once.
5. **When the hero dies, the view falls the same way and the veil darkens, then the menu opens after four seconds.** In VR there is no outside camera, so the menu comes by itself, also without the VR body. The veil hides while the menu is open, so it can be read.

## Consequences

- The knockout reads as one: fall, lie, come to, stand up.
- The player's body follows the lowered view (like crouching), which nothing can touch while the player is locked anyway.
- The body only shows the hero's state; deciding death and locking the player stays with the fight rules.
