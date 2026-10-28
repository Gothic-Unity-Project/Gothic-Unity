---
name: commit-pr
description: How to stage, commit and open PRs in Gothic-Unity - separate add/commit steps for review,
    commit message style, PR description with manual test steps, CI build label. Use when asked to commit, push,
    write a changelog, or create/describe a pull request.
---

# Commit & PR

## Commit

1. Show `git status` / `git diff --stat` and point out unrelated changes (Unity touches `.asset`, `.unity`,
   `ProjectSettings/*` on its own - don't sweep those in unless they belong to the change).
2. `git add <explicit files>` - **its own tool call**, so the developer can review the staged diff.
3. `git commit` - a separate call, after the developer approved the staging. Never chain `git add && git commit`.
4. Include `.meta` files for any new asset/script.
5. Message style (see `git log`): `feat: ...`, `fix: ...`, `docs: ...`; describe the player-visible effect, not
   just the code.
6. End every commit message with `Co-Authored-By: Claude <MODEL VERSION> <noreply@anthropic.com>`, using the model
   actually running (e.g. `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` or
   `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`).
7. Never push, force-push or amend without being asked. Work on a branch, not `main`.

## After coding, always print

A TL;DR with one line per changed file:
- `FightService.cs` - gate force-switch on Friendly attitude
- `Production.asset` - EnableFoo: 1

## Review results

The review is a separate step: the `review` skill writes its files to the git-ignored
`.claude/review/<branch>/`. Before creating or updating a PR, read the newest review there. If there is none, or
it isn't current (rule in `review` > "Files"), tell the author and run `review` first. Never write or
edit review reports here.

The texts sent to GitHub go into the same folder: `pr-body.md` and `<sha>-comment.md` (`<sha>` = the pushed commit).

## Pull request

Branch from `main`. Description sections (same as `.github/PULL_REQUEST_TEMPLATE/pull_request_template.md`):
- **What** - player-visible change + main technical points
- **Why** - bug / issue link
- **How to test manually** - concrete in-game steps for G1 and/or G2 (where to go, who to talk to, what you should
  see), and which DeveloperConfig flags must be on. Add it as checkboxes like "- [ ] - ...". The
  `playtest-planner` agent drafts them.
- **Save/load** - what happens to the new state on save + load
- **Known gaps / follow-ups**
- **AI usage** - what the AI did, what the human did
- **AI review** - summary of the newest review and the author's response, generated from the files, never written
  by hand:
  ```markdown
  ## AI review of a1b2c3d
  - architecture-reviewer: fits with concerns, 3 findings (2 now, 1 later)
  - gothic-reviewer: 1 bug, 2 nits
  - since 9f8e7d6: 2 resolved, 1 new. Full reports in the comments.

  ### Author's response
  - architecture 2 (NpcAiService size): follow-up #123
  ```

Each new review also becomes a PR comment `<sha>-comment.md`: the "since" line, then both reports unedited, each in
`<details><summary>agent: verdict, counts</summary>`. A PR without changes since the last posted review gets no
new comment.

A PR that decides something (new feature, setting, trade-off, a rule different from the original game) adds a
decision record `Docs/adr/<date>-<topic>.md` in plain words (rules and template in `Docs/adr/README.md`).

End every PR description with this line:

```
🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

Add the `pipeline-test-build` label when the PR needs a CI build (Windows64 / Quest / Pico).

## Sending it to GitHub

Opening or updating a PR is the request to push the branch. Write `pr-body.md` and `<sha>-comment.md` first, then:
- `gh auth status` succeeds: `gh pr create --base main --body-file pr-body.md` (new PR) or
  `gh pr edit --body-file pr-body.md` (existing), then `gh pr comment --body-file <sha>-comment.md`.
- No `gh`: print both file paths and tell the author to paste them into GitHub ("Compare & pull request" after the
  push, or edit the description and add a comment).
