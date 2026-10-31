# ADR: Bringing the game-completion branch to main, wave 2

- Status: proposed
- Date: 2026-10-31
- Technical details: Docs/branch-split-assessment.md

## Context

The game-completion branch (BoroBongo's play-tested branch) still holds about 620 commits on top of main: fights, perceptions, death, NPC movement, VR inventory, trade, ranged weapons, magic and save/load. Wave 1 (engine robustness, mods, dev tools, dialogs, world objects, lighting, videos) is on main since PRs #395-#401. The branch keeps growing and drifting from main, and one huge PR can't be reviewed or tested.

## Decisions

1. **Eleven PRs in dependency order, foundations first.** VM functions and engine fixes, then perceptions, death and knockout, NPC fights, NPC movement, the VR hero's body, VR inventory, trade, ranged weapons, magic, and save/load last. Each PR is small enough to test in about 10-20 minutes. Not chosen: the wave 1 order by theme only, because later themes need the foundations and testing them out of order means testing everything twice.
2. **Stacked PRs.** Each PR builds on the one before it, so its diff shows only its own changes and reviews can start before the lower PR is merged. When a lower PR is merged, the next one is retargeted to main. Not chosen: independent PRs from main, because most themes depend on the same scripts functions and would conflict with each other.
3. **Same flag policy as wave 1.** Settings that switch a feature or a VR design choice stay, as do tunable values. Settings that only guard a fix for the original game's behaviour go, the fixed behaviour is the only one left. This keeps the developer config from growing by hundreds of switches nobody turns off.
4. **Each PR carries the final state of its theme, not the branch's history.** The branch was play-tested in that state; replaying hundreds of small commits would bring back intermediate bugs.

## Consequences

- Reviews focus on architecture (where code lives, what depends on what), the play tests on the listed steps per PR.
- The branch is the source of truth until the last PR is merged; fixes found while testing go into the PR and back into the branch.
- Save/load (the last PR) still needs its decision on how much of the game state goes into our own save file.
