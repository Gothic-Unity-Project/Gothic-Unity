using System;
using System.Collections.Generic;
using GUZ.Core.Logging;
using JetBrains.Annotations;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Models.Caches
{
    /// <summary>
    /// <b>Represents a cached resource of a given type <see cref="T"/>.</b>
    /// <p>
    /// It works by getting a callback function, which
    /// can load a given asset by a name. If loading an asset using the loader succeeds, it is saved into an internal
    /// dictionary by name and returned immediately on subsequent loads of an asset with the same name.
    /// </p>
    /// <p>
    /// Caching is case-sensitive and errors during load are handled gracefully by returning <c>null</c>.
    /// </p>
    /// </summary>
    /// <typeparam name="T">The type of the resource, e.g. <see cref="Model"/></typeparam>
    public class ResourceCacheType<T>
    {
        private readonly Dictionary<string, T> _cache = new();
        private readonly Func<string, T> _loader;
        private readonly Func<string, GameVersion, T> _gameVersionLoader;

        /// <summary>
        /// Constructs a new resource given a loader.
        /// </summary>
        /// <param name="loader">
        /// A function which, when called, attempts to load an asset of type <see cref="T"/> with a given name. This
        /// function is only called once for each unique asset name. May throw.
        /// </param>
        public ResourceCacheType(Func<string, T> loader)
        {
            _loader = loader;
        }

        public ResourceCacheType(Func<string, GameVersion, T> gameVersionLoader)
        {
            _gameVersionLoader = gameVersionLoader;
        }

        /// <summary>
        /// Tries to load an asset of type <see cref="T"/> with the given name (<see cref="key"/>). If the asset has
        /// already been loaded before, a cached version of the asset is returned, saving a call to <see cref="_loader"/>,
        /// otherwise, the <see cref="_loader"/> is called and its result cached and returned.
        /// </summary>
        /// <param name="key">The name of the asset to load by calling the <see cref="_loader"/> function</param>
        /// <param name="value">The asset to be returned (out)</param>
        /// <param name="pringNotFoundWarning">Some elements like mdm and mdl need to be checked together. Ignore if one of these doesn't exist (expected behavior with Gothic files.)</param>
        /// <returns><c>true</c> if loading the asset succeeded and <c>false</c> if it failed</returns>
        public bool TryLoad([NotNull] string key, out T value, bool pringNotFoundWarning = true)
        {
            if (_cache.TryGetValue(key, out value))
            {
                return true;
            }

            try
            {
                value = _loader(key);
                _cache[key] = value;
                return true;
            }
            catch (Exception e)
            {
                if (pringNotFoundWarning)
                {
                    // Need to log missing files. Otherwise, we will never know which file is missing.
                    Logger.LogWarning($"Resource {key} not found: {e.Message}", LogCat.Loading);
                }
            }

            _cache[key] = default;
            return false;
        }

        public bool TryLoad([NotNull] string key, GameVersion version, out T value)
        {
            if (_cache.TryGetValue(key, out value))
            {
                return true;
            }

            try
            {
                value = _gameVersionLoader(key, version);
                _cache[key] = value;
                return true;
            }
            catch (Exception)
            {
                // ignored
            }

            _cache[key] = default;
            return false;
        }

        public void Dispose()
        {
            _cache.Clear();
            _cache.TrimExcess();
        }
    }
}
