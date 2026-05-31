using System;
using System.Collections.Generic;
using System.Text;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Vm
{
    /// <summary>
    /// Handles Ikarus and LeGo modding framework compatibility.
    ///
    /// Ikarus/LeGo are Daedalus scripting libraries that treat Gothic's memory as directly
    /// addressable. Since UnZENity has no real Gothic executable memory, we maintain a
    /// virtual heap and map Daedalus symbols to fake addresses so that the most
    /// common Ikarus patterns (alloc → ptr-to-inst → field access, math, string ops,
    /// config reads, dynamic calls) work without modification.
    ///
    /// Known limitations (require ZenKit C# API extensions not yet available):
    ///   - Loop control: repeat/while/mem_goto need unsafe_jump + PC access → stubbed with LogWarning
    ///   - _@ / MEM_GetIntAddress: needs pop_reference from the VM stack → stubbed with LogWarning
    ///   - LOCALS: needs current PC to find the calling function symbol → NOP
    ///   - Byte-level field addressing in MemoryInstance: per-field Dictionary instead of byte array
    /// </summary>
    public class VmIkarusLeGoDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;

        private VirtualMemory _memory;

        // Maps symbol index → fake address; used for MEM_InstToPtr round-trips.
        private readonly Dictionary<int, int> _instancePtrMap = new();


        public VmIkarusLeGoDomain()
        {
            this.Inject();
        }

        public void Init()
        {
            _memory = new VirtualMemory();

            SetupCoreFunctions();
            SetupMemoryFunctions();
            SetupDirectFunctions();
            SetupMathFunctions();
            SetupStringFunctions();
            SetupConfigFunctions();
            SetupDiagnosticFunctions();
            SetupHookEngineFunctions();
            SetupTrialogeFunctions();
        }


        // ─────────────────────────────────────────────────────────────
        // Core / exception handling / init stubs
        // ─────────────────────────────────────────────────────────────

        private void SetupCoreFunctions()
        {
            var vm = _gameStateService.GothicVm;

            vm.OverrideFunction("MEMINT_SetupExceptionHandler", () => { });
            vm.OverrideFunction("MEMINT_ReplaceSlowFunctions", () => { });

            vm.OverrideFunction("MEM_InitStatArrs", () => { });
            vm.OverrideFunction("MEM_InitRepeat", () => { });
            vm.OverrideFunction("MEM_GetAddress_Init", () => { });
            vm.OverrideFunction<string>("MEM_GetCommandLine", () => string.Empty);

            // LeGo PermMem — enables recursive local variables on the calling function.
            // Full implementation needs current-PC access to find the calling symbol.
            vm.OverrideFunction("LOCALS", () =>
                Logger.LogWarning("[LeGo] LOCALS() — recursive local variables not yet supported (needs PC access).", LogCat.ZenKit));

            SafeOverride<int>("FINAL", () =>
            {
                Logger.LogWarning("[LeGo] FINAL() is not implemented.", LogCat.ZenKit);
                return 0;
            });

            // Loop control — requires unsafe_jump + PC access, not yet in the ZenKit C# API.
            SafeOverride("repeat", () =>
                Logger.LogWarning("[Ikarus] repeat() loop control not implemented (needs unsafe_jump).", LogCat.ZenKit));
            SafeOverride("while", () =>
                Logger.LogWarning("[Ikarus] while() loop control not implemented (needs unsafe_jump).", LogCat.ZenKit));
            SafeOverride("mem_goto", () =>
                Logger.LogWarning("[Ikarus] mem_goto() not implemented (needs unsafe_jump).", LogCat.ZenKit));

            // Functions that patch assembly or need sorted NPC lists — safe to NOP
            SafeOverride<int, int>("WRITENOP", (_, _) => { });
            SafeOverride<string, int, int>("MEM_SETKEYS", (_, _, _) => { });
            SafeOverride("INIT_QUIVERS_ALWAYS", () => { });

            SafeOverride("_RENDER_INIT", () =>
                Logger.LogWarning("[Ikarus] _RENDER_INIT — not implemented.", LogCat.ZenKit));

            // Patches asm code of zCView::PrintTimed* for text coloring — safe to ignore
            SafeOverride("PRINT_FIXPS", () => { });

            // Called by Ikarus startup; real init is handled through our overrides
            SafeOverride("MEM_InitAll", () => { });

            // ASM emulation: allocate a fake internal stack and wire up the symbol address
            SafeOverride("ASMINT_Init", AsmInt_Init);
            SafeOverride("ASMINT_MyExternal", () => { });
            SafeOverride("ASMINT_CallMyExternal", () =>
                Logger.LogWarning("[Ikarus] ASMINT_CallMyExternal — CPU emulation not supported.", LogCat.ZenKit));
        }

        private void AsmInt_Init()
        {
            const int stackSize = 1024 * 4;
            var stackAddr = _memory.Alloc(stackSize);

            var stackSym = _gameStateService.GothicVm.GetSymbolByName("ASMINT_InternalStack");
            if (stackSym?.Type == DaedalusDataType.Int)
                stackSym.SetInt(stackAddr, 0);

            var callTargetAddr = _memory.Alloc(4);
            var callTargetSym = _gameStateService.GothicVm.GetSymbolByName("ASMINT_CallTarget");
            if (callTargetSym?.Type == DaedalusDataType.Int)
                callTargetSym.SetInt(callTargetAddr, 0);
        }


        // ─────────────────────────────────────────────────────────────
        // Virtual memory: Alloc / Free / Realloc / Read / Write
        // ─────────────────────────────────────────────────────────────

        private void SetupMemoryFunctions()
        {
            var vm = _gameStateService.GothicVm;

            vm.OverrideFunction<int, int>("MEM_Alloc", amount =>
            {
                if (amount <= 0)
                {
                    Logger.LogWarning($"[Ikarus] MEM_Alloc: non-positive size {amount}.", LogCat.ZenKit);
                    return 0;
                }
                return _memory.Alloc(amount);
            });

            vm.OverrideFunction<int>("MEM_Free", address => _memory.Free(address));

            // oldSize is part of the Ikarus signature but unused in our allocator
            vm.OverrideFunction<int, int, int, int>("MEM_Realloc", (address, _, newSize) =>
                _memory.Realloc(address, newSize));

            vm.OverrideFunction<int, int>("MEM_ReadInt", address => _memory.ReadInt(address));

            vm.OverrideFunction<int, int>("MEM_WriteInt", (address, value) =>
                _memory.WriteInt(address, value));

            vm.OverrideFunction<int, int, int>("MEM_CopyBytes", (src, dst, size) =>
                _memory.CopyBytes(src, dst, size));

            vm.OverrideFunction<string, int>("MEM_ReadString", address =>
                _memory.ReadString(address));

            // Static array: read / write a symbol's array element by index offset
            vm.OverrideFunction<int, int, int>("MEM_ReadStatArr", (symId, index) =>
            {
                var sym = _gameStateService.GothicVm.GetSymbolByIndex(symId);
                if (sym == null)
                {
                    Logger.LogWarning($"[Ikarus] MEM_ReadStatArr: symbol {symId} not found.", LogCat.ZenKit);
                    return 0;
                }
                return sym.GetInt((ushort)index);
            });

            vm.OverrideFunction<int, int, int>("MEM_WriteStatArr", (symId, index, value) =>
            {
                var sym = _gameStateService.GothicVm.GetSymbolByIndex(symId);
                if (sym == null)
                {
                    Logger.LogWarning($"[Ikarus] MEM_WriteStatArr: symbol {symId} not found.", LogCat.ZenKit);
                    return;
                }
                sym.SetInt(value, (ushort)index);
            });

            // _@ / MEM_GetIntAddress — get address of a Daedalus variable.
            // Requires pop_reference() from the native VM stack (not in C# API).
            // Consume the pushed parameter to keep the VM stack balanced.
            vm.OverrideFunction<int, int>("_@", _ =>
            {
                Logger.LogWarning("[Ikarus] _@() / MEM_GetIntAddress() — variable address not supported (needs pop_reference).", LogCat.ZenKit);
                return 0;
            });
            SafeOverride<int, int>("_@s", _ =>
            {
                Logger.LogWarning("[Ikarus] _@s() / MEM_GetStringAddress() — not supported.", LogCat.ZenKit);
                return 0;
            });
            SafeOverride<int, int>("_@f", _ =>
            {
                Logger.LogWarning("[Ikarus] _@f() / MEM_GetFloatAddress() — not supported.", LogCat.ZenKit);
                return 0;
            });
            SafeOverride<int, int>("MEM_GetIntAddress", _ =>
            {
                Logger.LogWarning("[Ikarus] MEM_GetIntAddress() — not supported.", LogCat.ZenKit);
                return 0;
            });
            SafeOverride<int, int>("MEM_GetStringAddress", _ =>
            {
                Logger.LogWarning("[Ikarus] MEM_GetStringAddress() — not supported.", LogCat.ZenKit);
                return 0;
            });
            SafeOverride<int, int>("MEM_GetFloatAddress", _ =>
            {
                Logger.LogWarning("[Ikarus] MEM_GetFloatAddress() — not supported.", LogCat.ZenKit);
                return 0;
            });
        }


        // ─────────────────────────────────────────────────────────────
        // Pointer ↔ instance conversion and dynamic calls
        // ─────────────────────────────────────────────────────────────

        private void SetupDirectFunctions()
        {
            var vm = _gameStateService.GothicVm;

            // MEM_PtrToInst / _^ — convert a fake address to a transient Daedalus instance
            vm.OverrideFunction<DaedalusInstance, int>("MEM_PtrToInst", address =>
                address == 0 ? null : DaedalusInstance.CreateTransient(new MemoryInstance(address, _memory)));
            vm.OverrideFunction<DaedalusInstance, int>("_^", address =>
                address == 0 ? null : DaedalusInstance.CreateTransient(new MemoryInstance(address, _memory)));

            // MEM_InstToPtr — return the fake address associated with an instance symbol
            vm.OverrideFunction<int, int>("MEM_InstToPtr", symId =>
            {
                if (_instancePtrMap.TryGetValue(symId, out var existing))
                    return existing;
                // Allocate a stable address so round-trips remain consistent
                var addr = _memory.Alloc(4);
                _instancePtrMap[symId] = addr;
                return addr;
            });

            // MEM_AssignInst — record the address mapping for a symbol; full write-back into
            // the symbol's native instance slot requires unsafe ZenKit API access.
            vm.OverrideFunction<int, int>("MEM_AssignInst", (symId, address) =>
            {
                var sym = _gameStateService.GothicVm.GetSymbolByIndex(symId);
                if (sym == null || sym.Type != DaedalusDataType.Instance)
                {
                    Logger.LogWarning($"[Ikarus] MEM_AssignInst: symbol {symId} is not an instance.", LogCat.ZenKit);
                    return;
                }
                _instancePtrMap[symId] = address;
            });

            // MEM_GetFuncIdByOffset — only needed for hook / self-modifying patterns
            vm.OverrideFunction<int, int>("MEM_GetFuncIdByOffset", offset =>
            {
                if (offset != 0)
                    Logger.LogWarning($"[Ikarus] MEM_GetFuncIdByOffset({offset}) — not supported.", LogCat.ZenKit);
                return 0;
            });

            // MEM_CallByID — call a Daedalus function by symbol index
            vm.OverrideFunction<int>("MEM_CallByID", symId =>
            {
                var sym = _gameStateService.GothicVm.GetSymbolByIndex(symId);
                if (sym is not { Type: DaedalusDataType.Function })
                {
                    Logger.LogWarning($"[Ikarus] MEM_CallByID({symId}): symbol is not callable.", LogCat.ZenKit);
                    return;
                }
                _gameStateService.GothicVm.Call(symId);
            });

            // MEM_CallByPtr — call a Daedalus function by its bytecode address
            vm.OverrideFunction<int>("MEM_CallByPtr", address =>
            {
                var sym = _gameStateService.GothicVm.GetSymbolByAddress(address);
                if (sym is not { Type: DaedalusDataType.Function })
                {
                    Logger.LogWarning($"[Ikarus] MEM_CallByPtr(0x{address:X}): no callable symbol at that address.", LogCat.ZenKit);
                    return;
                }
                _gameStateService.GothicVm.Call(sym.Index);
            });

            // Raw stack push/pop — not accessible through the typed C# API
            SafeOverride("memint_stackpushint", () =>
                Logger.LogWarning("[Ikarus] memint_stackpushint — raw stack push not supported.", LogCat.ZenKit));
            SafeOverride("memint_stackpushinst", () =>
                Logger.LogWarning("[Ikarus] memint_stackpushinst — raw stack push not supported.", LogCat.ZenKit));
            SafeOverride("memint_stackpushvar", () =>
                Logger.LogWarning("[Ikarus] memint_stackpushvar — raw stack push not supported.", LogCat.ZenKit));
            SafeOverride("memint_popstring", () =>
                Logger.LogWarning("[Ikarus] memint_popstring — raw stack pop not supported.", LogCat.ZenKit));
            SafeOverride("mem_popintresult", () =>
                Logger.LogWarning("[Ikarus] mem_popintresult — raw stack pop not supported.", LogCat.ZenKit));
            SafeOverride("mem_popstringresult", () =>
                Logger.LogWarning("[Ikarus] mem_popstringresult — raw stack pop not supported.", LogCat.ZenKit));
            SafeOverride("mem_popinstresult", () =>
                Logger.LogWarning("[Ikarus] mem_popinstresult — raw stack pop not supported.", LogCat.ZenKit));
        }


        // ─────────────────────────────────────────────────────────────
        // Math: float-as-int (IEEE 754 bit reinterpretation)
        // ─────────────────────────────────────────────────────────────

        private void SetupMathFunctions()
        {
            var vm = _gameStateService.GothicVm;

            // MKF: int → float bits (treat the int value as a float and return its bit pattern)
            vm.OverrideFunction<int, int>("MKF", v => BitConverter.SingleToInt32Bits((float)v));

            // TRUNCF/ROUNDF: float bits → truncate/round → return as plain int (not bits)
            vm.OverrideFunction<int, int>("TRUNCF", v =>
                (int)MathF.Truncate(BitConverter.Int32BitsToSingle(v)));

            vm.OverrideFunction<int, int>("ROUNDF", v =>
                (int)MathF.Round(BitConverter.Int32BitsToSingle(v)));

            vm.OverrideFunction<int, int, int>("ADDF", (a, b) =>
                BitConverter.SingleToInt32Bits(
                    BitConverter.Int32BitsToSingle(a) + BitConverter.Int32BitsToSingle(b)));

            vm.OverrideFunction<int, int, int>("SUBF", (a, b) =>
                BitConverter.SingleToInt32Bits(
                    BitConverter.Int32BitsToSingle(a) - BitConverter.Int32BitsToSingle(b)));

            vm.OverrideFunction<int, int, int>("MULF", (a, b) =>
                BitConverter.SingleToInt32Bits(
                    BitConverter.Int32BitsToSingle(a) * BitConverter.Int32BitsToSingle(b)));

            vm.OverrideFunction<int, int, int>("DIVF", (a, b) =>
            {
                var fb = BitConverter.Int32BitsToSingle(b);
                if (fb == 0f)
                {
                    Logger.LogError("[Ikarus] DIVF: division by zero.", LogCat.ZenKit);
                    return 0;
                }
                return BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(a) / fb);
            });
        }


        // ─────────────────────────────────────────────────────────────
        // Strings
        // ─────────────────────────────────────────────────────────────

        private void SetupStringFunctions()
        {
            var vm = _gameStateService.GothicVm;

            vm.OverrideFunction<string, string, int, int>("STR_SubStr", (str, start, count) =>
            {
                if (start < 0 || count < 0)
                {
                    Logger.LogError("[Ikarus] STR_SubStr: start and count must not be negative.", LogCat.ZenKit);
                    return string.Empty;
                }
                if (start >= str.Length)
                {
                    Logger.LogError("[Ikarus] STR_SubStr: start lies beyond end of string.", LogCat.ZenKit);
                    return string.Empty;
                }
                return str.Substring(start, Math.Min(count, str.Length - start));
            });

            vm.OverrideFunction<string, string>("STR_Upper", str => str.ToUpperInvariant());

            // SB_TOSTRING — LeGo StringBuilder: _SB_CURRENT holds a pointer to a {ptr, len, cap} struct
            SafeOverride<string>("SB_TOSTRING", () =>
            {
                var sbSym = _gameStateService.GothicVm.GetSymbolByName("_SB_CURRENT");
                if (sbSym?.Type != DaedalusDataType.Int)
                    return string.Empty;

                var ptr = sbSym.GetInt(0);
                if (ptr == 0)
                    return string.Empty;

                var dataPtr = _memory.ReadInt(ptr);
                var len = _memory.ReadInt(ptr + 4);
                if (dataPtr == 0 || len <= 0)
                    return string.Empty;

                return _memory.ReadBytes(dataPtr, len);
            });
        }


        // ─────────────────────────────────────────────────────────────
        // Gothic.ini / mod config bridge
        // ─────────────────────────────────────────────────────────────

        private void SetupConfigFunctions()
        {
            var vm = _gameStateService.GothicVm;

            // Section is ignored since our config is a flat key→value dictionary
            vm.OverrideFunction<string, string, string>("MEM_GetGothOpt", (_, key) =>
                _configService.Gothic.GetString(key, string.Empty));

            vm.OverrideFunction<string, string, string>("MEM_SetGothOpt", (section, key, value) =>
            {
                Logger.LogWarning($"[Ikarus] MEM_SetGothOpt({section}, {key}, {value}) — in-memory only, not persisted.", LogCat.ZenKit);
                _configService.Gothic.SetInt(section, key, 0);
            });

            vm.OverrideFunction<int, string>("MEM_GothOptSectionExists", section =>
            {
                // We have no section-level metadata; return true to prevent mod fallback paths
                Logger.LogWarning($"[Ikarus] MEM_GothOptSectionExists({section}) — section granularity not supported, returning true.", LogCat.ZenKit);
                return 1;
            });

            vm.OverrideFunction<int, string, string>("MEM_GothOptExists", (section, key) =>
            {
                // Fake Union activation so Ikarus skips unpatched-G2 workarounds
                if (section == "INTERNAL" && key == "UnionActivated")
                    return 1;

                return string.IsNullOrEmpty(_configService.Gothic.GetString(key, null)) ? 0 : 1;
            });

            vm.OverrideFunction<string, string, string>("MEM_GetModOpt", (section, key) =>
            {
                Logger.LogWarning($"[Ikarus] MEM_GetModOpt({section}, {key}) — not supported.", LogCat.ZenKit);
                return string.Empty;
            });

            vm.OverrideFunction<int, string>("MEM_ModOptSectionExists", section =>
            {
                Logger.LogWarning($"[Ikarus] MEM_ModOptSectionExists({section}) — not supported.", LogCat.ZenKit);
                return 0;
            });

            vm.OverrideFunction<int, string, string>("MEM_ModOptExists", (section, key) =>
            {
                Logger.LogWarning($"[Ikarus] MEM_ModOptExists({section}, {key}) — not supported.", LogCat.ZenKit);
                return 0;
            });
        }


        // ─────────────────────────────────────────────────────────────
        // Diagnostics: stack trace, ZSpy output, function replacement
        // ─────────────────────────────────────────────────────────────

        private void SetupDiagnosticFunctions()
        {
            var vm = _gameStateService.GothicVm;

            vm.OverrideFunction("MEM_PrintStackTrace", () => _gameStateService.GothicVm.PrintStackTrace());

            vm.OverrideFunction<int, string>("MEM_SendToSpy", (channel, message) =>
                Logger.Log($"[zSpy ch{channel}] {message}", LogCat.ZenKit));

            // MEM_ReplaceFunc — runtime function redirection; not supported, log the attempt
            vm.OverrideFunction<int, int>("MEM_ReplaceFunc", (destSymId, srcSymId) =>
            {
                var dest = _gameStateService.GothicVm.GetSymbolByIndex(destSymId);
                var src = _gameStateService.GothicVm.GetSymbolByIndex(srcSymId);
                Logger.LogWarning(
                    $"[Ikarus] MEM_ReplaceFunc({dest?.Name ?? $"#{destSymId}"} → {src?.Name ?? $"#{srcSymId}"}) — runtime function replacement not supported.",
                    LogCat.ZenKit);
            });
        }


        // ─────────────────────────────────────────────────────────────
        // Hook engine, console registration, WP teleport
        // ─────────────────────────────────────────────────────────────

        private void SetupHookEngineFunctions()
        {
            // HookEngineI — patches a native code address; not applicable in UnZENity
            SafeOverride<int, int, int>("HookEngineI", (address, _, funcSymId) =>
            {
                var sym = _gameStateService.GothicVm.GetSymbolByIndex(funcSymId);
                Logger.LogWarning(
                    $"[LeGo] HookEngineI(0x{address:X}) → {sym?.Name ?? $"#{funcSymId}"} — hook engine not supported.",
                    LogCat.ZenKit);
            });

            SafeOverride<string>("LOG_MOVETOTOP", topic =>
                Logger.LogWarning($"[LeGo] LOG_MOVETOTOP({topic}) — not implemented.", LogCat.ZenKit));

            // Teleports an NPC to a waypoint via pointer arithmetic; stubbed for now
            SafeOverride<int, string>("TELEPORTNPCTOWP", (npcSymId, wpName) =>
                Logger.LogWarning($"[Ikarus] TELEPORTNPCTOWP(#{npcSymId}, {wpName}) — not yet implemented.", LogCat.ZenKit));
        }


        // ─────────────────────────────────────────────────────────────
        // Trialoge (LeGo multi-NPC dialogue system)
        // ─────────────────────────────────────────────────────────────

        private void SetupTrialogeFunctions()
        {
            var vm = _gameStateService.GothicVm;

            vm.OverrideFunction<NpcInstance>("TRIA_Invite", npc =>
                Logger.LogWarning($"[LeGo] TRIA_Invite({npc?.GetName(NpcNameSlot.Slot0) ?? "null"}) — not yet implemented.", LogCat.ZenKit));

            vm.OverrideFunction("TRIA_Start", () =>
                Logger.LogWarning("[LeGo] TRIA_Start() — not yet implemented.", LogCat.ZenKit));

            vm.OverrideFunction<NpcInstance>("TRIA_Next", npc =>
                Logger.LogWarning($"[LeGo] TRIA_Next({npc?.GetName(NpcNameSlot.Slot0) ?? "null"}) — not yet implemented.", LogCat.ZenKit));
        }


        // ─────────────────────────────────────────────────────────────
        // SafeOverride helpers — skip silently if symbol is not in the scripts
        // ─────────────────────────────────────────────────────────────

        private void SafeOverride(string name, Action handler)
        {
            if (_gameStateService.GothicVm.GetSymbolByName(name) == null)
                return;
            _gameStateService.GothicVm.OverrideFunction(name, () => handler());
        }

        private void SafeOverride<TP0>(string name, Action<TP0> handler)
        {
            if (_gameStateService.GothicVm.GetSymbolByName(name) == null)
                return;
            _gameStateService.GothicVm.OverrideFunction<TP0>(name, p0 => handler(p0));
        }

        private void SafeOverride<TP0, TP1>(string name, Action<TP0, TP1> handler)
        {
            if (_gameStateService.GothicVm.GetSymbolByName(name) == null)
                return;
            _gameStateService.GothicVm.OverrideFunction<TP0, TP1>(name, (p0, p1) => handler(p0, p1));
        }

        private void SafeOverride<TP0, TP1, TP2>(string name, Action<TP0, TP1, TP2> handler)
        {
            if (_gameStateService.GothicVm.GetSymbolByName(name) == null)
                return;
            _gameStateService.GothicVm.OverrideFunction<TP0, TP1, TP2>(name, (p0, p1, p2) => handler(p0, p1, p2));
        }

        private void SafeOverride<TR>(string name, Func<TR> handler)
        {
            if (_gameStateService.GothicVm.GetSymbolByName(name) == null)
                return;
            _gameStateService.GothicVm.OverrideFunction(name, () => handler());
        }

        private void SafeOverride<TR, TP0>(string name, Func<TP0, TR> handler)
        {
            if (_gameStateService.GothicVm.GetSymbolByName(name) == null)
                return;
            _gameStateService.GothicVm.OverrideFunction(name, (TP0 p0) => handler(p0));
        }


        // =====================================================================
        // VirtualMemory — fake 32-bit heap for Ikarus alloc/read/write patterns
        // Addresses start at 0x1000_0000 to stay clear of symbol indices.
        // =====================================================================

        private sealed class VirtualMemory
        {
            private const int _baseAddress = 0x1000_0000;

            private int _nextAddress = _baseAddress;
            private readonly Dictionary<int, byte[]> _heap = new();

            /// <summary>Allocates <paramref name="size"/> bytes and returns the fake address.</summary>
            public int Alloc(int size)
            {
                if (size <= 0)
                    return 0;
                var aligned = (size + 3) & ~3;
                var address = _nextAddress;
                _heap[address] = new byte[aligned];
                _nextAddress += aligned;
                return address;
            }

            public void Free(int address)
            {
                if (!_heap.Remove(address))
                    Logger.LogWarning($"[Ikarus] MEM_Free(0x{address:X}): address not found in virtual heap.", LogCat.ZenKit);
            }

            public int Realloc(int address, int newSize)
            {
                if (newSize <= 0)
                {
                    Free(address);
                    return 0;
                }
                var newAddr = Alloc(newSize);
                if (_heap.TryGetValue(address, out var old))
                {
                    Buffer.BlockCopy(old, 0, _heap[newAddr], 0, Math.Min(old.Length, newSize));
                    _heap.Remove(address);
                }
                return newAddr;
            }

            public int ReadInt(int address)
            {
                var (block, offset) = Resolve(address, 4);
                return block == null ? 0 : BitConverter.ToInt32(block, offset);
            }

            public void WriteInt(int address, int value)
            {
                var (block, offset) = Resolve(address, 4);
                if (block == null)
                    return;
                var bytes = BitConverter.GetBytes(value);
                Buffer.BlockCopy(bytes, 0, block, offset, 4);
            }

            public void CopyBytes(int src, int dst, int size)
            {
                for (var i = 0; i < size; i++)
                {
                    var (srcBlock, srcOff) = Resolve(src + i, 1);
                    var (dstBlock, dstOff) = Resolve(dst + i, 1);
                    if (srcBlock != null && dstBlock != null)
                        dstBlock[dstOff] = srcBlock[srcOff];
                }
            }

            public string ReadString(int address)
            {
                var (block, offset) = Resolve(address, 1);
                if (block == null)
                    return string.Empty;
                var end = offset;
                while (end < block.Length && block[end] != 0)
                    end++;
                return Encoding.GetEncoding(1252).GetString(block, offset, end - offset);
            }

            /// <summary>Reads <paramref name="length"/> raw bytes as a Windows-1252 string.</summary>
            public string ReadBytes(int address, int length)
            {
                var (block, offset) = Resolve(address, length);
                if (block == null)
                    return string.Empty;
                return Encoding.GetEncoding(1252).GetString(block, offset, Math.Min(length, block.Length - offset));
            }

            private (byte[] block, int offset) Resolve(int address, int requiredBytes)
            {
                foreach (var kv in _heap)
                {
                    if (address >= kv.Key && address + requiredBytes <= kv.Key + kv.Value.Length)
                        return (kv.Value, address - kv.Key);
                }
                Logger.LogWarning($"[Ikarus] Virtual memory access out of range: 0x{address:X} ({requiredBytes} bytes).", LogCat.ZenKit);
                return (null, 0);
            }
        }


        // =====================================================================
        // MemoryInstance — IDaedalusTransientInstance backed by per-field dictionaries
        //
        // When a script calls MEM_PtrToInst(addr) and then accesses fields on the
        // returned instance, our Get/Set callbacks fire with the member DaedalusSymbol
        // and array index. The dictionaries are the authoritative storage.
        //
        // We intentionally do NOT mirror values into the virtual heap via sym.Address,
        // because sym.Address is the symbol's Daedalus bytecode position — a large value
        // completely unrelated to the byte offset of the field within the struct
        // (that would be sym.offset_as_member() in C++, which is not exposed in the C# API).
        // =====================================================================

        private sealed class MemoryInstance : IDaedalusTransientInstance
        {
            private readonly int _baseAddress;

            private readonly Dictionary<(string, ushort), int> _ints = new();
            private readonly Dictionary<(string, ushort), float> _floats = new();
            private readonly Dictionary<(string, ushort), string> _strings = new();

            public MemoryInstance(int baseAddress, VirtualMemory _)
            {
                _baseAddress = baseAddress;
            }

            public int GetInt(DaedalusSymbol sym, ushort idx) =>
                _ints.TryGetValue((sym.Name, idx), out var v) ? v : 0;

            public void SetInt(DaedalusSymbol sym, ushort idx, int val) =>
                _ints[(sym.Name, idx)] = val;

            public float GetFloat(DaedalusSymbol sym, ushort idx) =>
                _floats.TryGetValue((sym.Name, idx), out var v) ? v : 0f;

            public void SetFloat(DaedalusSymbol sym, ushort idx, float val) =>
                _floats[(sym.Name, idx)] = val;

            public string GetString(DaedalusSymbol sym, ushort idx) =>
                _strings.TryGetValue((sym.Name, idx), out var v) ? v : string.Empty;

            public void SetString(DaedalusSymbol sym, ushort idx, string val) =>
                _strings[(sym.Name, idx)] = val;
        }
    }
}
