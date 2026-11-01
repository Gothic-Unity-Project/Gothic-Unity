# ADR: Particle effects follow Gothic's own definitions

- Status: proposed
- Date: 2026-10-31

## Context

Fires, torches, smoke and many other effects in Gothic are particle effects. In our game they are switched off by default, and switched on they looked wrong: they burned at the wrong place, emitted a hundredth of their particles, flew in the wrong direction, lay flat and vanished half way through their life. BoroBongo's branch fixed much of this, and OpenGothic shows how the engine reads the definitions.

## Decisions

1. **The effects follow Gothic's definitions, like the engine.** Particles per second, life, direction and speed, gravity, shape, size, color and blending are read as Gothic means them, in its units. A torch keeps about 28 particles alive, a fire about 30, its smoke about 60, as in the original.
2. **Cheap enough for Quest 2.** One material per texture and blending is shared by all effects, particles don't collide with the world, effects far away pause, and stand-alone effects switch off at medium distance. Emission is capped at 500 particles per second.
3. **Placed and pointed like the engine.** An effect inside a fire's own setup sits relative to the fire; every other effect of a world sits at the place Gothic stores for it, also when it belongs to another object (a wall fire bowl's flame). Flames and smoke fly in the world's directions unless the effect says it follows its object, so they rise however a fire stand or torch is turned. Particles move along with their emitter only where the effect asks for it. Not chosen: always following the object - a tilted fire bowl or torch blew its flames sideways.
4. **Still switched off by default.** The game setting for particle effects stays off until their cost is measured on a Quest 2 in the Old Camp at night.

## Consequences

- Once switched on, fires and torches burn with flames and smoke where Gothic puts them.
- Animated textures of some effects (leaves, water vapour) aren't played yet; those effects show their first frame.
- Particle controllers that scripts start and stop show their effect only if they start running.
