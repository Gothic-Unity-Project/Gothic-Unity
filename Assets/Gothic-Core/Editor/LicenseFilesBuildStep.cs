using System.IO;
using Gothic.Core.Logging;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Editor
{
    /// <summary>
    /// Every build carries LICENSE (GPL v3) and THIRD_PARTY_NOTICES.md - the MIT/LGPL parts (Bink decoder, NVorbis, ...)
    /// require their notices in distributed binaries too. PC builds: next to the executable. Android (Quest/Pico): an APK
    /// can't be changed afterwards, so the files are put into StreamingAssets/Licenses for the build and removed again.
    /// Runs for Editor builds and CI (VRBuilderActions) alike.
    /// </summary>
    public class LicenseFilesBuildStep : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private static readonly string[] _files = { "LICENSE", "THIRD_PARTY_NOTICES.md" };
        private const string _streamingFolderName = "Licenses";

        public int callbackOrder => 0;

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        private static string StreamingFolder => Path.Combine(Application.streamingAssetsPath, _streamingFolderName);

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            Directory.CreateDirectory(StreamingFolder);
            CopyFiles(StreamingFolder);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android)
            {
                RemoveStreamingFolder();
                return;
            }

            var outputPath = report.summary.outputPath;
            var buildFolder = Directory.Exists(outputPath) ? outputPath : Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(buildFolder))
                CopyFiles(buildFolder);
        }

        private static void CopyFiles(string targetFolder)
        {
            foreach (var file in _files)
            {
                var source = Path.Combine(ProjectRoot, file);
                if (!File.Exists(source))
                {
                    Logger.LogWarningEditor($"[Build] '{file}' not found in the project root - not added to the build.",
                        LogCat.Debug);
                    continue;
                }

                File.Copy(source, Path.Combine(targetFolder, file), true);
            }
            Logger.LogEditor($"[Build] License files copied to '{targetFolder}'.", LogCat.Debug);
        }

        /// <summary>
        /// The temporary copies (and the .meta files Unity may have created for them) leave the project again.
        /// </summary>
        private static void RemoveStreamingFolder()
        {
            if (Directory.Exists(StreamingFolder))
                Directory.Delete(StreamingFolder, true);

            var meta = StreamingFolder + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }
    }
}
