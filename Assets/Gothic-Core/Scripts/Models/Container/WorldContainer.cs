using System.Collections.Generic;
using GUZ.Core.Models.Proxy;
using GUZ.Core.Services.Caches;
using UnityEngine;
using ZenKit;
using ZenKit.Vobs;
using Mesh = ZenKit.Mesh;

namespace GUZ.Core.Models.Container
{
    /// <summary>
    /// Parsed ZenKit World data is arranged in a way to easily be usable by Unity objects.
    /// E.g. by providing sub meshes.
    /// </summary>
    // FIXME - If we struggle memory issues, we should consider removing some data from memory (like cached BspTree) when switching worlds.
    public class WorldContainer
    {
        // Storing referenced to both worlds to keep shortcut data (child properties) below in memory and to later
        // Reuse for storing in between world switches and during save game creation.
        public ZenKit.World OriginalWorld;
        public ZenKit.World SaveGameWorld;


        // VOB related objects
        // We need to store it as we need the pointer to it for load+save of un-cached vobs.
        public List<IVirtualObject> Vobs;
        public List<NpcProxy> Npcs;

        // Cached objects - For performance reasons we only allow them cached. Otherwise every loop and getter will load them again.
        public CachedWayNet WayNet;

        // World related objects
        public Mesh Mesh;
        public CachedBspTree BspTree;
        public List<SubMeshData> SubMeshes;


        public class SubMeshData
        {
            public IMaterial Material;
            public TextureCacheService.TextureArrayTypes TextureArrayType;

            public readonly List<Vector3> Vertices = new();
            public readonly List<int> Triangles = new();
            public readonly List<Vector4> Uvs = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Color32> BakedLightColors = new();
            public readonly List<Vector4> TextureAnimations = new();
        }
    }
}
