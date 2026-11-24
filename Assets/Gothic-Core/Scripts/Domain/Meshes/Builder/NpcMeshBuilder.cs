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

        // Attachments live in an own child of their bone, so an armor change can remove them again.
        private const string _attachmentGoName = "_Attachment";

        protected ExtSetVisualBodyData BodyData;
        private bool _isHitboxExpected;

        public virtual void SetBodyData(ExtSetVisualBodyData body)
        {
            BodyData = body;
        }

        /// <summary>
        /// True for NPCs built into the NPC prefab (NpcHitboxColliderAdapter must exist). Visual-only bodies (VR hero
        /// body, Lab) have none - nothing to warn about.
        /// </summary>
        public void SetHitboxExpected(bool isHitboxExpected)
        {
            _isHitboxExpected = isHitboxExpected;
        }

        public override GameObject Build()
        {
            BuildViaMdmAndMdh();
            CreateBodyAabbCollider();

            return RootGo;
        }

        /// <summary>
        /// Runtime armor change (EquipItem, AI_EquipArmor, Mdl_SetVisualBody, a script taking the armor): replaces
        /// the body's skinned meshes (ZM_*) and the armor's attachments (e.g. Greg's hat) with the ones of the current
        /// .mdm on the NPC's existing skeleton. Head, weapons, helmet, colliders and the animation stay untouched.
        /// </summary>
        public bool RebuildBody()
        {
            if (Mdh == null || Mdm == null)
            {
                Logger.LogWarning($"[ArmorVisual] No .mdh/.mdm '{MeshName}' for {RootGo.name} - body not rebuilt.",
                    LogCat.Mesh);
                return false;
            }

            var nodeObjects = new GameObject[Mdh.Nodes.Count];
            for (var i = 0; i < Mdh.Nodes.Count; i++)
            {
                nodeObjects[i] = RootGo.FindChildRecursively(Mdh.Nodes[i].Name);
                if (nodeObjects[i] == null)
                {
                    Logger.LogWarning($"[ArmorVisual] Bone '{Mdh.Nodes[i].Name}' missing on {RootGo.name} - body not " +
                                      "rebuilt.", LogCat.Mesh);
                    return false;
                }
            }

            foreach (Transform child in RootGo.transform)
            {
                if (child.name.StartsWith("ZM_") && child.GetComponent<SkinnedMeshRenderer>() != null)
                    Object.Destroy(child.gameObject);
            }

            // Attachments of the old armor (e.g. Greg's captain hat) go with it.
            foreach (var oldAttachment in RootGo.GetComponentsInChildren<Transform>(true))
            {
                if (oldAttachment.name == _attachmentGoName)
                    Object.Destroy(oldAttachment.gameObject);
            }

            // Bind poses are calculated from the bones' current pose (CreateBonesData) and written into the cached,
            // shared mesh. An animated NPC would bake its current pose into it - mangled armor for everyone wearing it.
            // The skeleton goes into its rest pose (like during the initial build) for the rebuild, then back.
            var animatedPositions = new UnityEngine.Vector3[nodeObjects.Length];
            var animatedRotations = new Quaternion[nodeObjects.Length];
            for (var i = 0; i < nodeObjects.Length; i++)
            {
                nodeObjects[i].transform.GetLocalPositionAndRotation(out animatedPositions[i], out animatedRotations[i]);

                SetPosAndRot(nodeObjects[i], Mdh.Nodes[i].Transform);
                if (Mdh.Nodes[i].ParentIndex == -1)
                    nodeObjects[i].transform.localPosition = Mdh.RootTranslation.ToUnityVector();
            }

            var meshCounter = CreateSoftSkinMeshes(nodeObjects);
            CreateAttachments(nodeObjects, meshCounter);

            for (var i = 0; i < nodeObjects.Length; i++)
                nodeObjects[i].transform.SetLocalPositionAndRotation(animatedPositions[i], animatedRotations[i]);

            return true;
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
                if (nodeObject.name == "BIP01 L HAND" || nodeObject.name == Constants.Animations.RightHandBoneName)
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
        /// An attachment goes into an own child of its bone: a hat on BIP01 HEAD would otherwise share the bone's
        /// MeshFilter, and RebuildBody can remove the old armor's attachments again.
        /// </summary>
        protected override GameObject GetAttachmentGo(GameObject node)
        {
            var attachmentGo = new GameObject(_attachmentGoName);
            attachmentGo.transform.SetParent(node.transform, false);
            attachmentGo.layer = node.layer;
            return attachmentGo;
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
        /// Gothic stores a pre-baked collision AABB in the MDH file - the hitbox's fallback. The hitbox then follows the
        /// animated body (NpcBodyBounds), measured here while the bones are still in their rest pose.
        /// </summary>
        private void CreateBodyAabbCollider()
        {
            if (Mdh == null)
                return;

            // TODO - For NPC, the CollisionBoundingBox is quite narrow. Think about using Mdh.BoundingBox for VR as it's broader for better hit detection.
            var bounds = Mdh.CollisionBoundingBox.ToUnityBounds();
            var hitbox = RootGo.GetComponentInChildren<NpcHitboxColliderAdapter>();
            if (hitbox != null)
            {
                // The MDH box stays only if the body can't be measured (no bones or skinned mesh).
                hitbox.SetDimension(bounds);
                hitbox.FollowBody(RootGo);
            }
            else if (_isHitboxExpected)
                Logger.LogWarning($"[NpcMeshBuilder] No NpcHitboxColliderAdapter below '{RootGo.name}' - " +
                                  "the NPC has no hitbox.", LogCat.Npc);
        }
    }
}
