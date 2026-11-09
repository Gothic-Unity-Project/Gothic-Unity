using System.Collections.Generic;
using Gothic.Core.Domain.StaticCache;
using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using TMPro;
using UnityEngine;

namespace Gothic.Core.Services.Caches
{
    /// <summary>
    /// Contains lookup caches for GameObjects, AudioClips and other Unity objects for faster use.
    /// Caching of ZenKit data is done inside ResourceLoader.cs
    /// </summary>
    public class MultiTypeCacheService
    {
        /// <summary>
        /// Hints:
        ///     * Includes NPCs and Hero (Easier for lookups like "what is the nearest enemy in range".)
        ///     * Also includes all monsters
        ///     * We need to ensure that any time an NpcInstance.UserData contains an NpcData object, that it is stored here.
        ///       Otherwise, UserData's WeakReference pointer gets cleared.
        /// </summary>
        public readonly List<NpcContainer> NpcCache = new();

        public readonly List<VobContainer> VobCache = new();


        /// <summary>
        /// This dictionary caches the sprite assets for fonts.
        /// </summary>
        public Dictionary<string, TMP_SpriteAsset> FontCache = new();

        /// <summary>
        /// Caching all types of Meshes (World?, Vob, Npc) to optimize memory usage of meshes if duplicated
        /// (e.g. multiple VOBs sharing the same mesh)
        /// </summary>
        public Dictionary<string, Mesh> Meshes = new();

        public Dictionary<string, AudioClip> AudioClips = new();

        /// <summary>
        /// Materials of texture-less, color-only meshes (e.g. G2's treasure X marks) - shared by all VOBs with the same
        /// color.
        /// </summary>
        public Dictionary<Color32, Material> UntexturedMaterials = new();

        /// <summary>
        /// Colliders of items without pre-cached ones, calculated from their mesh once per mesh name
        /// (VobItemColliderBuilder). An empty list: nothing could be fitted or sliced, the item gets a capsule.
        /// </summary>
        public Dictionary<string, List<VobItemColliderCacheCreatorDomain.Data>> ItemColliders = new();


        public void Init()
        {
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(delegate
            {
                NpcCache.ClearAndReleaseMemory();
                VobCache.ClearAndReleaseMemory();
            });
        }

        public void Dispose()
        {
            FontCache.Clear();
            Meshes.Clear();
            AudioClips.Clear();
            UntexturedMaterials.Clear();
            ItemColliders.Clear();
            NpcCache.Clear();
            VobCache.Clear();
        }
    }
}
