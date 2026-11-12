using System;
using Gothic.Core.Logging;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Extensions
{
    public static class ZenKitVmExtension
    {
        /// <summary>
        /// Reads the effect's user string as invariant-culture float. False if it is missing or unparsable.
        /// </summary>
        public static bool TryGetUserFloat(this EffectBaseInstance fx, int index, out float value)
        {
            return float.TryParse(fx.GetUserString(index), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Overrides the script function only if the scripts declare it with exactly the signature of the handler.
        /// With another signature (another Ikarus/LeGo version) the override would leave values on the VM stack or
        /// take foreign ones - then the script's own function stays.
        /// </summary>
        public static bool TryOverrideMatching(this DaedalusVm vm, string name, Action handler)
        {
            if (!vm.HasSignature(name, null))
                return false;
            vm.OverrideFunction(name, () => handler());
            return true;
        }

        public static bool TryOverrideMatching<TP0>(this DaedalusVm vm, string name, Action<TP0> handler)
        {
            if (!vm.HasSignature(name, null, typeof(TP0)))
                return false;
            vm.OverrideFunction<TP0>(name, p0 => handler(p0));
            return true;
        }

        public static bool TryOverrideMatching<TP0, TP1>(this DaedalusVm vm, string name, Action<TP0, TP1> handler)
        {
            if (!vm.HasSignature(name, null, typeof(TP0), typeof(TP1)))
                return false;
            vm.OverrideFunction<TP0, TP1>(name, (p0, p1) => handler(p0, p1));
            return true;
        }

        public static bool TryOverrideMatching<TP0, TP1, TP2>(this DaedalusVm vm, string name,
            Action<TP0, TP1, TP2> handler)
        {
            if (!vm.HasSignature(name, null, typeof(TP0), typeof(TP1), typeof(TP2)))
                return false;
            vm.OverrideFunction<TP0, TP1, TP2>(name, (p0, p1, p2) => handler(p0, p1, p2));
            return true;
        }

        public static bool TryOverrideMatching<TR>(this DaedalusVm vm, string name, Func<TR> handler)
        {
            if (!vm.HasSignature(name, typeof(TR)))
                return false;
            vm.OverrideFunction(name, () => handler());
            return true;
        }

        public static bool TryOverrideMatching<TR, TP0>(this DaedalusVm vm, string name, Func<TP0, TR> handler)
        {
            if (!vm.HasSignature(name, typeof(TR), typeof(TP0)))
                return false;
            vm.OverrideFunction(name, (TP0 p0) => handler(p0));
            return true;
        }

        /// <summary>
        /// Whether the scripts declare the function with exactly this return and parameter types (warns once if not).
        /// The Ikarus/LeGo overrides check it before they install their guarded handler.
        /// </summary>
        public static bool HasSignature(this DaedalusVm vm, string name, Type returnType, params Type[] paramTypes)
        {
            var sym = vm.GetSymbolByName(name);
            if (sym == null)
                return false;

            var fits = sym.Type == DaedalusDataType.Function && sym.Size == paramTypes.Length &&
                       sym.HasReturn == (returnType != null) &&
                       (returnType == null || Fits(sym.ReturnType, returnType));
            for (var i = 0; fits && i < paramTypes.Length; i++)
                fits = Fits(vm.GetSymbolByIndex(sym.Index + 1 + i)?.Type, paramTypes[i]);

            if (!fits)
                Logger.LogWarning($"[Ikarus] {name}: the scripts declare another signature - not overridden.",
                    LogCat.ZenKit);
            return fits;
        }

        private static bool Fits(DaedalusDataType? scriptType, Type type)
        {
            return scriptType switch
            {
                DaedalusDataType.Int or DaedalusDataType.Function => type == typeof(int),
                DaedalusDataType.Float => type == typeof(float),
                DaedalusDataType.String => type == typeof(string),
                DaedalusDataType.Instance => typeof(DaedalusInstance).IsAssignableFrom(type),
                _ => false
            };
        }
    }
}
