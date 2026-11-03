using System;
using System.Collections.Generic;
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
    /// The method names match DaedalusVm, so registration code reads the same.
    /// </summary>
    public class VmExternalGuard
    {
        private readonly DaedalusVm _vm;
        private readonly HashSet<string> _failedExternals = new();

        public VmExternalGuard(DaedalusVm vm)
        {
            _vm = vm;
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
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0>(string name, DaedalusVm.ExternalFunc<TR, TP0> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1>(string name, DaedalusVm.ExternalFunc<TR, TP0, TP1> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1, TP2>(string name, DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1, TP2, TP3>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TR, TP0, TP1, TP2, TP3, TP4, TP5>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal(string name, DaedalusVm.ExternalFuncV cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0>(string name, DaedalusVm.ExternalFuncV<TP0> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1>(string name, DaedalusVm.ExternalFuncV<TP0, TP1> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2>(string name, DaedalusVm.ExternalFuncV<TP0, TP1, TP2> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3>(string name, DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3, TP4, TP5>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3, TP4, TP5, TP6>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void RegisterExternal<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7> cb)
        {
            _vm.RegisterExternal(name, Guard(name, cb));
        }

        public void OverrideFunction<TR>(string name, DaedalusVm.ExternalFunc<TR> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0>(string name, DaedalusVm.ExternalFunc<TR, TP0> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1>(string name, DaedalusVm.ExternalFunc<TR, TP0, TP1> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1, TP2>(string name, DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1, TP2, TP3>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TR, TP0, TP1, TP2, TP3, TP4, TP5>(string name,
            DaedalusVm.ExternalFunc<TR, TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction(string name, DaedalusVm.ExternalFuncV cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0>(string name, DaedalusVm.ExternalFuncV<TP0> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1>(string name, DaedalusVm.ExternalFuncV<TP0, TP1> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2>(string name, DaedalusVm.ExternalFuncV<TP0, TP1, TP2> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3>(string name, DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3, TP4>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3, TP4, TP5>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3, TP4, TP5, TP6>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6> cb)
        {
            _vm.OverrideFunction(name, Guard(name, cb));
        }

        public void OverrideFunction<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7>(string name,
            DaedalusVm.ExternalFuncV<TP0, TP1, TP2, TP3, TP4, TP5, TP6, TP7> cb)
        {
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

        private void Failed(string name, Exception e)
        {
            if (!_failedExternals.Add(name))
                return;

            Logger.LogError($"External >{name}< threw - it returns its default value instead. Later failures of " +
                            $"this external are not logged again. {e}", LogCat.ZenKit);
        }
    }
}
