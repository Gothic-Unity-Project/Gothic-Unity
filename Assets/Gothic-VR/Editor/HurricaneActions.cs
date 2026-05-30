using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;

namespace GUZ.VR.Editor
{
    public class HurricaneActions
    {
        private static string[] _hvrMaterialFolders = { "Assets/HurricaneVR/Framework/Materials", "Assets/HurricaneVR/TechDemo/Materials" };
        
        /// <summary>
        /// Leveraging Unity's implementation of MaterialConversion.
        /// @see documentation: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/features/rp-converter.html
        /// @see source code (for more MaterialUpgraders if needed): UnityEditor.Rendering.Universal.UniversalRenderPipelineMaterialUpgrader:GetUpgraders()
        /// </summary>
        [MenuItem("UnZENity/Build/HVR - Convert Materials to URP", priority = 100)]
        private static void ConvertHVRMaterialToURP()
        {
            var upgrader = new StandardUpgrader("Standard");
            var materialFiles = new List<string>();
            
            foreach (var folder in _hvrMaterialFolders)
            {
                materialFiles.AddRange(Directory.GetFiles(folder, "*.mat"));
            }

            foreach (var materialFile in materialFiles)
            {
                var materialToConvert = AssetDatabase.LoadAssetAtPath<Material>(materialFile);
                upgrader.Upgrade(materialToConvert, MaterialUpgrader.UpgradeFlags.LogMessageWhenNoUpgraderFound);
                Debug.Log($"Material >{materialToConvert.name}< converted to URP");
            }

            Debug.Log("### All HVR materials are converted successfully. ###");
        }
    }
}
