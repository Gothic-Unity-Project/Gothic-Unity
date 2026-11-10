---
name: review
description: Reviews the current branch locally before a PR - runs the architecture-reviewer and gothic-reviewer
    agents, stores their reports unedited in .claude/review/<branch>/, compares them with the previous review and
    collects the author's response per open finding. Use when asked to review the branch, the changes or the code
    before committing or pushing; /commit-pr reads its results.
---

# Review

The reports are what the human PR reviewer reads later, so they stay exactly as the agents wrote them.

## Files

`.claude/review/<branch>/` (git-ignored, `/` in the branch name becomes `-`), `<sha>` = short `HEAD` at review time:
- `<sha>-architecture.md`, `<sha>-gothic.md` - the agents' reports, unedited
- `<sha>-response.md` - the author's answer per open finding

A review is **current** while no file other than `*.md` changed after the reports were written: check
`git diff --name-only <sha>` plus untracked files from `git status --short` against the reports' modification time.
Committing doesn't make a review outdated, changing code does.

## Steps

1. Run the `architecture-reviewer` and `gothic-reviewer` agents in parallel on `origin/main...HEAD`, plus `--wip`
   when there are uncommitted changes.
2. Save each report **unedited** (the agents are read-only, you write the files). Never shorten or drop findings.
3. Compare with the previous review in the folder, by file and problem, not line number: resolved, new, still open.
4. Show the human the verdicts, the counts and the comparison. Show the findings as **one numbered table**
   (`# | Agent | Severity | File | Finding | Status` with status new / still open / resolved), so the author can
   answer by number.
5. For every open finding, ask the author: fix it now, follow-up (issue link), or disagree (why). Their answers go
   into `<sha>-response.md`, never into the reports, as the same numbered table plus an `Answer` column.
6. Every approved fix runs in a **sub-agent** (`general-purpose`), never in the main session. Pick the model by
   complexity:
   - `haiku` - mechanical: rename, style, missing log, comment/doc, one-line guard
   - `sonnet` - a normal fix inside one class or service, clear from the finding
   - `opus` - crosses layers or services, Daedalus/save-load semantics, DI wiring, or needs a design decision
   Give the agent the finding number, the report text and the relevant files; tell it to follow `CLAUDE.md`, keep
   the change to that finding and **commit once done** (style from the `commit-pr` skill, one commit per finding,
   no push). Independent fixes may run in parallel unless they touch the same files.
