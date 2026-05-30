using System.Collections.Generic;
using GUZ.Core.Extensions;
using UnityEngine;
using ZenKit;
using Vector3 = System.Numerics.Vector3;

namespace GUZ.Core.Services.Caches
{
    public class NpcArmorPositionCacheService
    {
        private Dictionary<IModelHierarchy, List<Matrix4x4>> _bonesInWorldSpace = new();
        private Dictionary<ISoftSkinMesh, List<Vector3>> _correctedVertexPositions = new();

        /// <summary>
        /// Return calculated positions for NPC armor vertices.
        ///
        /// NPC armor meshes are stored within mdm files. These files contain vertex positions two times:
        /// 1. mdm.SoftSkinMeshes[].Mesh.Positions --> These positions can create a proper mesh, but! without any logical
        ///    bone connection in space. --> Do not use for armor!
        /// 2. mdm.SoftSkinMeshes[].Weights --> A calculation of mdh's bones position with vertices and their weights offers
        ///    the vertex of each armor at the appropriate place onto a bone.
        /// My best guess about the "why" is:
        /// * If you create an armor with Blender, then it stores the new one in an .asc file. (I think this is needed,
        ///   as you could potentially move the full mesh in world space before saving it, so that e.g. the left hand
        ///   in Blender isn't saved at the same spot, the hand bone is for the .mdh file information.)
        /// * The .asc file contains all vertices AND bone positions.
        /// * Once you put the .asc file onto Gothic folder, start the game, and load the file for the first time, then
        ///   Gothic will auto-compile it to an .mdm file where the actual bone information is put from readable vector3
        ///   in Mesh.Positions to the "hidden" information inside SoftSkinMesh.Weights.
        /// * I think the calculation at runtime for the armor position in ZenGine benefits from this calculation, but not for Unity
        ///   At least we have the calculation used right now! ;-)
        ///
        /// Be careful: In this cache handler, we store the I* objects from ZenKit itself. Please take care you chose
        /// the cached objects or otherwise you will suffer performance death. ;-)
        /// </summary>
        /// <returns></returns>
        public List<Vector3> TryGetPositions(ISoftSkinMesh softSkinMesh, IModelHierarchy mdh)
        {
            if (!_bonesInWorldSpace.ContainsKey(mdh))
            {
                CalculateBonesInWorldSpace(mdh);
            }

            if (!_correctedVertexPositions.TryGetValue(softSkinMesh, out var currentCorrectedVertexPositions))
            {
                currentCorrectedVertexPositions = CalculateCorrectedVertexPositions(softSkinMesh, mdh);
            }

            return currentCorrectedVertexPositions;
        }

        private void CalculateBonesInWorldSpace(IModelHierarchy mdh)
        {
            List<Matrix4x4> retValue = new();

            foreach (var node in mdh.Nodes)
            {
                Matrix4x4 newTransform;

                if (node.ParentIndex == -1)
                {
                    newTransform = Matrix4x4.Translate(mdh.RootTranslation.ToUnityVector(false));
                }
                else
                {
                    newTransform = retValue[node.ParentIndex];
                }

                newTransform *= node.Transform.ToUnityMatrix();

                retValue.Add(newTransform);
            }

            _bonesInWorldSpace[mdh] = retValue;
        }

        /// <summary>
        /// We transform to UnityVector in between as calculation was easier (or at least we found a method which works)
        /// </summary>
        private List<Vector3> CalculateCorrectedVertexPositions(ISoftSkinMesh softSkinMesh, IModelHierarchy mdh)
        {
            List<Vector3> retValue = new();

            for (var vertexId = 0; vertexId < softSkinMesh.Weights.Count; vertexId++)
            {
                var vertexPosition = new UnityEngine.Vector3(0, 0, 0);

                foreach (var weight in softSkinMesh.Weights[vertexId])
                {
                    var mdhBoneMatrix = _bonesInWorldSpace[mdh][weight.NodeIndex];

                    var newPos = mdhBoneMatrix.MultiplyPoint(weight.Position.ToUnityVector(false));
                    vertexPosition += newPos * weight.Weight;
                }

                retValue.Add(new Vector3(vertexPosition.x, vertexPosition.y, vertexPosition.z));
            }

            _correctedVertexPositions[softSkinMesh] = retValue;

            return retValue;
        }

        public void Dispose()
        {
            _bonesInWorldSpace.Clear();
            _correctedVertexPositions.Clear();
        }
    }
}
