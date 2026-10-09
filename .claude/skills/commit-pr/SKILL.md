---
name: commit-pr
description: How to stage, commit and open PRs in Gothic-UnZENity - separate add/commit steps for review, commit message style, PR description with manual test steps, CI build label. Use when asked to commit, push, write a changelog, or create/describe a pull request.
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
6. Never push, force-push or amend without being asked. Work on a branch, not `main`.

## After coding, always print

A TL;DR with one line per changed file:
- `FightService.cs` - gate force-switch on Friendly attitude
- `Production.asset` - EnableFoo: 1

## Pull request

Branch from `main`. Description sections:
- **What** - player-visible change + main technical points
- **Why** - bug / issue link
- **How to test manually** - concrete in-game steps for G1 and/or G2 (where to go, who to talk to, what you should
  see), and which DeveloperConfig flags must be on
- **Save/load** - what happens to the new state on save + load
- **Known gaps / follow-ups**

Add the `pipeline-test-build` label when the PR needs a CI build (Windows64 / Quest / Pico).
