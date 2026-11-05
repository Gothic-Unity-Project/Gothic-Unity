using System.Collections.Generic;
using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The VR hero body's mesh copies cut by bone weights (VRHeroBody): without the model's hands, the bare forearms,
    /// the sleeveless armor. Remembers every mesh it created - DestroyCreatedMeshes frees them (rebuild, destroy).
    /// </summary>
    public class VrHeroBodyMeshCutter
    {
        // Triangles weighted on average more than this to a part (hand, forearm) belong to it (see CutArms).
        public const float ArmCutWeight = 0.5f;
        private const float _bareForearmWeight = 0.3f;
        // Armor triangles reaching this far into the forearm stay (a short overlap with the bare forearm).
        private const float _sleeveKeepWeight = 0.75f;

        private readonly List<Mesh> _createdMeshes = new();

        /// <summary>
        /// Copy of the mesh cut by the bone weights of its triangles (average of the 3 vertices, 50 % limit - the bare
        /// forearms and the sleeveless armor meet without gap or overlap). Null if nothing is left or the mesh can't be
        /// read.
        /// </summary>
        public Mesh CutArms(Mesh mesh, Transform[] bones, VrHeroArmCut cut)
        {
            if (mesh == null || !mesh.isReadable || bones == null)
                return null;

            var isForearmBone = new bool[bones.Length];
            var isHandBone = new bool[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                var name = bones[i] != null ? bones[i].name.ToUpperInvariant() : string.Empty;
                isForearmBone[i] = name.Contains("FOREARM");
                isHandBone[i] = name.Contains(" HAND") || name.Contains("FINGER");
            }

            var weights = mesh.boneWeights;
            if (weights.Length != mesh.vertexCount)
                return null;

            float WeightOf(int vertex, bool[] isBone)
            {
                var w = weights[vertex];
                return Of(w.boneIndex0, w.weight0) + Of(w.boneIndex1, w.weight1) +
                       Of(w.boneIndex2, w.weight2) + Of(w.boneIndex3, w.weight3);

                float Of(int boneIndex, float weight) =>
                    boneIndex >= 0 && boneIndex < isBone.Length && isBone[boneIndex] ? weight : 0f;
            }

            float SleeveWeight(int vertex) => WeightOf(vertex, isForearmBone) + WeightOf(vertex, isHandBone);

            var copy = Object.Instantiate(mesh);
            copy.name = $"{mesh.name}_{cut}";
            var keptTriangles = 0;
            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var triangles = mesh.GetTriangles(subMesh);
                var kept = new List<int>(triangles.Length);
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var hand = (WeightOf(triangles[i], isHandBone) + WeightOf(triangles[i + 1], isHandBone) +
                                WeightOf(triangles[i + 2], isHandBone)) / 3f;
                    var forearm = (WeightOf(triangles[i], isForearmBone) + WeightOf(triangles[i + 1], isForearmBone) +
                                   WeightOf(triangles[i + 2], isForearmBone)) / 3f;
                    var isKept = cut switch
                    {
                        VrHeroArmCut.WithoutHands => hand <= ArmCutWeight,
                        // A bit up into the armor - the cut sleeve overlaps it, no gap at the elbow.
                        VrHeroArmCut.Forearms => hand <= ArmCutWeight && forearm + hand > _bareForearmWeight,
                        VrHeroArmCut.ForearmsAndHands => forearm + hand > _bareForearmWeight,
                        // No triangle with a forearm vertex: it stuck out straight from the bent elbow (spikes).
                        _ => Mathf.Max(SleeveWeight(triangles[i]), SleeveWeight(triangles[i + 1]),
                            SleeveWeight(triangles[i + 2])) <= _sleeveKeepWeight
                    };
                    if (!isKept)
                        continue;
                    kept.Add(triangles[i]);
                    kept.Add(triangles[i + 1]);
                    kept.Add(triangles[i + 2]);
                }
                copy.SetTriangles(kept, subMesh);
                keptTriangles += kept.Count / 3;
            }

            if (cut == VrHeroArmCut.Sleeveless)
                MoveForearmWeightsToUpperArm(copy, bones, isForearmBone, isHandBone);

            Logger.Log($"[VRHeroBody] {copy.name}: {keptTriangles} of {mesh.triangles.Length / 3} triangles.",
                LogCat.VR);
            if (keptTriangles == 0)
            {
                Object.Destroy(copy);
                return null;
            }
            _createdMeshes.Add(copy);
            return copy;
        }

        public void DestroyCreatedMeshes()
        {
            foreach (var mesh in _createdMeshes)
            {
                if (mesh != null)
                    Object.Destroy(mesh);
            }
            _createdMeshes.Clear();
        }

        /// <summary>
        /// Rolled-up sleeve: the elbow vertices that stay are partly weighted to the forearm - stretched by the IK they
        /// became spikes towards the wrist. Their forearm/hand weight goes to the bone above (upper arm): the sleeve
        /// ends rigid at the elbow.
        /// </summary>
        private static void MoveForearmWeightsToUpperArm(Mesh mesh, Transform[] bones, bool[] isForearmBone,
            bool[] isHandBone)
        {
            var target = new int[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                target[i] = i;
                if (!isForearmBone[i] && !isHandBone[i])
                    continue;
                // Nearest ancestor that is neither forearm nor hand (BIP01 x UPPERARM).
                for (var parent = bones[i].parent; parent != null; parent = parent.parent)
                {
                    var index = System.Array.IndexOf(bones, parent);
                    if (index >= 0 && !isForearmBone[index] && !isHandBone[index])
                    {
                        target[i] = index;
                        break;
                    }
                }
            }

            int Remap(int boneIndex) => boneIndex >= 0 && boneIndex < target.Length ? target[boneIndex] : boneIndex;
            var weights = mesh.boneWeights;
            for (var v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                w.boneIndex0 = Remap(w.boneIndex0);
                w.boneIndex1 = Remap(w.boneIndex1);
                w.boneIndex2 = Remap(w.boneIndex2);
                w.boneIndex3 = Remap(w.boneIndex3);
                weights[v] = w;
            }
            mesh.boneWeights = weights;
        }
    }
}
