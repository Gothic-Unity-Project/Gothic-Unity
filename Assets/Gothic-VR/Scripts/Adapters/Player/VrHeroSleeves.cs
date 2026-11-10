using System.Collections.Generic;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Rolled-up sleeves of the VR hero body (backpack in a hand): the armor meshes without forearms and the bare
    /// forearms of the hero's naked body mesh, skinned to the armor's skeleton - the backpack's slots aren't hidden.
    /// Also swaps the meshes with and without the model's hands (VrHeroHands). Worker of VRHeroBody (new().Inject(),
    /// Clear + Build per built body).
    /// </summary>
    public class VrHeroSleeves
    {
        [Inject] private readonly MeshService _meshService;

        private readonly VrHeroBodyMeshCutter _meshCutter;
        // VRHeroBody's meshes with and without the model's hands (DeveloperConfig.EnableHeroHands).
        private readonly List<(SkinnedMeshRenderer renderer, Mesh withHands, Mesh withoutHands)> _handMeshes;

        // Armor meshes without forearms + bare forearms (naked body mesh) on the armor skeleton.
        private readonly List<(SkinnedMeshRenderer renderer, Mesh full, Mesh sleeveless)> _sleeveMeshes = new();
        private readonly List<SkinnedMeshRenderer> _bareForearms = new();
        // The bare forearms' meshes without and with the naked hands (DeveloperConfig.EnableHeroHands).
        private readonly Dictionary<SkinnedMeshRenderer, (Mesh forearms, Mesh withHands)> _bareForearmMeshes = new();
        private bool _areRolledUp;

        /// <summary>
        /// The bare forearms of the built body (disabled while the sleeves are down).
        /// </summary>
        public IReadOnlyList<SkinnedMeshRenderer> BareForearms => _bareForearms;

        public VrHeroSleeves(VrHeroBodyMeshCutter meshCutter,
            List<(SkinnedMeshRenderer renderer, Mesh withHands, Mesh withoutHands)> handMeshes)
        {
            _meshCutter = meshCutter;
            _handMeshes = handMeshes;
        }

        /// <summary>
        /// The old body is gone (its meshes are destroyed by the mesh cutter's owner).
        /// </summary>
        public void Clear()
        {
            _sleeveMeshes.Clear();
            _bareForearms.Clear();
            _bareForearmMeshes.Clear();
            _areRolledUp = false;
        }

        /// <summary>
        /// Rolls the sleeves up or down (only with bare forearms) and applies the arm meshes if that changed.
        /// </summary>
        public void SetRolledUp(bool isRolledUp, bool areHeroHandsShown)
        {
            isRolledUp &= _bareForearms.Count > 0;
            if (isRolledUp == _areRolledUp)
                return;

            _areRolledUp = isRolledUp;
            ApplyArmMeshes(areHeroHandsShown);
        }

        /// <summary>
        /// The armor's and the bare forearms' meshes for both states: rolled-up sleeves (backpack in a hand) and the
        /// hero's own hands (DeveloperConfig.EnableHeroHands). With both the naked hands come with the bare forearms -
        /// the sleeveless armor has none, the fists vanished while the backpack was held.
        /// </summary>
        public void ApplyArmMeshes(bool areHeroHandsShown)
        {
            foreach (var (renderer, withHands, withoutHands) in _handMeshes)
            {
                if (renderer != null)
                    renderer.sharedMesh = areHeroHandsShown ? withHands : withoutHands;
            }
            foreach (var (renderer, full, sleeveless) in _sleeveMeshes)
            {
                if (renderer == null)
                    continue;
                if (_areRolledUp)
                    renderer.sharedMesh = sleeveless;
                else if (!_handMeshes.Exists(i => i.renderer == renderer))
                    renderer.sharedMesh = full;
            }
            foreach (var forearm in _bareForearms)
            {
                if (forearm == null)
                    continue;
                forearm.enabled = _areRolledUp;
                if (_bareForearmMeshes.TryGetValue(forearm, out var meshes))
                    forearm.sharedMesh = areHeroHandsShown && meshes.withHands != null
                        ? meshes.withHands
                        : meshes.forearms;
            }
        }

        /// <summary>
        /// Sleeveless copies of the armor meshes and the forearms of the naked body (Mdl_SetVisualBody), skinned to the
        /// armor's skeleton - they stretch and roll with the IK like the armor did.
        /// </summary>
        public void Build(GameObject body, NpcProperties props, string mdhName,
            Dictionary<string, Transform> armorBones)
        {
            if (props.MdmName.EqualsIgnoreCase(props.BodyData.Body))
                return; // no armor - nothing to roll up

            var nakedRoot = new GameObject("_VRHeroBodyNaked");
            GameObject naked;
            try
            {
                naked = _meshService.CreateNpc(nakedRoot.name, props.BodyData.Body, mdhName, props.BodyData,
                    root: nakedRoot) ?? nakedRoot;
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"[VRHeroBody] Can't build the naked body ({props.BodyData.Body}): {e.Message}",
                    LogCat.VR);
                Object.Destroy(nakedRoot);
                return;
            }

            foreach (var nakedRenderer in naked.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var forearmMesh = _meshCutter.CutArms(nakedRenderer.sharedMesh, nakedRenderer.bones,
                    VrHeroArmCut.Forearms);
                if (forearmMesh == null)
                    continue;

                // Same place relative to the root as in the naked body (the bind poses are relative to it).
                var relative = naked.transform.worldToLocalMatrix * nakedRenderer.transform.localToWorldMatrix;
                var forearmGo = new GameObject("_BareForearms");
                forearmGo.transform.SetParent(body.transform, false);
                forearmGo.transform.SetLocalPositionAndRotation(relative.GetPosition(), relative.rotation);
                forearmGo.transform.localScale = relative.lossyScale;

                var bones = new Transform[nakedRenderer.bones.Length];
                for (var i = 0; i < bones.Length; i++)
                {
                    var nakedBone = nakedRenderer.bones[i];
                    if (nakedBone != null)
                        armorBones.TryGetValue(nakedBone.name.ToUpperInvariant(), out bones[i]);
                }

                var forearm = forearmGo.AddComponent<SkinnedMeshRenderer>();
                forearm.sharedMesh = forearmMesh;
                forearm.sharedMaterials = nakedRenderer.sharedMaterials;
                forearm.bones = bones;
                if (nakedRenderer.rootBone != null &&
                    armorBones.TryGetValue(nakedRenderer.rootBone.name.ToUpperInvariant(), out var rootBone))
                    forearm.rootBone = rootBone;
                forearm.updateWhenOffscreen = true;
                forearm.shadowCastingMode = ShadowCastingMode.Off;
                forearm.enabled = false;
                _bareForearms.Add(forearm);
                _bareForearmMeshes[forearm] = (forearmMesh,
                    _meshCutter.CutArms(nakedRenderer.sharedMesh, nakedRenderer.bones, VrHeroArmCut.ForearmsAndHands));
            }
            Object.Destroy(nakedRoot);

            if (_bareForearms.Count == 0)
                return;

            foreach (var armorRenderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (_bareForearms.Contains(armorRenderer))
                    continue;
                var sleeveless = _meshCutter.CutArms(armorRenderer.sharedMesh, armorRenderer.bones,
                    VrHeroArmCut.Sleeveless);
                if (sleeveless != null)
                    _sleeveMeshes.Add((armorRenderer, armorRenderer.sharedMesh, sleeveless));
            }
        }
    }
}
