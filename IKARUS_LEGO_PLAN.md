# Ikarus / LeGo Compatibility — Implementation Plan

Reference implementation: OpenGothic, `game/game/compatibility/`
- `directmemory.cpp` / `.h` — main entry point, mirrors what `VmIkarusLeGoDomain.cs` is meant to be
- `mem32.cpp` / `.h` — virtual 32‑bit heap with type-aware read/write callbacks
- `mem32instances.h` — fake C++ structs (`oGame`, `oWorld`, `zCView`, `zString`, `zCPar_Symbol`, …) that the heap pretends to host
- `cpu32.cpp` / `.h` — tiny x86 decoder used for engine-text overrides + `ASMINT_CallMyExternal`
- `phoenix.cpp` / `.h` — unrelated (mesh packing helper)

Current UnZENity entry point: `Assets/UnZENity-Core/Scripts/Domain/Vm/VmIkarusLeGoDomain.cs`
Wired from: `BootstrapDomain.LoadIkarusLeGo()` (only triggers when `IKARUS_VERSION` symbol exists)

## What ZenKitCS already exposes (relevant API surface)

The existing file's "needs ZenKit C# API extensions not yet available" comment is **out of date**. ZenKitCS already has:

| Need | C# API |
|---|---|
| `pop_reference()` | `DaedalusVm.PopReference(out int idx, out DaedalusInstance? ctx)` |
| `top_is_reference()` | `DaedalusVm.IsTopOfStackReference` |
| `pc()` | `DaedalusVm.ProgramCounter` |
| `unsafe_jump(pc)` | `DaedalusVm.JumpUnsafe(uint pc)` |
| `register_access_trap(cb)` | `DaedalusVm.SetAccessTrapCallback(AccessTrapCallback)` |
| `set_access_trap_enable(true)` | `DaedalusSymbol.UseAccessTrap` |
| `set_local_variables_enable(true)` | `DaedalusSymbol.UseLocalVariables` (for LOCALS / PermMem) |
| `offset_as_member()` | `DaedalusSymbol.OffsetAsMember` |
| `class_size()` | `DaedalusSymbol.ClassSize` |
| `find_symbol_by_address(pc)` | `DaedalusScript.GetSymbolByAddress(int)` |
| `instruction_at(pc)` | `DaedalusScript.GetInstruction(int)` |
| Global `self/other/victim/hero/item` | `DaedalusVm.GlobalSelf` setters + getters |
| Push/Pop raw ints/insts | private `Push<T>` / `Pop<T>` (used inside `OverrideFunctionUnsafe`) |

