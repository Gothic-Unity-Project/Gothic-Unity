# ADR: The hero can die

- Status: proposed
- Date: 2026-10-30

## Context

The hero could only be knocked out. Monsters and bandits beat him down again and again, his hit points fell far below zero, and he always got up after ten seconds. In Gothic the same rule decides for the hero as for everybody else: whoever is beaten by someone hostile to him dies, everybody else is knocked out. Monsters and bandits are hostile to the hero, so they kill him. Scripts also ask an NPC to finish off a knocked out enemy (the finishing move), which didn't do anything here.

## Decisions

1. **The hero follows the engine's rule.** Monsters and hostile humans kill him, the others knock him out. Between the hero and an NPC the NPC's attitude counts, both ways, like in the engine.
2. **The finishing move works.** The NPC walks to the knocked out enemy, turns and stabs; the enemy dies as the stab starts. It works on the hero too, as in the original scripts.
3. **The hero's death ends the game.** He can't move or act until a game is loaded, his death scream plays, and the scripts' death state runs for him like in the engine. The menu then offers loading, a new game and the settings, but no saving or continuing.
4. **In VR the view falls like a knockout and darkens, then the menu opens after five seconds.** The original shows the dead hero from a circling camera until the player opens the menu. In VR there is no outside camera, so the menu comes by itself. The dark veil hides while the menu is open, so it can be read.

## Consequences

- Fights are dangerous again; players need to save.
- A wrong attitude now kills the hero instead of knocking him out, so attitude bugs show up harder.
- Not covered yet: deaths that don't come from a hit or a script lowering the hit points, such as falling, drowning or magic, because those don't hurt the hero yet.
