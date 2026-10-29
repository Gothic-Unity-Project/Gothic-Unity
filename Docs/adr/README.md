# Architecture decision records

Why the game works the way it does. One short record per PR that decides something: which way we chose, why, and what we didn't choose. Months later this is where you find the reason behind a decision.

## When

Every PR that makes a decision someone could question later: a new feature, a new setting, a rule taken from (or deliberately different from) the original game, a trade-off. Pure bug fixes and refactorings without a choice need none.

## How

- One file per decision: `Docs/adr/<date>-<topic>.md`, e.g. `2026-10-28-vr-ledge-climbing.md`. The date instead of a running number keeps parallel PRs from colliding, and the folder sorted by name is the list of all records.
- Added in the PR it belongs to. Update its status when the PR is merged or replaced by a later decision. Testing in the headset is a given, no need to note it.
- Plain words for players and developers alike: no class names, no code, no technical mechanics. Those go into the area's technical doc, which the record links.
- One line per paragraph or list item (no hard line wraps), so it reads well everywhere.
- Short: a page at most.

## Template

```markdown
# ADR: <topic>

- Status: proposed / accepted (PR #...) / replaced by <record>
- Date: <yyyy-mm-dd>
- Technical details: Docs/<area>.md

## Context

What the problem was, and how the original game handles it.

## Decisions

1. **The decision in one sentence.** Why. Not chosen: the alternative, and why not.

## Consequences

- What changes because of it, what it costs, what is still open.
```
