using System;
using System.Collections.Generic;
using System.Text;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Vm
{
    /// <summary>
    /// Registers externals and function overrides so that no C# exception ever crosses into ZenKit's native VM.
    /// A throw there skips ZenKit's own clean-up: the call frame stays on the VM's call stack, the declared return
    /// value is never pushed, and after enough of them the Daedalus stack overflows and the editor dies natively.
    /// Here a failing callback is logged once per external and returns the declared type's default instead
    /// (0, 0f, "" or a null instance), so the script continues with a balanced stack.
    /// The method names match DaedalusVm, so registration code reads the same. Registering a symbol the scripts
    /// don't declare logs its name and throws (EnsureDeclared).
    /// </summary>
    public class VmExternalGuard
    {
        private readonly DaedalusVm _vm;
        private readonly Func<Encoding> _encoding;
        private readonly HashSet<string> _failedExternals = new();

        /// <param name="encoding">The installation's code page (GameStateService.Encoding). Read on every use: the
        /// language is detected after the externals are registered.</param>
        public VmExternalGuard(DaedalusVm vm, Func<Encoding> encoding)
        {
            _vm = vm;
            _encoding = encoding;
        }

        public DaedalusSymbol GetSymbolByName(string name)
        {
            return _vm.GetSymbolByName(name);
        }

        public void RegisterExternalDefault(DaedalusVm.ExternalDefaultFunction cb)
        {
            _vm.RegisterExternalDefault((vm, sym) =>
            {
                try
                {
                    cb(vm, sym);
                }
                catch (Exception e)
                {
                    Failed(sym.Name, e);
                }
            });
        }


        public void RegisterExternal<TR>(string name, DaedalusVm.ExternalFunc<TR> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0>(string name, DaedalusVm.ExternalFunc<TR, TP0> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1>(string name, DaedalusVm.ExternalFunc<TR, TP0, TP1> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1, TP2>(string name, DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1, TP2, TP3>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1, TP2, TP3, TP4, TP5>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal(string name, DaedalusVm.ExternalFuncV cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        /// <summary>
        /// An external that takes two strings and returns one whose text may hold non-ASCII characters
        /// (ConcatStrings). ZenKit's typed registration would scramble them on IL2CPP, see
        /// ZenKitVmExtension.PushNativeString: here the parameters and the result pass the VM as bytes in the
        /// installation's code page. Guarded like the others: a failing callback returns "".
        /// Not covered yet: the other callbacks that return text (the Ikarus/LeGo overrides STR_SubStr, STR_Upper,
        /// MEM_ReadString, SB_TOSTRING) still use ZenKit's typed registration.
        /// </summary>
        public void RegisterStringExternal(string name, Func<string, string, string> cb)
        {
            EnsureDeclared(name);
            if (!ZenKitVmExtension.HasNativeStrings)
            {
                Logger.LogWarning($"External >{name}<: ZenKit's VM handle isn't reachable - registered the ZenKit " +
                                  "way, its non-ASCII characters are scrambled on Quest and Pico.", LogCat.ZenKit);
                RegisterExternal<string, string, string>(name, (p0, p1) => cb(p0, p1));
                return;
            }

            _vm.RegisterExternal(name, () =>
            {
                var result = string.Empty;
                try
                {
                    // Before the language is detected, Gothic's default code page.
                    var codePage = _encoding() ?? Encoding.GetEncoding((int)StringEncoding.WestEurope);
                    try
                    {
                        // Parameters come off the stack last first.
                        var p1 = _vm.PopNativeString(codePage);
                        var p0 = _vm.PopNativeString(codePage);
                        result = cb(p0, p1);
                    }
                    catch (Exception e)
                    {
                        Failed(name, e);
                    }

                    // Always pushed: the scripts expect a return value on the stack.
                    _vm.PushNativeString(result, codePage);
                }
                catch (Exception e)
                {
                    // The native call itself failed (a ZenKit update renamed it): nothing may cross into the VM.
                    Failed(name, e);
                }
            });
        }

        public void RegisterExternal<TP0>(string name, DaedalusVm.ExternalFuncV<TP0> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1>(string name, DaedalusVm.ExternalFuncV<TP0, TP1> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2>(string name, DaedalusVm.ExternalFuncV<TP0, TP1, TP2> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3>(string name, DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3, TP4, TP5>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3, TP4, TP5, TP6>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7> cb)
        {
            EnsureDeclared(name);
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void OverrideFunction<TR>(string name, DaedalusVm.ExternalFunc<TR> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0>(string name, DaedalusVm.ExternalFunc<TR, TP0> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1>(string name, DaedalusVm.ExternalFunc<TR, TP0, TP1> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1, TP2>(string name, DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1, TP2, TP3>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1, TP2, TP3, TP4, TP5>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction(string name, DaedalusVm.ExternalFuncV cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0>(string name, DaedalusVm.ExternalFuncV<TP0> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1>(string name, DaedalusVm.ExternalFuncV<TP0, TP1> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2>(string name, DaedalusVm.ExternalFuncV<TP0, TP1, TP2> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3>(string name, DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3, TP4, TP5>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3, TP4, TP5, TP6>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7> cb)
        {
            EnsureDeclared(name);
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        private DaedalusVm.ExternalFunc<TR> Guard<TR>(string name, DaedalusVm.ExternalFunc<TR> cb)
        {
            return () =>
            {
                try
                {
                    return cb();
                }
                catch (Exception e)
                {
                    return Failed<TR>(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFunc<TR, TP0> Guard<TR, TP0>(string name, DaedalusVm.ExternalFunc<TR, TP0> cb)
        {
            return p0 =>
            {
                try
                {
                    return cb(p0);
                }
                catch (Exception e)
                {
                    return Failed<TR>(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFunc<TR, TP0, TP1> Guard<TR, TP0, TP1>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1> cb)
        {
            return (p0, p1) =>
            {
                try
                {
                    return cb(p0, p1);
                }
                catch (Exception e)
                {
                    return Failed<TR>(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2> Guard<TR, TP0, TP1, TP2>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2> cb)
        {
            return (p0, p1, p2) =>
            {
                try
                {
                    return cb(p0, p1, p2);
                }
                catch (Exception e)
                {
                    return Failed<TR>(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3> Guard<TR, TP0, TP1, TP2, TP3>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3> cb)
        {
            return (p0, p1, p2, p3) =>
            {
                try
                {
                    return cb(p0, p1, p2, p3);
                }
                catch (Exception e)
                {
                    return Failed<TR>(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4> Guard<TR, TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4> cb)
        {
            return (p0, p1, p2, p3, p4) =>
            {
                try
                {
                    return cb(p0, p1, p2, p3, p4);
                }
                catch (Exception e)
                {
                    return Failed<TR>(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4, TP5>
            Guard<TR, TP0, TP1, TP2, TP3, TP4, TP5>(string name,
                DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            return (p0, p1, p2, p3, p4, p5) =>
            {
                try
                {
                    return cb(p0, p1, p2, p3, p4, p5);
                }
                catch (Exception e)
                {
                    return Failed<TR>(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFuncV Guard(string name, DaedalusVm.ExternalFuncV cb)
        {
            return () =>
            {
                try
                {
                    cb();
                }
                catch (Exception e)
                {
                    Failed(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFuncV<TP0> Guard<TP0>(string name, DaedalusVm.ExternalFuncV<TP0> cb)
        {
            return p0 =>
            {
                try
                {
                    cb(p0);
                }
                catch (Exception e)
                {
                    Failed(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFuncV<TP0, TP1> Guard<TP0, TP1>(string name, DaedalusVm.ExternalFuncV<TP0, TP1> cb)
        {
            return (p0, p1) =>
            {
                try
                {
                    cb(p0, p1);
                }
                catch (Exception e)
                {
                    Failed(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFuncV<TP0, TP1, TP2> Guard<TP0, TP1, TP2>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2> cb)
        {
            return (p0, p1, p2) =>
            {
                try
                {
                    cb(p0, p1, p2);
                }
                catch (Exception e)
                {
                    Failed(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3> Guard<TP0, TP1, TP2, TP3>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3> cb)
        {
            return (p0, p1, p2, p3) =>
            {
                try
                {
                    cb(p0, p1, p2, p3);
                }
                catch (Exception e)
                {
                    Failed(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4> Guard<TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4> cb)
        {
            return (p0, p1, p2, p3, p4) =>
            {
                try
                {
                    cb(p0, p1, p2, p3, p4);
                }
                catch (Exception e)
                {
                    Failed(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5> Guard<TP0, TP1, TP2, TP3, TP4, TP5>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            return (p0, p1, p2, p3, p4, p5) =>
            {
                try
                {
                    cb(p0, p1, p2, p3, p4, p5);
                }
                catch (Exception e)
                {
                    Failed(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6>
            Guard<TP0, TP1, TP2, TP3, TP4, TP5, TP6>(string name,
                DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6> cb)
        {
            return (p0, p1, p2, p3, p4, p5, p6) =>
            {
                try
                {
                    cb(p0, p1, p2, p3, p4, p5, p6);
                }
                catch (Exception e)
                {
                    Failed(name, e);
                }
            };
        }

        private DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7>
            Guard<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7>(string name,
                DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7> cb)
        {
            return (p0, p1, p2, p3, p4, p5, p6, p7) =>
            {
                try
                {
                    cb(p0, p1, p2, p3, p4, p5, p6, p7);
                }
                catch (Exception e)
                {
                    Failed(name, e);
                }
            };
        }

        private TR Failed<TR>(string name, Exception e)
        {
            Failed(name, e);

            // A null string would be pushed as an instance - the script expects a string on the stack.
            if (typeof(TR) == typeof(string))
                return (TR)(object)string.Empty;

            return default;
        }

        /// <summary>
        /// ZenKit throws a bare "Symbol not found" for an external or function the scripts don't declare (a mod
        /// without it). The boot still stops - a mod without it isn't playable - but the log names the culprit.
        /// </summary>
        private void EnsureDeclared(string name)
        {
            if (_vm.GetSymbolByName(name) != null)
                return;

            Logger.LogError($"External >{name}< isn't declared by the scripts (a mod without it?) - VM boot stops.",
                LogCat.ZenKit);
            throw new InvalidOperationException($"Daedalus symbol >{name}< not found.");
        }

        private void Failed(string name, Exception e)
        {
            if (!_failedExternals.Add(name))
                return;

            Logger.LogError($"External >{name}< threw - it returns its default value instead. Later failures of " +
                            $"this external are not logged again. {e}", LogCat.ZenKit);
        }
    }
}
