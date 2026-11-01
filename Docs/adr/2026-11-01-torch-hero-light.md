# ADR: A torch in the hand lights the world like a flame

- Status: proposed
- Date: 2026-11-01
- Technical details: Docs/lighting.md

## Context

In VR the player holds the torch and moves it around, but the light hardly changes: the world is lit at the corners of its large polygons only, the light ignores which way a surface faces, it follows the torch in small jumps, it doesn't flicker, and the flame has no glow. In the original game the torch sits in the hero's hand at a fixed height, so this never stood out. Gothic's own torch light flickers through its color animation: Gothic 1's burning torch steps through 33 warm colors ten times a second, with a range of 15 m.

## Decisions

1. **The torch in the player's hand gets one sharp light, computed for every pixel and by the angle of each surface, that follows the torch every frame.** All other lights stay as they are. Not chosen: sharp light for every light in the world, which costs too much on Quest 2.
2. **Only the torch in the player's hand, not a dropped torch or one an NPC carries.** Those keep the normal soft light. If both hands hold a burning torch, the one grabbed last lights the world.
3. **The light has the range, color and flicker of the game's own burning torch light** and is stronger close to the flame. A light without any animation of its own gets a gentle flicker of its range instead, so a flame never looks frozen.
4. **The flame gets a soft glow while it is the sharp light.** For now every torch glows with Gothic's plain round glow texture: the torch light names Gothic's own torch lens flare, but the file that describes lens flares isn't read yet. That, and the lens flares placed in the world, stay a separate task.
5. **One version of each shader with a switch inside, not two versions.** Switching between two shader versions makes a Quest freeze for a moment the first time a lit torch is held. Not chosen: two versions, prepared during loading.
6. **Letting go of the torch fades the sharp light out over half a second while the normal torch light comes back,** so there is no dark moment. Putting the torch out, water, putting it away and leaving the world switch the sharp light off at once.
7. **It is an option, off by default,** on a new "More" page of the Immersion menu (Gothic's menus fit six rows and that menu is full once the open branches are in); the movement vignette moves to that page too. It is stored in Gothic.ini with the game's own options, so the flat-screen mode can offer it later. Not chosen: on by default, before its cost is measured on a Quest 2.

## Consequences

- With the option off the picture doesn't change; the shaders carry one small extra check and pass the surface direction along, to be measured.
- With it on, every visible surface near the torch computes one more light; to be measured in the Old Camp at night on a Quest 2.
- The light spell could use the same sharp light later.
