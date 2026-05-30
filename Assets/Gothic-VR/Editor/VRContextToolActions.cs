using System.IO;
using System.Linq;
using GUZ.Core.Adapters;
using GUZ.Core.Models.Context;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GUZ.VR.Editor
{
    public class VRContextToolActions
    {
        private const string HVR_COMPILER_FLAG = "GUZ_HVR_INSTALLED";

        [MenuItem("UnZENity/Build/Context/Check HVR status", priority = 1)]
        private static void CheckHVRPluginStatus()
        {
            var hvrFolder = Application.dataPath + "/HurricaneVR";
            var hvrExists = Directory.Exists(hvrFolder) && Directory.EnumerateFiles(hvrFolder).Count() != 0;
            var hvrCompilerSettingStandaloneExists = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone)
                .Contains(HVR_COMPILER_FLAG);
            var hvrCompilerSettingAndroidExists = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android)
                .Contains(HVR_COMPILER_FLAG);
            var hvrSceneSetting = Object.FindObjectOfType<BootstrapAdapter>()?.DeveloperConfig.GameControls == Controls.VR;

            var message =
                $"{hvrExists} - Plugin installed\n" +
                $"{hvrCompilerSettingStandaloneExists}/{hvrCompilerSettingAndroidExists} - Included in Build Standalone/Android\n" +
                $"{hvrSceneSetting} - Included in Scene";

            EditorUtility.DisplayDialog("Hurricane VR - Status", message, "Close");
        }

        /// <summary>
        /// Activate compiler flag being used inside GvrContext classes. This decides whether to build or don't build HVR classes.
        /// </summary>
        [MenuItem("UnZENity/Build/Context/Activate HVR in Build", priority = 2)]
        private static void ActivatePlugin()
        {
            ActivatePlugin(NamedBuildTarget.Standalone);
            ActivatePlugin(NamedBuildTarget.Android);
        }

        private static void ActivatePlugin(NamedBuildTarget target)
        {
            // Change compile flag in PlayerSettings
            var settings = PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(";")
                .ToList();

            if (settings.Any(i => i.Equals(HVR_COMPILER_FLAG)))
            {
                return;
            }

            settings.Add(HVR_COMPILER_FLAG);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", settings));
        }

        /// <summary>
        /// Deactivate compiler flag being used inside GvrContext classes. This decides whether to build or don't build HVR classes.
        /// </summary>
        [MenuItem("UnZENity/Build/Context/De-activate HVR", priority = 3)]
        private static void DeactivatePlugin()
        {
            DeactivatePlugin(NamedBuildTarget.Standalone);
            DeactivatePlugin(NamedBuildTarget.Android);
        }

        private static void DeactivatePlugin(NamedBuildTarget target)
        {
            var settings = PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(";")
                .ToList();

            if (!settings.Any(i => i.Equals(HVR_COMPILER_FLAG)))
            {
                return;
            }

            settings.Remove(HVR_COMPILER_FLAG);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", settings));
        }
    }
}
