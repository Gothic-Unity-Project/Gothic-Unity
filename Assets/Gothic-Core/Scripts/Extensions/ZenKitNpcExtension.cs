using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Gothic.Core.Models.Proxy;
using ZenKit.Vobs;

namespace Gothic.Core.Extensions
{
    /// <summary>
    /// Workarounds for ZenKit bindings of oCNpc VOBs.
    /// </summary>
    public static class ZenKitNpcExtension
    {
        private const string _dllName = "zenkitcapi";

        [DllImport(_dllName)]
        private static extern IntPtr ZkNpc_getAiVars(UIntPtr slf, out ulong count);

        private static readonly FieldInfo _handleField = typeof(VirtualObject)
            .GetField("Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);


        /// <summary>
        /// ZenKitCAPI's ZkNpc_setAiVars copies the array length as BYTES (memcpy(aivar, vars, len)), i.e.
        /// Npc.AiVars = int[50] only stores the first 12 values. ZkNpc_getAiVars returns the native int[100] array,
        /// we therefore write through it. Returns false if the VOB has no native handle.
        /// FIXME - Remove once https://github.com/GothicKit/ZenKitCAPI/pull/5 is merged and shipped with ZenKitCS.
        /// </summary>
        public static bool TrySetAiVars(this IVirtualObject npc, int[] values)
        {
            if (npc is VirtualObjectProxy proxy)
                npc = proxy.GetVob();

            if (npc is not VirtualObject zkNpc || _handleField == null)
                return false;

            var ptr = ZkNpc_getAiVars((UIntPtr)_handleField.GetValue(zkNpc), out var count);
            if (ptr == IntPtr.Zero)
                return false;

            Marshal.Copy(values, 0, ptr, (int)Math.Min((ulong)values.Length, count));
            return true;
        }
    }
}
