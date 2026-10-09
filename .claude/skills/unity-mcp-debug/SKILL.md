---
name: unity-mcp-debug
description: Runtime debugging workflow for Gothic-UnZENity with the Unity MCP server - read console/exceptions before editing code, filter huge logs, never recompile during play mode, fall back to Editor.log. Use for any bug report from a play session ("X doesn't work in game", exceptions, NPC behaves wrong, game paused on error).
---

# Debugging a running Unity session

## 1. Understand before you touch anything

Read the relevant system (services, domain, adapters involved) and the Daedalus scripts behind the behaviour,
trace the data flow end to end, form a hypothesis - then verify with targeted queries. Don't fire random checks.
If the bug looks random, look for the systemic cause rather than chasing single instances.

## 2. Read logs BEFORE any code edit

Saving a `.cs` file while the Editor is open triggers a domain reload that **wipes the console** and all runtime
state. So:

1. Check `mcpforunity://editor/state` (play mode? paused? compiling?).
2. Read exceptions first, then warnings, then the specific tag.
3. List what you found and ask the developer per finding: "found X, I propose fix / ignore - ok?"
4. Only then edit code.

## 3. Always assume the console has 999+ entries

Never read the console unfiltered - large reads time out or crash MCP. Use `read_console` with `types`,
`filter_text` and a small `count` (≤ 10-15). Know the tag you're looking for first (e.g. `[FightService]`,
`[NpcAi]`, `Exception`).

Filter order:
1. `Exception` / errors
2. `not yet implemented in DaedalusVM` - an unimplemented external returns 0 silently and is very often the root
   cause of AI loops, NPCs not fighting back, stuck quests. One of these can invalidate every other hypothesis.
3. The subsystem tag.

If MCP is down or times out, grep Unity's `Editor.log` (Windows: `%LOCALAPPDATA%\Unity\Editor\Editor.log`,
macOS: `~/Library/Logs/Unity/Editor.log`, Linux: `~/.config/unity3d/Editor.log`). It is huge - grep for the tag,
take the tail only. It also keeps logs lost to "Clear on Play". After an Editor crash, read `Editor-prev.log`;
an `Assets/_Recovery/` folder means play mode crashed.

For Daedalus-level tracing (which externals a script called and with what), enable ZSpy logs in the DeveloperConfig
- very verbose, turn on only while investigating.

## 4. Never compile during play mode

Check editor state right before every `refresh_unity` / compile. If `is_playing` is true, batch your edits and
compile after the developer stops the game (or ask). A reload in play mode destroys the scene they prepared for you.

## 5. Native crashes

Some crashes come from native code (ZenKit, dmusic, Burst animation jobs) and cannot be caught in C#
(`AccessViolationException`, Burst `TransformStreamHandle` aborts, native OOM). Don't wrap them in try/catch and
call it fixed - find what input triggers them and avoid passing it (skip, pre-filter, feature flag). To diagnose
safely outside the Editor, a small .NET console app referencing the ZenKit / DirectMusic managed DLLs with
`ZenKit.Logger.Set(LogLevel.Trace, ...)` shows exactly where parsing fails.

## 6. After the fix

- Add `Logger.Log/LogWarning(..., LogCat.X)` to the silent-failure path you just found, so next time it's visible.
- Tell the developer exactly what to test in game to confirm.
