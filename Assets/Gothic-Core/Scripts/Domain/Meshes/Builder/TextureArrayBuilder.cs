using System;
using System.Threading.Tasks;
using GUZ.Core.Services.Caches;
using UnityEngine;

namespace GUZ.Core.Domain.Meshes.Builder
{
    /// <summary>
    /// Create texture array for all meshes. Basically no MeshBuilder,
    /// but we inherit the abstract builder to leverage some methods.
    /// </summary>
    public class TextureArrayBuilder : AbstractMeshBuilder
    {
        public override GameObject Build()
        {
            throw new NotImplementedException("Use BuildAsync instead.");
        }

        public async Task BuildAsync()
        {
            await TextureCacheService.BuildTextureArray();
        }
    }
}
