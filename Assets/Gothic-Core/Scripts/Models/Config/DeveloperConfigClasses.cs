using System;
using UnityEngine;

namespace GUZ.Core.Models.Config
{
    [Serializable]
    public class MeshCullingGroup
    {
        [Range(1f, 100f)]
        public float MaximumObjectSize;

        [Range(1f, 1000f)]
        public float CullingDistance;
    }
}
