using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GUZ.Core.Logging;
using Reflex.Injectors;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Extensions
{
    public static class BuiltInTypeExtension
    {
        /// <summary>
        /// Execute on newly created C# object to execute DI injection.
        /// Please use it only, when needed as it causes some CPU cycles when done.
        ///
        /// Checks for [Inject] properties and methods.
        /// </summary>
        public static T Inject<T>(this T instance)
        {
            // e.g., Lab's MonoBehaviours are not yet initialized when scene is added in editor mode, but this method gets called. Ignore it.
            if (ReflexProjectInstaller.DIContainer == null)
                return instance;

            AttributeInjector.Inject(instance, ReflexProjectInstaller.DIContainer);
            return instance;
        }


        /// <summary>
        /// await (async Task) calls silently drop exceptions.
        /// This call logs them at least to make it easier to debug.
        /// </summary>
        public static async Task AwaitAndLog(this Task awaitable)
        {
            try
            {
                await awaitable;
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString(), LogCat.Misc);
                throw;
            }
        }

        /// <summary>
        /// await (async Task&lt;T&gt;) calls silently drop exceptions.
        /// This call logs them at least to make it easier to debug.
        /// </summary>
        public static async Task<T> AwaitAndLog<T>(this Task<T> awaitable)
        {
            try
            {
                return await awaitable;
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString(), LogCat.Misc);
                throw;
            }
        }


        public static bool IsEmpty(this string self)
        {
            return !self.Any();
        }

        public static bool IsEmpty<T>(this IEnumerable<T> self)
        {
            return !self.Any();
        }

        public static bool EqualsIgnoreCase(this string self, string other)
        {
            if (self == null)
            {
                return other == null;
            }

            return self.Equals(other, StringComparison.OrdinalIgnoreCase);
        }

        public static bool StartsWithIgnoreCase(this string self, string other)
        {
            return self.StartsWith(other, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ContainsIgnoreCase(this string self, string other)
        {
            return self.Contains(other, StringComparison.OrdinalIgnoreCase);
        }

        public static bool EndsWithIgnoreCase(this string self, string other)
        {
            return self.EndsWith(other, StringComparison.OrdinalIgnoreCase);
        }

        public static string TrimEndIgnoreCase(this string self, string pattern)
        {
            if (string.IsNullOrEmpty(self) || string.IsNullOrEmpty(pattern))
            {
                return self;
            }

            if (self.EndsWithIgnoreCase(pattern))
            {
                self = self.Substring(0, self.Length - pattern.Length);
            }

            return self;
        }

        /// <summary>
        /// For huge lists and elements where data is (e.g.) structs, it makes sense to set Capacity=0,
        /// which releases the elements finally. If you have a smaller Collection with references only, it's not needed.
        ///
        /// Please judge for yourself.
        /// </summary>
        public static void ClearAndReleaseMemory<TKey>(this List<TKey> self)
        {
            self.Clear();
            self.TrimExcess(); // Sets Capacity=0 and releases the "empty" objects from memory.
        }

        /// <summary>
        /// For huge lists and elements where data is (e.g.) structs, it makes sense to set Capacity=0,
        /// which releases the elements finally. If you have a smaller Collection with references only, it's not needed.
        ///
        /// Please judge for yourself.
        /// </summary>
        public static void ClearAndReleaseMemory<TKey, TValue>(this Dictionary<TKey, TValue> self)
        {
            self.Clear();
            self.TrimExcess(); // Sets Capacity=0 and releases the "empty" objects from memory.
        }

        /// <summary>
        /// ZenKit delivers values mostly in cm. Convenient method to move to Meter.
        /// </summary>
        public static float ToMeter(this int cmValue)
        {
            return (float)cmValue / 100;
        }

        /// <summary>
        /// ZenKit delivers values mostly in cm. Convenient method to move to Meter.
        /// </summary>
        public static float ToMeter(this float cmValue)
        {
            return cmValue / 100;
        }

        public static int ToCentimeter(this int cmValue)
        {
            return cmValue * 100;
        }

        public static float ToCentimeter(this float cmValue)
        {
            return cmValue * 100;
        }
    }
}
