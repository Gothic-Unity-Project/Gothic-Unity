# ADR: Codebase health check

- Status: proposed
- Date: 2026-10-31
- Technical details: .claude/agents/codebase-health.md, .claude/scripts/health-metrics.py

## Context

Many branches are built, reviewed and fixed in parallel, a lot of it with AI help. Each change is reviewed on its own, but nobody looks at what all of them do to the code together. The worry: the code slowly turns into spaghetti - big classes grow, the same rule appears in several places, workarounds pile up, the game gets slower on Quest 2 and every fix needs more reading.

## Decisions

1. **A separate check looks at the whole code with everything merged, not at single changes.** The existing reviewers judge one change at a time and can't see slow decay. Not chosen: adding this to the existing reviewers, because their job is a quick verdict on one change.
2. **It gives four scores from 1 to 10: performance, code quality, extendability and how much one must read to fix or add something.** Each score follows a fixed description of what each level means, and must name numbers and code places as evidence, so two runs can be compared. Not chosen: an overall feeling without a scale, which nobody can compare over time.
3. **Numbers come from a script, not from the model.** The script counts file sizes, duplicated code, scattered game-version checks, per-frame work, how often files change and how much code a typical change touches. It costs nothing and gives the same result every time, and the model only reads the places the numbers point to. Not chosen: letting the model read everything, which is expensive and not repeatable.
4. **Each run keeps its numbers, and the next run reports the trend.** A number that gets worse is the real warning sign. A score may only jump by more than one step if the numbers moved too.
5. **It runs on a different, independent model and doesn't read the earlier review reports before scoring.** So it isn't just repeating the opinion of the reviewers that shaped the code.
6. **It never runs on its own.** A run uses a large model and costs the user's tokens, so Claude may only suggest it, and it starts only when the user asks for it. Not chosen: running it automatically after merges or before PRs.

## Consequences

- Reports and numbers stay on the developer's machine (in the ignored Claude folder), not in the repository.
- Static reading can't measure frame times: the performance score is an estimate until the game is measured on a Quest.
- The script uses simple text matching, so its counts are trends, not exact truths.
