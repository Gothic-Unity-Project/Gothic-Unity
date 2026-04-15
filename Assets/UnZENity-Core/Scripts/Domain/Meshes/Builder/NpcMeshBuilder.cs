using System.Collections.Generic;
using System.Text.RegularExpressions;
using GUZ.Core.Logging;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services.Caches;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = GUZ.Core.Logging.Logger;
using Mesh = UnityEngine.Mesh;
using Vector3 = System.Numerics.Vector3;

namespace GUZ.Core.Domain.Meshes.Builder
{
    public class NpcMeshBuilder : AbstractMeshBuilder
    {
        [Inject] private readonly NpcArmorPositionCacheService _npcArmorCacheService;


        protected ExtSetVisualBodyData BodyData;

        public virtual void SetBodyData(ExtSetVisualBodyData body)
        {
            BodyData = body;
        }

        public override GameObject Build()
        {
            BuildViaMdmAndMdh();
            CreateBoneColliders();

            return RootGo;
        }

        protected override GameObject[] BuildViaMdmAndMdh()
        {
            var nodeObjects = base.BuildViaMdmAndMdh();

            AddFistCollider(nodeObjects);

            return nodeObjects;
        }

        private void AddFistCollider(GameObject[] nodeObjects)
        {
            foreach (var nodeObject in nodeObjects)
            {
                if (nodeObject.name == "BIP01 L HAND" || nodeObject.name == "BIP01 R HAND")
                {
                    // var capsuleCollider = nodeObject.AddComponent<FistFightAdapter>();
                }
            }
        }

        /// <summary>
        /// Change texture name based on VisualBodyData.
        /// </summary>
        protected override Texture2D GetTexture(string name)
        {
            var finalTextureName =
                // This regex replaces the suffix of V0_C0 with values of corresponding data.
                // e.g. Some_Texture_V0_C0.TGA --> Some_Texture_V1_C2.TGA
                Regex.Replace(name, "(?<=.*?)V0_C0",
                    $"V{BodyData.BodyTexNr}_C{BodyData.BodyTexColor}");

            return base.GetTexture(finalTextureName);
        }

        protected override Dictionary<string, IMultiResolutionMesh> GetFilteredAttachments(
            Dictionary<string, IMultiResolutionMesh> attachments)
        {
            Dictionary<string, IMultiResolutionMesh> newAttachments = new(attachments);

            // Remove head as it will be loaded later.
            if (newAttachments.Remove("BIP01 HEAD"))
            {
                Logger.Log("Removed default >BIP01 HEAD< attachment mesh from NPC.", LogCat.Mesh);
            }

            return newAttachments;
        }

        /// <summary>
        /// Positions in mdm files for NPC armor isn't what it seems to be. We need to calculate the real data from weights.
        /// Please check the Cache class for more details.
        /// </summary>
        protected override List<Vector3> GetSoftSkinMeshPositions(ISoftSkinMesh softSkinMesh)
        {
            return _npcArmorCacheService.TryGetPositions(softSkinMesh, Mdh);
        }
        
        /// <summary>
        /// During fight situations, the bones are checked for physical collision via e.g. *eventTag(0 "DEF_HIT_LIMB" "BIP01 R HAND")
        /// We therefore calculate a box collider for all of the limbs/bones and disable it until its needed at fight time.
        ///
        /// Hint: We assume that the bounding boxes of the bones will stay stable and no long stretches will happen
        ///       (which would force a recalculation).
        /// </summary>
        private void CreateBoneColliders()
        {
            var renderers = RootGo.GetComponentsInChildren<SkinnedMeshRenderer>();
            var boneBoundsMap = new Dictionary<Transform, Bounds>();

            foreach (var renderer in renderers)
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null)
                    continue;

                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                var smrBones = renderer.bones;
                var bindPoses = mesh.bindposes;

                for (var i = 0; i < vertices.Length; i++)
                {
                    var weight = weights[i];
                    var boneIdx = weight.boneIndex0;

                    // Use vertices with more than 10% weight.
                    if (weight.weight0 > 0.1f)
                    {
                        var boneTransform = smrBones[boneIdx];
                
                        // DIRECT CALCULATION:
                        // Multiply the vertex by the bind pose matrix to get the 
                        // position relative to the bone at the time of rigging.
                        var localPt = bindPoses[boneIdx].MultiplyPoint3x4(vertices[i]);

                        if (!boneBoundsMap.ContainsKey(boneTransform))
                        {
                            boneBoundsMap[boneTransform] = new Bounds(localPt, UnityEngine.Vector3.zero);
                        }
                        else
                        {
                            var bounds = boneBoundsMap[boneTransform];
                            bounds.Encapsulate(localPt);
                            boneBoundsMap[boneTransform] = bounds;
                        }
                    }
                }
            }

            // Apply to Colliders
            foreach (var boneBound in boneBoundsMap)
            {
                var boneTransform = boneBound.Key;
                var finalBounds = boneBound.Value;

                if (finalBounds.size.sqrMagnitude < 0.0001f)
                    continue;

                var col = boneTransform.gameObject.AddComponent<BoxCollider>();
                col.center = finalBounds.center;
                col.size = finalBounds.size;
                col.isTrigger = true; // We want to calculate Triggering only, not pushing/colliding.
                // FIXME - Activate colliders dynamically when DEF_HIT_LIMB is active. Then we save compute power when no one is fighting...!
                col.enabled = true; // Will be enabled at runtime during fights when DEF_HIT_LIMB is set.
            }
        }
    }
}
