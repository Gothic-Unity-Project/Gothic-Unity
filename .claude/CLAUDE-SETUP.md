# Claude Code – skills & agents for Gothic-UnZENity

## Install

Until the PR is merged, copy the files manually. After the merge, `git pull` is enough.

1. Copy into `.claude/` of your repo clone:
   ```
   .claude/skills/      (5 folders)
   .claude/agents/      (3 .md files)
   .claude/hooks/block-root-grep.sh
   .claude/settings.json
   ```
2. Optional, for Unity MCP: `.mcp.json` in the repo root (MCP for Unity server at `http://127.0.0.1:8080/mcp`).
3. Windows: Git Bash is required for the hook (Claude Code needs it anyway).
4. Restart Claude Code in the repo folder and check: typing `/` lists the skills, `/agents` lists the agents.

Put your local paths (e.g. to the MDK scripts) in `CLAUDE.local.md` or in the prompt – the skills don't hardcode them.

## Usage

- **Skills** load automatically when your prompt matches their description. Force one with `/name`.
- **Agents** run as separate sessions with their own context. Call them by name: "use daedalus-scout to …".
  They return a short report, so your main session stays clean.
- **settings.json** pre-allows read-only commands (git status/diff/log, GitHub, MCP console reads) and has a hook
  that blocks Grep over the whole repo or the whole `Assets/` folder, because that takes forever. Always pass a subfolder.

## Skills

| Skill | Example prompt | What it does |
|---|---|---|
| `daedalus-bridge` | "Implement the `Npc_HasEquippedRangedWeapon` external" | Reads the Gothic scripts and looks for `B_*` helpers first, then writes C#. Watches for the usual traps: NpcInstance vs vob, Packed vs GetItem inventory, `ItemInstance.Name` ≠ symbol name, VAR FLOAT params, nested `vm.Call`. |
| `unity-mcp-debug` | "Game is paused, NPC doesn't fight back – find out why" | Before touching code, reads exceptions and "not yet implemented in DaedalusVM" warnings via MCP (filtered). Never compiles during play mode. Falls back to `Editor.log` when MCP is down. |
| `mod-triage` | "Run mod X, it crashes in the menu" | Goes through a checklist in boot order: VFS/archives → VM → menu → precaching → gameplay. Also tells you when the problem is native (ZenKit/Union/LeGo) and can't be fixed in C#. |
| `feature-checklist` | "Add an EnableX flag and feature Y" | Flag in `DeveloperConfig.cs` **plus** its value in `Production.asset`, Logger with `LogCat`, Reflex registration, dispatcher events, physics layers, save/load impact. |
| `commit-pr` | "Commit this and write the PR description" | `git add` and `git commit` as separate steps (so you review first). Per-file TL;DR. PR with manual test steps and the `pipeline-test-build` label. |

## Agents

| Agent | Example prompt | What it does |
|---|---|---|
| `daedalus-scout` | "Use daedalus-scout: how does `Npc_GetNextTarget` work?" | Read-only lookup in the Gothic scripts, OpenGothic and ZenKit. Returns the signature, semantics, call sites, existing `B_*` helpers, and whether we've implemented it. |
| `gothic-reviewer` | "Run gothic-reviewer on my diff before I commit" | Reviews your diff for project pitfalls: Daedalus sync, `!` force-unwraps, missing logs, flags without asset values, save/load, collision matrix, code style. |
| `playtest-planner` | "playtest-planner: test plan for this branch" | Turns the diff and the "TODO TEST ME" flags into a G1/G2 checklist: required flags, in-game steps, expected result, logs to watch, save/load check. |
