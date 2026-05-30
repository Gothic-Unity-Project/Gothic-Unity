using System;
using System.IO;
using GUZ.Core.Models.Config;
using UnityEngine;
using UnityEngine.Networking;
using ZenKit;

namespace GUZ.Core.Domain.Config
{
    public static class JsonRootLoader
    {
        public const string DefaultSteamGothic1Folder = @"C:\Program Files (x86)\Steam\steamapps\common\Gothic\";
        public const string DefaultSteamGothic2Folder = @"C:\Program Files (x86)\Steam\steamapps\common\Gothic II\";

        private const string _settingsFileName = "GameSettings.json";
        private const string _settingsFileNameDev = "GameSettings.dev.json";

        private const string _speechToTextFolder = "SpeechToText";
        private const string _speechToTextHowto = "HOWTO.txt";
        


        public static JsonRootConfig Load()
        {
            PrepareAndroidFolders();

            var rootPath = GetGameSettingsRootPath();
            var settingsFilePath = $"{rootPath}/{_settingsFileName}";

            if (!File.Exists(settingsFilePath))
            {
                throw new ArgumentException($"No >GameSettings.json< file exists at >{settingsFilePath}<.");
            }

            var settingsJson = File.ReadAllText(settingsFilePath);
            var loadedSettings = JsonUtility.FromJson<JsonRootConfig>(settingsJson);

            // Overwrite data with GameSettings.dev.json if it exists.
            var settingsDevFilePath = $"{rootPath}/{_settingsFileNameDev}";
            if (File.Exists(settingsDevFilePath))
            {
                var devJson = File.ReadAllText(settingsDevFilePath);
                JsonUtility.FromJsonOverwrite(devJson, loadedSettings);
            }

            // We need to do a final check for Gothic installation path and which one to ultimately use.
            loadedSettings.Gothic1Path = AlterGothicInstallationPath(loadedSettings.Gothic1Path, GameVersion.Gothic1);
            loadedSettings.Gothic2Path = AlterGothicInstallationPath(loadedSettings.Gothic2Path, GameVersion.Gothic2);

            return loadedSettings;
        }

        /// <summary>
        /// We prepare use of app by copying GameSettings.json and empty Gothic installation directories where gamers
        /// will place their game data into.
        ///
        /// HINT: With Android 10+, there is no easy way to use data from a different folder. i.e. we can create files and folders wherever we want,
        /// but if we upload or alter them from another app (like SideQuest), we loose access (Androids new Scoped Storage/Shared Storage feature).
        /// Therefore, let's stick with the installation folder as it's the official place where other apps (SideQuest etc.) can update/upload our files
        /// and Gothic-UnZENity can still read the data later on.
        /// </summary>
        private static void PrepareAndroidFolders()
        {
            if (Application.platform != RuntimePlatform.Android)
                return;

            // If directory exists and GameSettings.json is placed, we assume everything is created already.
            if (File.Exists($"{Application.persistentDataPath}/{_settingsFileName}"))
                return;

            
            // Create folder(s)
            Directory.CreateDirectory($"{Application.persistentDataPath}/Gothic1");
            Directory.CreateDirectory($"{Application.persistentDataPath}/Gothic2");

            // Copy GameSettings.json into app's shared folder
            {
                var gameSettingsPath = Path.Combine($"{Application.streamingAssetsPath}/{_settingsFileName}");

                var www = UnityWebRequest.Get(gameSettingsPath);
                www.SendWebRequest();

                // Wait until async download is done
                while (!www.isDone)
                { }

                var result = www.downloadHandler.text;
                File.WriteAllText($"{Application.persistentDataPath}/{_settingsFileName}", result);
            }

            // If existing, copy GameSettings.dev.json into writable shared storage folder of our app.
            {
                var gameSettingsPath = Path.Combine($"{Application.streamingAssetsPath}/{_settingsFileName}");

                var gameSettingsDevPath = Path.Combine($"{Application.streamingAssetsPath}/{_settingsFileNameDev}");
                if (File.Exists(gameSettingsDevPath))
                {
                    var devresult = File.ReadAllText(gameSettingsPath);
                    File.WriteAllText($"{Application.persistentDataPath}/{_settingsFileNameDev}", devresult);
                }
            }

            // Copy SpeechToText folder with README information
            {
                var speechToTextFolder = Path.Combine($"{Application.streamingAssetsPath}/{_speechToTextFolder}");
                var howtoFile = Path.Combine($"{speechToTextFolder}/{_speechToTextHowto}");
                var destFolder = Path.Combine($"{Application.persistentDataPath}/{_speechToTextFolder}");
                var destHowtoFile = Path.Combine($"{destFolder}/{_speechToTextHowto}");

                Directory.CreateDirectory(destFolder);
                
                var www = UnityWebRequest.Get(howtoFile);
                www.SendWebRequest();

                // Wait until async download is done
                while (!www.isDone)
                { }

                var result = www.downloadHandler.text;
                File.WriteAllText(destHowtoFile, result);
            }
        }

        /// <summary>
        /// Return path of settings file based on target architecture.
        /// </summary>
        private static string GetGameSettingsRootPath()
        {
            // https://docs.unity3d.com/ScriptReference/Application-persistentDataPath.html
            // Will be: /storage/emulated/<userid>/Android/data/<packagename>/files
            if (Application.platform == RuntimePlatform.Android)
            {
                return Application.persistentDataPath;
            }

            // https://docs.unity3d.com/ScriptReference/Application-streamingAssetsPath.html
            // Will be:
            // 1. Editor: Assets\StreamingAssets\
            // 2. Standalone: Build\Gothic-UnZENity_Data\StreamingAssets\
            return Application.streamingAssetsPath;
        }

        /// <summary>
        /// Check if the specified path inside GameSettings is a valid Gothic installation. If not, use a platform specific fallback:
        /// Standalone: C:\Program Files (x86)\Steam\steamapps\common\Gothic\
        /// Android: /storage/emulated/0/Android/data/com.GothicUnZENity/files/Gothic1/
        /// </summary>
        private static string AlterGothicInstallationPath(string gothicInstallationPath, GameVersion version)
        {
            // GameSettings (or its dev) entry already provides a valid installation directory.
            if (Directory.Exists(gothicInstallationPath))
            {
                return gothicInstallationPath;
            }

            // Try platform specific fallbacks.
            if (Application.platform == RuntimePlatform.Android)
            {
                if (version == GameVersion.Gothic1)
                {
                    return $"{Application.persistentDataPath}/Gothic1";
                }
                else
                {
                    return $"{Application.persistentDataPath}/Gothic2";
                }
            }
            // Standalone
            else
            {
                if (version == GameVersion.Gothic1)
                {
                    return DefaultSteamGothic1Folder;
                }
                else
                {
                    return DefaultSteamGothic2Folder;
                }
            }
        }
    }
}
