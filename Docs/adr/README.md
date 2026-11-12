# Architecture decision records

Why the game works the way it does. One short record per PR that decides something: which way we chose, why, and what we didn't choose. Months later this is where you find the reason behind a decision.

## When

Every PR that makes a decision someone could question later: a new feature, a new setting, a rule taken from (or deliberately different from) the original game, a trade-off. Pure bug fixes and refactorings without a choice need none.

## How

- One file per decision: `Docs/adr/<date>-<topic>.md`, e.g. `2026-10-28-vr-ledge-climbing.md`. The date instead of a running number keeps parallel PRs from colliding, and the folder sorted by name is the list of all records.
- Added in the PR it belongs to. Testing in the headset is a given, no need to note it.
- Plain words for players and developers alike: no class names, no code, no technical mechanics. Those go into the area's technical doc, which the record links.
- One line per paragraph or list item (no hard line wraps), so it reads well everywhere.
- Short: a page at most.

## Which decisions are in force

There is no status line. Where the record is tells its state:

- **On a branch / in an open PR: proposed.** It may still change in that PR, until it is merged.
- **In main: accepted and in force.** A record in main is never edited again - not its decisions, not its wording.
- **A decision changes later: write a new record.** Its header names what it replaces: `- Replaces: [<old record>](<old file>)`, or only some decisions: `- Replaces: decisions 2-3 of [<old record>](<old file>)`. The old record stays as it is.
- **Is an old decision still in force?** Search `Docs/adr/` for its file name in `Replaces:` lines. Not replaced: in force. Replaced: the newer record wins (for the decisions it names).
- **Code that should work against an accepted decision** isn't built quietly: the human decides, and the change gets its new record in the same PR.

## Template

```markdown
# ADR: <topic>

- Date: <yyyy-mm-dd>
- Replaces: [<old record>](<old file>) (only if it changes an earlier decision; "decisions 2-3 of [...]" for a part)
- Technical details: Docs/<area>.md

## Context

What the problem was, and how the original game handles it.

## Decisions

1. **The decision in one sentence.** Why. Not chosen: the alternative, and why not.

## Consequences

- What changes because of it, what it costs, what is still open.
```
