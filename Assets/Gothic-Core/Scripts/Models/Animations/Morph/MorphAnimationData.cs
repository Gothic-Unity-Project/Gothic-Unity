using System.Collections.Generic;
using UnityEngine;
using ZenKit;

namespace Gothic.Core.Models.Animations.Morph
{
    public class MorphAnimationData
    {
        // Time data
        public float Time;
        public bool IsLooping;

        // Blended morphs (HeadMorph, DeveloperConfig.EnableFaceAnimations), like the engine's face animations:
        // Weight = intensity (Mdl_StartFaceAni), HoldSeconds > 0 = stop after it, < 0 = hold until stopped,
        // 0 = the animation's own duration. BlendWeight follows the MMB blend in/out, HoldTimer restarts with a refresh.
        public float Weight = 1f;
        public float HoldSeconds;
        public float HoldTimer;
        public float BlendWeight;
        public bool IsStopping;

        // Mesh data
        public IMorphMesh MeshMetadata;
        public string MeshName => MeshMetadata.Name;

        // Animation data
        public IMorphAnimation AnimationMetadata;
        public List<Vector3[]> AnimationFrameData;
        public string AnimationName => AnimationMetadata.Name;
        public int AnimationFrameCount => AnimationMetadata.FrameCount;
    }
}
