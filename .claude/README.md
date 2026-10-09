# Claude Code setup for Gothic-UnZENity

Shared skills, agents and settings so Claude Code knows the project's pitfalls. `CLAUDE.md` in the repo root covers
architecture; these files cover workflow and hard-won gotchas.

## Skills (`skills/`) - loaded automatically when relevant, or call with `/<name>`

| Skill | Use for |
|---|---|
| `daedalus-bridge` | externals, AI states, perceptions, XP/attributes, inventory - anything crossing C# ↔ Daedalus |
| `unity-mcp-debug` | debugging a play session via Unity MCP: logs first, filtering, no compile in play mode |
| `mod-triage` | getting a G1/G2 mod to boot and play, recognising native/engine blockers |
| `feature-checklist` | implementing features: DeveloperConfig flags + asset values, logging, DI, layers, save/load |
| `commit-pr` | staging/committing in reviewable steps, PR description with manual test steps |

## Agents (`agents/`) - ask e.g. "use daedalus-scout to look up Npc_GetNextTarget"

| Agent | What it does |
|---|---|
| `daedalus-scout` | read-only lookup in Gothic scripts / OpenGothic / ZenKit, returns a short brief |
| `gothic-reviewer` | reviews your diff against project-specific pitfalls before commit/PR |
| `playtest-planner` | turns branch changes into an in-game test checklist for G1/G2 |

## Settings

- `settings.json` - read-only git/GitHub/MCP permissions and a hook that blocks Grep over the whole repo.
  Personal overrides go in `settings.local.json` (not shared).
- Unity MCP: see `.mcp.json` in the repo root (MCP for Unity server on `http://127.0.0.1:8080/mcp`).

## Local paths

Skills don't hardcode anyone's disk layout. Tell Claude where your Gothic MDK scripts live (or put it in your
personal `CLAUDE.local.md`), otherwise it uses the public script repos on GitHub.
