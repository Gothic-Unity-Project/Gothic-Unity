using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Gothic.Core.Logging;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Extensions
{
    public static class ZenKitVmExtension
    {
        private const string _zenKitLibrary = "zenkitcapi";

        // ZenKit keeps the native VM pointer internal; the string calls below need it.
        private static readonly FieldInfo _nativeHandle =
            typeof(DaedalusScript).GetField("Handle", BindingFlags.NonPublic | BindingFlags.Instance);

        [DllImport(_zenKitLibrary)]
        private static extern IntPtr ZkDaedalusVm_popString(UIntPtr vm);

        [DllImport(_zenKitLibrary)]
        private static extern void ZkDaedalusVm_pushString(UIntPtr vm, byte[] value);

        /// <summary>
        /// Whether strings can be exchanged with the VM as raw bytes (PopNativeString / PushNativeString).
        /// False if a ZenKit version renamed its internal handle - callers then use ZenKit's own string calls.
        /// </summary>
        public static bool HasNativeStrings => _nativeHandle != null;

        /// <summary>
        /// ZenKit pitfall: it hands C# strings to its native library through a custom marshaller (the code page of
        /// the installation). IL2CPP (Quest, Pico) ignores custom marshallers and sends UTF-8 instead. A string with
        /// umlauts pushed by ZenKit's DaedalusVm.Push comes back scrambled once the scripts hand it on: each
        /// non-ASCII character shows up as two wrong ones. Mono (editor, Windows build) is fine. This pushes the
        /// bytes in the game's code page itself.
        /// </summary>
        public static void PushNativeString(this DaedalusVm vm, string value, Encoding encoding)
        {
            ZkDaedalusVm_pushString((UIntPtr)_nativeHandle.GetValue(vm), ToNativeString(value, encoding));
        }

        /// <summary>
        /// Counterpart of PushNativeString for externals registered without ZenKit's typed parameters: takes the
        /// top string off the VM stack. Parameters come off last first.
        /// </summary>
        public static string PopNativeString(this DaedalusVm vm, Encoding encoding)
        {
            var pointer = ZkDaedalusVm_popString((UIntPtr)_nativeHandle.GetValue(vm));
            if (pointer == IntPtr.Zero)
                return string.Empty;

            var length = 0;
            while (Marshal.ReadByte(pointer, length) != 0)
                length++;
            if (length == 0)
                return string.Empty;

            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return encoding.GetString(bytes);
        }

        /// <summary>
        /// The string as a zero-terminated C string in the given code page.
        /// </summary>
        public static byte[] ToNativeString(string value, Encoding encoding)
        {
            value ??= string.Empty;
            // One byte more than the text: it stays 0 and ends the C string.
            var bytes = new byte[encoding.GetByteCount(value) + 1];
            encoding.GetBytes(value, 0, value.Length, bytes, 0);
            return bytes;
        }

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
