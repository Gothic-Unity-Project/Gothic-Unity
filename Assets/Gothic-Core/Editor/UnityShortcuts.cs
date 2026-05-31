using UnityEditor;

namespace Gothic.Core.Editor
{
    public class UnityShortcuts
    {
        [MenuItem("UnZENity/Unity/Build Profiles", priority = 700)]
        public static void ShowBuildSettingsWindow()
        {
            EditorApplication.ExecuteMenuItem("File/Build Profiles");
        }
        
        [MenuItem("UnZENity/Unity/Build And Run", priority = 710)]
        public static void ShowBuildAndRunWindow()
        {
            EditorApplication.ExecuteMenuItem("File/Build And Run");
        }

        [MenuItem("UnZENity/Unity/Preferences", priority = 800)]
        public static void ShowPreferencesWindow()
        {
            EditorApplication.ExecuteMenuItem("Edit/Preferences...");
        }

        [MenuItem("UnZENity/Unity/Project Settings", priority = 810)]
        public static void ShowProjectSettingsWindow()
        {
            EditorApplication.ExecuteMenuItem("Edit/Project Settings...");
        }

        [MenuItem("UnZENity/Unity/Package Manager", priority = 900)]
        public static void ShowPackageManagerWindow()
        {
            EditorApplication.ExecuteMenuItem("Window/Package Management/Package Manager");
        }
        [MenuItem("UnZENity/Unity/Localization Tables", priority = 910)]
        public static void ShowLocalizationTablesWindow()
        {
            EditorApplication.ExecuteMenuItem("Window/Asset Management/Localization Tables");
        }
    }
}
