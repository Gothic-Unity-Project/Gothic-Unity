using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// How big an NPC's body is - dialog distances grow with it (a dragon's head is meters away from the hero).
    /// </summary>
    public static class NpcBodySize
    {
        // Humans and small monsters - no extra distance for them.
        private const float _minRadius = 0.5f;

        /// <summary>
        /// Horizontal radius (m) of the NPC's visible body (skinned meshes), at least _minRadius.
        /// </summary>
        public static float GetRadius(NpcContainer npc)
        {
            if (npc?.Go == null)
                return _minRadius;

            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var skinnedRenderer in npc.Go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!hasBounds)
                {
                    bounds = skinnedRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(skinnedRenderer.bounds);
                }
            }

            return hasBounds ? Mathf.Max(_minRadius, Mathf.Max(bounds.extents.x, bounds.extents.z)) : _minRadius;
        }
    }
}
