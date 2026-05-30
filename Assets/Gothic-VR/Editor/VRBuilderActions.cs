using System;
using System.Collections.Generic;
using Unity.XR.OpenXR.Features.PICOSupport;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace GUZ.VR.Editor
{
    public class VRBuilderActions
    {
        private static readonly string[] _scenes = FindEnabledEditorScenes();

        private const string _appName = "Gothic-UnZENity";
        private const string _targetDir = "Builds";


        [MenuItem("UnZENity/Build/PCVR", priority = 20)]
        private static void PerformWindows64Build()
        {
            var targetPath = _targetDir + "/Windows64/" + _appName + ".exe";

            SetWindows64Settings();
            
            GenericBuild(_scenes, targetPath, BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64, BuildOptions.None);
        }

        [MenuItem("UnZENity/Build/Pico", priority = 21)]
        private static void PerformPicoBuild()
        {
            var targetPath = _targetDir + "/Pico/" + _appName + ".apk";
            SetPicoSettings();
            GenericBuild(_scenes, targetPath, BuildTargetGroup.Android, BuildTarget.Android, BuildOptions.None);
        }

        [MenuItem("UnZENity/Build/Quest", priority = 22)]
        private static void PerformQuestBuild()
        {
            var targetPath = _targetDir + "/Quest/" + _appName + ".apk";
            SetQuestSettings();
            GenericBuild(_scenes, targetPath, BuildTargetGroup.Android, BuildTarget.Android, BuildOptions.None);
        }

        private static void GenericBuild(string[] scenes, string targetPath, BuildTargetGroup buildTargetGroup,
            BuildTarget buildTarget, BuildOptions buildOptions)
        {
            // Set the target platform for the build
            EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);

            // Set BuildPlayerOptions
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = targetPath,
                target = buildTarget,
                targetGroup = buildTargetGroup,
                options = buildOptions
            };

            if (buildTarget == BuildTarget.Android)
            {
                SetKeystoreInformation();
            }

            // Build the project
            BuildPipeline.BuildPlayer(options);
        }

        private static void SetKeystoreInformation()
        {
            var args = Environment.GetCommandLineArgs();

            var keystoreName = GetArg(args, "-keystoreName");
            var keystorePass = GetArg(args, "-keystorePass");
            var keyaliasName = GetArg(args, "-keyaliasName");
            var keyaliasPass = GetArg(args, "-keyaliasPass");

            // If nothing's set, it's a local build. Keep everything as it is.
            if (keystorePass == null && keyaliasName == null && keyaliasPass == null)
                return;
            
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystoreName;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = keyaliasName;
            PlayerSettings.Android.keyaliasPass = keyaliasPass;
        }
        
        private static string GetArg(string[] args, string name)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == name && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static string[] FindEnabledEditorScenes()
        {
            var editorScenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled)
                {
                    continue;
                }

                editorScenes.Add(scene.path);
            }

            return editorScenes.ToArray();
        }

        private static void SetWindows64Settings()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        }
        
        private static void SetPicoSettings()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            //Enable Pico
            OpenXRSettings.ActiveBuildTargetInstance.GetFeature<PICO4ControllerProfile>().enabled = true;
            OpenXRSettings.ActiveBuildTargetInstance.GetFeature<PICOFeature>().enabled = true;

            //Disable Meta
            OpenXRSettings.ActiveBuildTargetInstance.GetFeature<MetaQuestFeature>().enabled = false;
            OpenXRSettings.ActiveBuildTargetInstance.GetFeature<OculusTouchControllerProfile>().enabled = false;

            Debug.Log("OpenXR settings set for: Pico");
        }

        private static void SetQuestSettings()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            
            //Enable Meta
            OpenXRSettings.ActiveBuildTargetInstance.GetFeature<OculusTouchControllerProfile>().enabled = true;
            OpenXRSettings.ActiveBuildTargetInstance.GetFeature<MetaQuestFeature>().enabled = true;

            //Disable Pico
            OpenXRSettings.ActiveBuildTargetInstance.GetFeature<PICOFeature>().enabled = false;
            OpenXRSettings.ActiveBuildTargetInstance.GetFeature<PICO4ControllerProfile>().enabled = false;

            Debug.Log("OpenXR settings set for: Quest");
        }
    }
}
