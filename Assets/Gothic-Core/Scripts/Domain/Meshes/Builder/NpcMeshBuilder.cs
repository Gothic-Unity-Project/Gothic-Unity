using System.Collections.Generic;
using System.Text.RegularExpressions;
using GUZ.Core.Extensions;
using Gothic.Core.Logging;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services.Caches;
using GUZ.VR.Adapters.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
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
            CreateBodyAabbCollider();

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
        /// Gothic stores a pre-baked collision AABB in the MDH file.
        /// A single BoxCollider on the root GO matches Gothic's approach: the box is
        /// static relative to the NPC's feet and is never deformed by animations.
        /// </summary>
        private void CreateBodyAabbCollider()
        {
            if (Mdh == null)
                return;

            // TODO - For NPC, the CollisionBoundingBox is quite narrow. Think about using Mdh.BoundingBox for VR as it's broader for better hit detection.
            var bounds = Mdh.CollisionBoundingBox.ToUnityBounds();
            RootGo.GetComponentInChildren<NpcHitboxColliderAdapter>().SetDimension(bounds);
        }
    }
}
