using System.Linq;
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
using Material = UnityEngine.Material;
using Vector3 = System.Numerics.Vector3;

namespace Gothic.Core.Domain.Meshes.Builder
{
    public class NpcMeshBuilder : AbstractMeshBuilder
    {
        [Inject] private readonly NpcArmorPositionCacheService _npcArmorCacheService;
        [Inject] private readonly Gothic.Core.Services.Config.ConfigService _configService;

        private const string _attachmentGoName = "_Attachment";
        private static readonly HashSet<string> _loggedAttachmentMeshes = new();


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

        /// <summary>
        /// Runtime armor change (AI_EquipArmor, EquipItem, ...): replaces the body's skinned meshes (ZM_*) with the ones
        /// of the current .mdm on the NPC's existing skeleton. Head, weapons, colliders and the animation stay untouched.
        /// </summary>
        public bool RebuildBody()
        {
            var nodeObjects = new GameObject[Mdh.Nodes.Count];
            for (var i = 0; i < Mdh.Nodes.Count; i++)
            {
                nodeObjects[i] = RootGo.FindChildRecursively(Mdh.Nodes[i].Name);
                if (nodeObjects[i] == null)
                {
                    Logger.LogWarning($"[ArmorVisual] Bone '{Mdh.Nodes[i].Name}' missing on {RootGo.name} - body not rebuilt.",
                        LogCat.Mesh);
                    return false;
                }
            }

            foreach (Transform child in RootGo.transform)
            {
                if (child.name.StartsWith("ZM_") && child.GetComponent<SkinnedMeshRenderer>() != null)
                    Object.Destroy(child.gameObject);
            }

            // Attachments of the old armor (e.g. Greg's pirate hat) go with it.
            foreach (var oldAttachment in RootGo.GetComponentsInChildren<Transform>(true))
            {
                if (oldAttachment.name == _attachmentGoName)
                    Object.Destroy(oldAttachment.gameObject);
            }

            // Bind poses are calculated from the bones' current pose (CreateBonesData) and written into the cached, shared
            // mesh. An animated NPC would bake its current pose into it - mangled armor for everyone wearing it.
            // Put the skeleton into its rest pose (like during the initial build) for the rebuild, then restore it.
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

            SetNpcMeshLayers();
            return true;
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

            // Diagnostics for armor hats/helmets (once per mesh): which bones carry attachments, with which textures.
            if (attachments.Count > 0 && _loggedAttachmentMeshes.Add(MeshName ?? ""))
            {
                foreach (var attachment in attachments)
                {
                    var textures = string.Join(",", attachment.Value.SubMeshes.Select(s => s.Material?.Texture));
                    Logger.Log($"[ArmorVisual] '{MeshName}' attachment on '{attachment.Key}': {textures}", LogCat.Mesh);
                }
            }

            // Only strip BIP01 HEAD when a separate head MMB file replaces it (human NPCs).
            // Monsters like Skeleton bake the skull directly into the MDM attachment — removing it
            // leaves them headless because NpcHeadMeshBuilder has nothing to load (Head="").
            // Armors carry a placeholder head there too (e.g. Greg's KOPFTUCH) - the engine replaces it with the NPC's head.
            // Hats are part of the armor's soft skin mesh (drawn via CreateUntexturedMaterial when color-only).
            if (!string.IsNullOrEmpty(BodyData.Head) && newAttachments.Remove("BIP01 HEAD"))
            {
                Logger.Log("Removed default >BIP01 HEAD< attachment mesh from NPC.", LogCat.Mesh);
            }

            return newAttachments;
        }

        // One 1x1 texture per color - Lit/SingleMesh has no color property.
        private static readonly Dictionary<Color32, Texture2D> _colorTextures = new();

        /// <summary>
        /// DeveloperConfig.EnableRuntimeArmorVisuals: armor parts with a color-only (untextured) material, e.g. Greg's black
        /// captain hat (material KOPFTUCH), are drawn in their color like in the engine. Skipping them hid the part and
        /// shifted all following materials by one sub mesh.
        /// </summary>
        protected override Material CreateUntexturedMaterial(IMaterial materialData)
        {
            if (!_configService.Dev.EnableRuntimeArmorVisuals)
                return null;

            var color = new Color32(materialData.Color.R, materialData.Color.G, materialData.Color.B, 255);
            if (!_colorTextures.TryGetValue(color, out var texture) || texture == null)
            {
                texture = new Texture2D(1, 1);
                texture.SetPixel(0, 0, color);
                texture.Apply();
                _colorTextures[color] = texture;
            }

            Logger.Log($"[ArmorVisual] Color-only material '{materialData.Name}' of '{MeshName}' -> {color}", LogCat.Mesh);
            return new Material(Constants.ShaderSingleMeshLit) { mainTexture = texture };
        }

        /// <summary>
        /// DeveloperConfig.EnableRuntimeArmorVisuals: attachments go into an own child of the bone - a hat on BIP01 HEAD
        /// would otherwise share the MeshFilter of the head mesh - and can be removed again on an armor change.
        /// </summary>
        protected override GameObject GetAttachmentGo(GameObject node)
        {
            if (!_configService.Dev.EnableRuntimeArmorVisuals)
                return node;

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
            // Visual-only bodies (VR hero body) aren't built from the NPC prefab - no hitbox there.
            RootGo.GetComponentInChildren<NpcHitboxColliderAdapter>()?.SetDimension(bounds);
        }
    }
}