What's **not** in ZenKitCS:
- `unsafe_call(sym)` and `unsafe_get_gi/unsafe_set_gi` — needed for OG's `ZCPARSER__CREATEINSTANCE` shim. Workaround: `Call(symId)` after manually swapping `SELF.SetInstance(...)`.
- `find_parameters_for_function(func)` — only needed by OG's `directCall`; we can substitute `Call(symId)`.
- `OverrideFunctionUnsafe` is private. We need a public/internal sibling for `repeat / while / mem_goto / _@ / MEM_GetIntAddress / MEM_ReadStatArr / MEM_WriteStatArr / MEM_CallByID / MEM_CallByPtr / memint_*` — they all need raw `vm` access mid-call to push/pop without the typed wrapper. **Add a thin `OverrideFunctionRaw(string name, Action<DaedalusVm> cb)` to `DaedalusVm`** (it's a one-line wrapper around `OverrideFunctionUnsafe`).

## What's already implemented in `VmIkarusLeGoDomain.cs`

Working (no changes needed):
- Virtual heap (`Alloc/Free/Realloc/ReadInt/WriteInt/CopyBytes/ReadString/ReadBytes`)
- Init NOPs (`MEMINT_SetupExceptionHandler`, `MEM_InitStatArrs`, `MEM_InitRepeat`, `MEM_GetCommandLine`, `MEM_InitAll`)
- `MEM_Alloc / MEM_Free / MEM_Realloc / MEM_ReadInt / MEM_WriteInt / MEM_CopyBytes / MEM_ReadString`
- `MEM_ReadStatArr / MEM_WriteStatArr` (by symbol index — works because of optional `context` param on `DaedalusSymbol.GetInt/SetInt`)
- `MEM_CallByID / MEM_CallByPtr` (basic)
- `MKF / TRUNCF / ROUNDF / ADDF / SUBF / MULF / DIVF` (math)
- `STR_SubStr / STR_Upper / SB_TOSTRING`
- `MEM_GetGothOpt / MEM_SetGothOpt / MEM_GothOptExists` (Union fake), Mod*Opt warn-only
- `MEM_PrintStackTrace / MEM_SendToSpy`
- `ASMINT_Init` (allocates a fake stack)
- `WRITENOP / MEM_SETKEYS / INIT_QUIVERS_ALWAYS / PRINT_FIXPS` (dummied per OG)

Stubbed today, **but actually achievable** with current C# API:
- `repeat / while / mem_goto`
- `_@ / _@s / _@f / MEM_GetIntAddress / MEM_GetStringAddress / MEM_GetFloatAddress`
- `LOCALS`
- `MemoryInstance` byte-level field addressing (currently per-field dictionary)
- `MEM_AssignInst` (currently records pointer only — does not back the symbol's instance with our `MemoryInstance`)
- `MEM_PtrToInst` round-trip with `MEM_InstToPtr` for non-Ikarus instances

Not implemented at all:
- Loop bytecode scan + `loopBacktrack / loopPayload` maps + access-trap based END/CONTINUE/BREAK
- `traceBackExpression` (needed for `while` to compute back-jump)
- Sorted `symbolsByAddress` table for PC → function lookup
- `_takeref` (the real `_@` / `MEM_GetIntAddress`)
- Engine memory map (oGame*, GameMan*, zTimer, zFactory*, zCFontMan*, focusList, …)
- `.text` allocations for engine-code addresses mods overwrite
- Symbol-table mirror (`zCParser` / `zCPar_Symbol` / `ScriptVar` callbacks)
- `_AI_FUNCTION_EVENT` "CALL …" parser (`DirectMemory::eventPlayAni`)
- WinAPI / utility shims (`LoadLibrary`, `GetUserNameA`, `GetLocalTime`, `GETBUFFERCRC32_G2`, `SYSGETTIMEPTR_G2`, …)
- Hook engine (`HookEngineI`, `_FF_Hook` per-frame tick)
- LeGo `FINAL`, real `LOCALS` (PermMem), `ZCCONSOLE__REGISTER`
- `ZCPARSER__GETINDEX_G2`, `ZCPARSER__CREATEINSTANCE`
- LeGo View / FontMan / NPC / world hooks (mostly engine-text intercepts)
- Trialoge full implementation (`TRIA_Invite/Start/Next`)
- `Cpu32` x86 emulator (out of scope for first pass)

---

## Phased Plan

### Phase 0 — ZenKitCS prerequisite
**File:** `ZenKitCS/ZenKit/DaedalusVm.cs`
- Expose `OverrideFunctionUnsafe` as `public void OverrideFunctionRaw(string name, Action<DaedalusVm> cb)` so the domain can pop/push manually.
- Expose `Push<T>(T)` / `Pop<T>()` as `internal` (or write `PushInt/PushString/PushFloat/PushInstance` public helpers). `PopReference` is already public.
- Verify `JumpUnsafe`, `ProgramCounter`, `IsTopOfStackReference`, `SetAccessTrapCallback` work end‑to‑end (write a minimal test in `Assets/UnZENity-Tests/PlayMode/`).

Without Phase 0 the rest is blocked.

### Phase 1 — Loop control (`repeat` / `while` / `mem_goto`)
OpenGothic: `directmemory.cpp:325-640`

Implementation steps:
1. **`SetupFunctionTable()`** — at `Init()`, build `List<DaedalusSymbol> _symbolsByAddress` containing all function symbols with `Address != 0`, sorted by `Address`. Add `FindSymbolByAddress(int addr)` doing a binary search (lower_bound + back-step), mirroring `directmemory.cpp:722-735`.
2. **`SetupIkarusLoops()`** — bytecode scan:
   - For each symbol `END / BREAK / CONTINUE`: set `sym.UseAccessTrap = true`.
   - Walk every instruction (`for pc in [0, vm.size)`, `vm.GetInstruction(pc)` advancing by `instr.size`). NOTE: ZenKitCS does **not** expose `vm.size` (`DaedalusScript` instruction-buffer length). Need a small ZenKitCS addition: `DaedalusScript.InstructionBufferSize` (`Native.ZkDaedalusScript_getSize` if available, otherwise scan symbols and pick the max `Address` + walk until `Rsr`).
   - Build:
     - `_loopBacktrack: Dictionary<uint pcOfPushvEnd, uint backJmpPc>` and `Dictionary<uint pcOfPushvContinue, uint loopHeadPc>` (mirrors `directmemory.cpp:355-397`)
     - `_loopPayload: Dictionary<uint loopHeadPc, (DaedalusSymbol i, int len)>` populated at runtime by `repeat`.
3. **`Repeat(vm)`** raw override — `len = vm.PopInt()`; `(iSym, _, _) = vm.PopReference()`; if `len == 0 || iSym == null` → `vm.JumpUnsafe(loopBacktrack[pc] - instrSize)`. Otherwise `iSym.SetInt(0)` and stash `_loopPayload[pc] = (iSym, len)`. Mirrors `directmemory.cpp:540-558`.
4. **`While(vm)`** — `cond = vm.PopInt()`; if zero → `vm.JumpUnsafe(loopBacktrack[pc] - instrSize)`. The loopBacktrack target is computed from `TraceBackExpression`. Mirrors `directmemory.cpp:560-570`.
5. **`MemGoto(vm)`** — pops label id, walks bytecode of the current function looking for `BL @mem_label` preceded by `PUSHI <id>`, jumps to it. Mirrors `directmemory.cpp:572-602`.
6. **Access trap callback** — registered via `vm.SetAccessTrapCallback(LoopTrap)`. When `PUSHV @END` fires inside a loop:
   - If `_loopPayload` has no entry → it's a `while`; jump back to the head.
   - Otherwise increment `i`; jump back if `i+1 < len`, else erase the payload.
   Mirrors `directmemory.cpp:604-640`.
7. **`TraceBackExpression(vm, pc)`** — back-walks instructions of the calling function, decrementing/incrementing a parameter counter to find the start of the `while(...)` expression. Direct port of `directmemory.cpp:642-720`.

This unblocks LeGo's `For/Foreach` and any while-loop in mod scripts.

### Phase 2 — Variable address operators (`_@`, `_@s`, `_@f`, `MEM_Get*Address`)
OpenGothic: `directmemory.cpp:1040-1103` (`_takeref`)

Single shared implementation:
- If `vm.IsTopOfStackReference`: `(refSym, idx, ctx) = vm.PopReference()`. If `refSym.IsMember` and ctx is our `MemoryInstance` → push `instance.Address + refSym.OffsetAsMember`. Otherwise compute `scriptVariables + refSym.Index * sizeof(ScriptVar)` and push.
- Else (top is symbol-id int): pop int → `GetSymbolByIndex(symId)`. If type is `Instance` and the bound instance is `MemoryInstance` → push its address; else fall back to `scriptVariables + sym.Index * sizeof(ScriptVar)`.
- Push `0xBAD10000` / `0xBAD20000` on error and log.

Once `_@` works, **`SetupEngineMemory()` becomes meaningful** because scripts that read engine state via `_@(EngineSymbol)` start hitting our pinned regions.

### Phase 3 — Real `MemoryInstance` (byte-level)
Replace the dictionary-backed `MemoryInstance` with one that uses the heap directly:

```
GetInt(sym, idx)  → mem.ReadInt(_baseAddress + sym.OffsetAsMember + idx * sym.ClassSize)
SetInt(sym, idx)  → mem.WriteInt(...)
GetFloat / SetFloat → BitConverter on a 4-byte slot
GetString / SetString → store via a fake `zString { ptr, len, dontDelete }` struct in 8 bytes,
                        with the actual chars allocated separately and rewritten on Set
```

Mirrors `directmemory.cpp:38-84`. Without this, any script that does pointer arithmetic between two struct fields breaks.

Also fix:
- **`MEM_AssignInst`** — actually create a `MemoryInstance(addr)` and call `sym.SetInstance(...)`. Mirrors `directmemory.cpp:1231-1238`.
- **`MEM_PtrToInst`** — if the address is inside `[scriptVariables, scriptVariables + count * sizeof(ScriptVar))`, derive the symbol index and return its real instance (so writing through the ptr touches the real Daedalus var). Otherwise return a fresh `MemoryInstance(addr)`. Mirrors `directmemory.cpp:1105-1116`.
- **`MEM_InstToPtr`** — if the symbol's instance is a `MemoryInstance` return its address; else compute `scriptVariables + inst.Index * sizeof(ScriptVar)`. Mirrors `directmemory.cpp:1118-1134`. (Today's `_instancePtrMap` allocator is wrong; it doesn't reproduce OG semantics.)

### Phase 4 — `LOCALS` (LeGo PermMem)
OpenGothic: `directmemory.cpp:1575-1582`

```
OverrideFunctionRaw("LOCALS", vm => {
    var sym = FindSymbolByAddress((int)vm.ProgramCounter);
    if (sym != null) sym.UseLocalVariables = true;
});
```

This enables ZenKit's per-call local variable allocation for the calling function — required for mods that use recursive Daedalus functions.

### Phase 5 — Engine memory mirror (`SetupEngineMemory` / `SetupEngineText`)
OpenGothic: `directmemory.cpp:399-538`

Decide first **which mods we actually want to support**. Most casual mods only need Phases 1–4. Engine memory is required by:
- Anything reading `oGame.WLDTIMER`, day/night state, etc.
- LeGo `View.d` (uses `zCView` structs at fixed addresses)
- LeGo `Trialoge.d` (reads `SPAWN_INSERTRANGE`)
- The Phoenix-era CRC32 mod check (`GETBUFFERCRC32_G2`)

Implementation:
1. Port the constants block in `directmemory.cpp:416-447` (G2 addresses — keep G1 values out for now or guard behind a config flag).
2. Define managed mirror structs (`oGame`, `oWorld`, `zCView`, `oCViewStatusBar`, `zCFontMan`, `zCList`, `zCViewText`, `zCPar_Symbol`, `zString`, `zError`, …) — direct ports of `mem32instances.h:1-429`. Use `[StructLayout(LayoutKind.Sequential, Pack = 4)]` (or Explicit with `[FieldOffset]`) so byte sizes match Gothic.
3. Extend `VirtualMemory`:
   - `Pin(byte[] backingStore, int address, int size)` — register an externally-allocated region at a specific address.
   - `Alloc(int address, int size)` — alloc at a fixed address (for `.text` slots).
4. Set `CurrSymbolTableLength` via `GetSymbolByName("CurrSymbolTableLength").SetInt(SymbolCount, 0)`.
5. Set the `INGAME_MENU_INSTANCE` to "MENU_MAIN" so LeGo can detect the main menu state.
6. Allocate the `.text` regions (`OCNPC__ENABLE_EQUIPBESTWEAPONS` etc., `directmemory.cpp:495-538`). These are zero-filled — they exist only so that mods overwriting bytes there don't crash on `MEM_WriteInt`.

### Phase 6 — Symbol table mirror (zCParser / zCPar_Symbol / ScriptVar callbacks)
OpenGothic: `directmemory.cpp:399-414, 754-941`

This is needed for mods that walk Gothic's symbol table directly (`MEM_GetSymbolIndex`, dynamic lookups by name, hooking by symbol address). Less common; can be deferred until a target mod requires it.

When implementing:
- Add `VirtualMemory` callback hooks per region type (`Type.zCParser_variables`, `Type.zCPar_Symbol`, `Type.zCParser`).
- On read of a `zCPar_Symbol` slot, lazily encode the Daedalus symbol's metadata (name pointer, content, offset, bitfield, file/line) per `directmemory.cpp:851-941`.
- On read of `ScriptVar` slot, mirror the live symbol's int/float/string into the heap region. On write, push back to the symbol.

This also enables `_takeref` for non-instance global variables.

### Phase 7 — Direct call helpers (no-ops)
OpenGothic: `directmemory.cpp:1279-1315`

`memint_stackpushint / memint_stackpushinst / memint_stackpushvar / memint_popstring / mem_popintresult / mem_popstringresult / mem_popinstresult` — leave as no-ops as OG does. They only exist as placeholders for inline-ASM that we don't run. Today's implementation already warns; switch to silent NOP per OG.

`memint_stackpushinst` is the one exception — OG actually pushes the instance to keep `_AI_FUNCTION_EVENT` working (`directmemory.cpp:1283-1292`). Implement that.

### Phase 8 — `_AI_FUNCTION_EVENT` (animation-driven script calls)
OpenGothic: `directmemory.cpp:209-309`

When an animation event fires `eventPlayAni("CALL …")`:
- Parse the format `CALL <type> <args…>` where `type` is `I / S / SS / NSII / <numeric>`.
- Resolve the trailing arg as a Daedalus symbol index, push the prefix args, call.

Hook this from our existing animation event dispatcher (search `Assets/UnZENity-Core/Scripts/.../Animation` for `EventPlayAni`/`EventTag` handlers) — invoke `VmIkarusLeGoDomain.OnPlayAniEvent(string)`. Required by mods using LeGo Anim8 / dialog hooks.

### Phase 9 — Hook engine + per-frame tick
OpenGothic: `directmemory.cpp:200-207, 1557-1587`

Minimum viable:
- `HookEngineI(addr, oldInstr, fn)` — keep as warning (we have no engine to hook).
- `_FF_Hook` — call once per frame from a `MonoBehaviour.Update` adapter, matching `DirectMemory::tick`. Wire it into the existing frame-tick service.
- `FINAL()` — return 0 (today already does this).

### Phase 10 — WinAPI / utility shims
OpenGothic: `directmemory.cpp:1433-1555`

Shimming these without Cpu32 means we install **fake function-pointer addresses** that no script will actually call as code. They're only read as data (e.g., `MEM_ReadInt` of `WINAPI__LOADLIBRARY_ptr`). Implement:
- `WINAPI__LOADLIBRARY_ptr / WINAPI__GETPROCADDRESS_ptr` — alloc 4 bytes each, write a non-zero handle so scripts checking `if (handle != 0)` proceed.
- `GETBUFFERCRC32_G2` — install a Daedalus-callable wrapper if any mod calls it through `MEM_CallByPtr`. Otherwise skip. Same for `GETUSERNAMEA / GETLOCALTIME / SYSGETTIMEPTR_G2`.

Defer the full Cpu32 port (1100+ lines of x86 decoding) — log calls into engine code as warnings instead.

### Phase 11 — LeGo Trialoge / View / Font / Console (real impl)
- `TRIA_Invite / TRIA_Start / TRIA_Next` — needs integration with the dialog system. Spec out separately.
- `LOG_MOVETOTOP` — needs the LeGo log topic manager. Defer.
- `ZCCONSOLE__REGISTER` — register the console command in our debug console. Optional.
- `TELEPORTNPCTOWP(npcId, wpName)` — port from `directmemory.cpp:159-171`. Look up the NPC by symbol index, get its `UserData` (NpcContainer), call our existing teleport logic.

### Phase 12 — `Cpu32` x86 emulator (LAST)
Only required when a mod overrides engine code via `MEM_WriteInt`/`HookEngineI` and the patched bytes get executed via `ASMINT_CallMyExternal`. Out of scope for the initial pass — log loudly and document the limitation.

---

## Recommended order of work

1. **Phase 0** (ZenKitCS expose `OverrideFunctionRaw` + helpers).
2. **Phase 2** (`_@` family) — small, immediately useful, and the cleanest validation that the new raw-override path works.
3. **Phase 3** (real `MemoryInstance` + correct `MEM_AssignInst / PtrToInst / InstToPtr`) — fixes silent data loss in current code.
4. **Phase 1** (loops) — biggest correctness win.
5. **Phase 4** (`LOCALS`) — trivial after Phase 1's `FindSymbolByAddress` exists.
6. **Phase 7 + 8** (memint NOPs + AI event parser).
7. **Phase 5 + 6** (engine memory + symbol mirror) — driven by real mod needs; pick one target mod and add only what it touches.
8. **Phases 9 / 10 / 11** as scope expands.
9. **Phase 12** only if a target mod absolutely demands it.

## Validation strategy

- Add a PlayMode test under `Assets/UnZENity-Tests/PlayMode/Vm/IkarusLeGoTests.cs` per phase. Use the `Lab` mocks for VMs without loading the full Gothic content.
- For each phase write a tiny Daedalus snippet (loaded via a test-fixture .DAT) that exercises the just-added function — e.g. Phase 2 test:
  ```daedalus
  var int x; x = 42;
  func void TEST() { var int p; p = _@(x); MEM_WriteInt(p, 99); }
  ```
  then assert `vm.GetSymbolByName("x").GetInt(0) == 99`.
- Smoke-test against an actual Ikarus-using mod (e.g. *Returning 2.0* or any LeGo-based small mod) by enabling it in `GameSettings.dev.json` and watching the Uber Console for unmatched warnings.

## Out of scope (document as "won't fix" until requested)
- Full Cpu32 x86 emulation
- Mods that overwrite Gothic.exe code segments at runtime
- HookEngineI redirecting to native engine functions (we have no engine functions to redirect to)
- `MEM_ReplaceFunc` (current warn-only is correct)
