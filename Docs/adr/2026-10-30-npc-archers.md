# ADR: NPCs shoot with bows and crossbows

- Status: proposed
- Date: 2026-10-30

## Context

Archers and crossbowmen drew their weapons and then did nothing that hurt: ranged attacks only wrote a warning. Gate guards in Gothic 1 are ranged fighters, so they never fought. In Gothic an archer aims, shoots an arrow that flies and can miss, and reloads.

## Decisions

1. **Shots follow the engine.** The archer aims for a moment, shoots and reloads, keeping his face to the enemy. Each shot takes one arrow or bolt from his inventory; without any he doesn't shoot. His fight moves come from the game's ranged fight tables, so he keeps his distance as the scripts intend.
2. **Arrows really fly.** At the engine's speed on a falling arc aimed at the target. They hit if they pass close to him, stop at walls, and a missed arrow stays a while. In VR the player can step out of an arrow's way.
3. **Hitting and damage use the engine's numbers.** Up close every shot hits; at the reference distance the shooter's own chance counts (Gothic 1 his dexterity, Gothic 2 his hit chance), beyond 45 m nothing hits. The weapon's damage goes against the target's protection; Gothic 2 adds the shooter's dexterity, Gothic 1 has critical hits by talent. Arrows kill rather than knock out, like in the engine, and leave those lying on the ground alone.

## Consequences

- Archers and guards with crossbows are dangerous again.
- Scripts must give archers ammunition, as in the original; one without arrows only aims.
- Mages still don't cast; magic comes separately.
