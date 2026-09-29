using System.Collections.Generic;
using System.Text.RegularExpressions;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.VR.Adapters.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
using Mesh = UnityEngine.Mesh;
using Vector3 = System.Numerics.Vector3;

namespace Gothic.Core.Domain.Meshes.Builder
{
    public class NpcMeshBuilder : AbstractMeshBuilder
    {
        [Inject] private readonly NpcArmorPositionCacheService _npcArmorCacheService;


        // Meter of extra hitbox reach per side around the model's bounding box.
        private const float _hitboxPadding = 0.1f;

        protected ExtSetVisualBodyData BodyData;

        public virtual void SetBodyData(ExtSetVisualBodyData body)
        {
            BodyData = body;
        }

        public override GameObject Build()
        {
            BuildViaMdmAndMdh();
            SetNpcMeshLayers();
            CreateBodyAabbCollider();

            return RootGo;
        }

        private void SetNpcMeshLayers()
        {
            foreach (var t in RootGo.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<Rigidbody>() != null)
                    continue; // RootMotionCollider stays on VobNpcOrMonster
                if (t.GetComponent<NpcHitboxColliderAdapter>() != null)
                    continue; // WeaponHitboxCollider stays on VobHitbox
                t.gameObject.layer = Constants.VobItemNoWorldCollision;
            }
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

            // Only strip BIP01 HEAD when a separate head MMB file replaces it (human NPCs).
            // Monsters like Skeleton bake the skull directly into the MDM attachment — removing it
            // leaves them headless because NpcHeadMeshBuilder has nothing to load (Head="").
            if (!string.IsNullOrEmpty(BodyData.Head) && newAttachments.Remove("BIP01 HEAD"))
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
        /// Gothic stores two pre-baked AABBs in the MDH file, both relative to the root bone (BIP01) - which is
        /// where the hitbox lives. The CollisionBoundingBox is meant for world collision and is tiny for hit
        /// detection (Humans: 34x125x16cm, starting 28cm above the feet; Wolf: 30x83x96cm, starting 23cm above
        /// the ground). We therefore use the full model BoundingBox (Humans: 69x169x33cm, Wolf: 59x113x193cm,
        /// reaching down to the feet) plus some padding, as VR swings are far less precise than a gamepad attack.
        /// </summary>
        private void CreateBodyAabbCollider()
        {
            if (Mdh == null)
                return;

            var bounds = Mdh.BoundingBox.ToUnityBounds();
            bounds.Expand(_hitboxPadding * 2f); // Expand() adds the amount to size, so we grow by padding per side.
            RootGo.GetComponentInChildren<NpcHitboxColliderAdapter>().SetDimension(bounds);
        }
    }
}
