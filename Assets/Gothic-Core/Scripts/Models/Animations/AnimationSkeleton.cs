using Unity.Collections;
using UnityEngine;

namespace Gothic.Core.Models.Animations
{
    /// <summary>
    /// Skeleton data of one model hierarchy (.mdh), baked once by AnimationService and shared across all NPCs
    /// using the same model script: transform paths to resolve the bone GameObjects, the rest pose written for
    /// bones no animation drives, and root bone information (its position is owned by root motion).
    /// </summary>
    public class AnimationSkeleton
    {
        // Bone paths relative to the NPC root (e.g. "BIP01/BIP01 PELVIS/..."), index == mdh node index.
        public string[] Paths;

        public int RootNodeIndex;

        // RootTranslation.y in meters - resting height of the root bone above the feet.
        // Used to size the physics walk capsule (see AnimationSystem.ResizeRootCollider).
        public float RootHeight;

        public NativeArray<Vector3> RestPositions;
        public NativeArray<Quaternion> RestRotations;

        // Zero-length placeholder for unused job slots (job NativeArray fields always need a created array).
        public NativeArray<int> EmptyBoneMap;

        public int NodeCount => Paths.Length;

        public void Dispose()
        {
            if (RestPositions.IsCreated)
                RestPositions.Dispose();
            if (RestRotations.IsCreated)
                RestRotations.Dispose();
            if (EmptyBoneMap.IsCreated)
                EmptyBoneMap.Dispose();
        }
    }
}
