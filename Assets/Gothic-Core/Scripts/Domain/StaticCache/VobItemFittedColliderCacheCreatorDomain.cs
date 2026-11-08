using System;
using System.Collections.Generic;
using Gothic.Core.Domain.Meshes;
using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.StaticCache
{
    /// <summary>
    /// Melee weapons (ItMw_*) get boxes fitted to their parts (blade, guard, grip, pommel, axe and hammer heads)
    /// instead of the width profile colliders of other items - boxes along the mesh axes: the slanted cross guard of
    /// ItMw_2H_Sword_Light_03 had a 28 x 20 cm box, axe heads boxes full of air. Thin parts stay (rods and shafts
    /// are held there). They are the hitboxes of the hero's swings.
    /// Fitted during pre-caching, as the fit samples every triangle. A box along the item's axes is stored as a plain
    /// box (BoxCollider), a slanted one as an oriented box (convex box mesh, VobItemColliderBuilder).
    /// </summary>
    public static class VobItemFittedColliderCacheCreatorDomain
    {
        // Each box is one collider on the weapon, which PhysX checks on every step it touches something.
        private const int _maxBoxesPerWeapon = 12;
        // Meters: how much bigger an axis aligned box may be than the fitted one to become a BoxCollider.
        private const float _maxAxisAlignedGrowth = 0.01f;

        public static bool IsMeleeWeapon(string visualName)
        {
            return visualName.StartsWith("ITMW", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// vertices: in meters, three per triangle (as VobItemColliderCacheCreatorDomain prepares them).
        /// </summary>
        public static List<VobItemColliderCacheCreatorDomain.Data> Create(string visualName, Vector3[] vertices,
            int[] triangles)
        {
            var boxes = ItemColliderFittingDomain.Fit(vertices, triangles, true, _maxBoxesPerWeapon,
                out var isTrimmed);

            if (isTrimmed)
                Logger.LogWarning($"{visualName} has more than {_maxBoxesPerWeapon} separate parts. " +
                                  "The smallest get no collider.", LogCat.PreCaching);

            var colliders = new List<VobItemColliderCacheCreatorDomain.Data>(boxes.Count);
            foreach (var box in boxes)
                colliders.Add(ToColliderData(box));
            return colliders;
        }

        /// <summary>
        /// A box whose axes are (nearly) the item's axes - in any order - becomes the box around it in item space.
        /// </summary>
        private static VobItemColliderCacheCreatorDomain.Data ToColliderData(ItemColliderFittingDomain.Box box)
        {
            var axes = new[]
            {
                box.Rotation * Vector3.right,
                box.Rotation * Vector3.up,
                box.Rotation * Vector3.forward
            };

            var alignedSize = Vector3.zero;
            var matchedAxes = 0;
            var isAxisAligned = true;
            for (var itemAxis = 0; itemAxis < 3; itemAxis++)
            {
                var boxAxis = 0;
                for (var a = 0; a < 3; a++)
                {
                    alignedSize[itemAxis] += Mathf.Abs(axes[a][itemAxis]) * box.Size[a];
                    if (Mathf.Abs(axes[a][itemAxis]) > Mathf.Abs(axes[boxAxis][itemAxis]))
                        boxAxis = a;
                }

                matchedAxes |= 1 << boxAxis;
                isAxisAligned &= alignedSize[itemAxis] - box.Size[boxAxis] <= _maxAxisAlignedGrowth;
            }

            if (isAxisAligned && matchedAxes == 0b111)
            {
                return new VobItemColliderCacheCreatorDomain.Data
                {
                    T = VobItemColliderCacheCreatorDomain.T.B,
                    C = box.Center,
                    S = alignedSize
                };
            }

            return new VobItemColliderCacheCreatorDomain.Data
            {
                T = VobItemColliderCacheCreatorDomain.T.O,
                C = box.Center,
                S = box.Size,
                Q = box.Rotation
            };
        }
    }
}
