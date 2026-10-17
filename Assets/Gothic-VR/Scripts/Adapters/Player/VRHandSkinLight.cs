#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.HandLightInfluence: the VR hands (material VRHands) keep their skin color - the world's lights and
    /// the sky colored ambient (blue night, red evening, cave auras, the orange torch) color them only this much. Fully lit
    /// they turned navy, light blue or red. The rest comes from emission with the skin texture, so they never go dark.
    /// </summary>
    public static class VRHandSkinLight
    {
        private const string _handMaterialName = "VRHands";
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int _emissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int _emissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int _smoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int _specularHighlightsId = Shader.PropertyToID("_SpecularHighlights");
        private static readonly int _environmentReflectionsId = Shader.PropertyToID("_EnvironmentReflections");

        public static void Apply(ConfigService configService)
        {
            var influence = Mathf.Clamp01(configService.Dev.HandLightInfluence);
            if (influence >= 1f)
                return;

            var count = 0;
            foreach (var handRenderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                var shared = handRenderer.sharedMaterials;
                var isHand = false;
                foreach (var material in shared)
                    isHand |= material != null && material.name.StartsWith(_handMaterialName);
                if (!isHand)
                    continue;

                // Instances: the hand material is a shared asset.
                foreach (var material in handRenderer.materials)
                {
                    if (!material.name.StartsWith(_handMaterialName) || !material.HasProperty(_emissionColorId) ||
                        material.IsKeywordEnabled("_EMISSION"))
                        continue;

                    // DeveloperConfig.HandSkinTint: like the hero's arm, not the bright pink of the hand texture.
                    var skin = configService.Dev.HandSkinTint;
                    material.SetColor(_baseColorId, skin * influence);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    material.SetTexture(_emissionMapId, material.GetTexture(_baseMapId));
                    material.SetColor(_emissionColorId, skin * (1f - influence));
                    // Matte skin, not wax: no glossy highlight, no environment reflection.
                    material.SetFloat(_smoothnessId, 0f);
                    material.SetFloat(_specularHighlightsId, 0f);
                    material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    material.SetFloat(_environmentReflectionsId, 0f);
                    material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    count++;
                }
            }
            Logger.Log($"[VRHandSkinLight] {count} hand material(s) ({_handMaterialName}): lights color them " +
                       $"{influence:P0}", LogCat.VR);
        }
    }
}
#endif
