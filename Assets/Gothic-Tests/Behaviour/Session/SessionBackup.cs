using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Session
{
    /// <summary>
    /// What a run may change on disk - the developer's Gothic.ini (the Gothic.ini steps write it) and the test save
    /// slots 14 and 15 - is copied away before the first step and put back when the run ends. A backup left by a run
    /// that didn't end (editor crash, play mode stopped) is put back first thing at the next start.
    /// </summary>
    public static class SessionBackup
    {
        public static readonly int[] TestSaveSlots = { 14, 15 };

        // Written last: a backup folder without it is incomplete, and its originals were never touched.
        private const string _manifestName = "backup.txt";

        public static string Folder => Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Logs",
            "BehaviourTests", "backup");

        private static string ManifestPath => Path.Combine(Folder, _manifestName);

        /// <summary>
        /// The paths are the game's own (ConfigService.Gothic.IniFilePath, SaveGameService.SavesFolderPath), so the
        /// backup holds exactly what the game reads and writes.
        /// </summary>
        public static void Create(string gothicIniPath, string savesFolderPath)
        {
            if (Directory.Exists(Folder))
                Directory.Delete(Folder, true);
            Directory.CreateDirectory(Folder);

            var entries = new List<string> { Copy(gothicIniPath, "Gothic.ini") };
            entries.AddRange(TestSaveSlots.Select(slot =>
                Copy(Path.Combine(savesFolderPath, $"savegame{slot}"), $"savegame{slot}")));
            File.WriteAllLines(ManifestPath, entries);
            Logger.Log($"[BehaviourTests] Backed up {gothicIniPath} and save slots " +
                $"{string.Join(", ", TestSaveSlots)} of {savesFolderPath} to {Folder}", LogCat.Debug);
        }

        /// <summary>
        /// Puts back what Create copied away and removes the backup. Without a complete backup nothing is touched.
        /// </summary>
        public static void Restore()
        {
            if (!File.Exists(ManifestPath))
            {
                if (Directory.Exists(Folder))
                    Directory.Delete(Folder, true);
                return;
            }

            foreach (var line in File.ReadAllLines(ManifestPath).Where(line => line.Length > 0))
            {
                var parts = line.Split('\t');
                PutBack(Path.Combine(Folder, parts[0]), parts[2], parts[1] == "present");
            }

            Directory.Delete(Folder, true);
            Logger.Log("[BehaviourTests] Gothic.ini and the test save slots are restored", LogCat.Debug);
        }

        private static string Copy(string original, string backupName)
        {
            var backup = Path.Combine(Folder, backupName);
            var isPresent = File.Exists(original) || Directory.Exists(original);
            if (File.Exists(original))
                File.Copy(original, backup);
            else if (Directory.Exists(original))
                CopyDirectory(original, backup);

            return $"{backupName}\t{(isPresent ? "present" : "absent")}\t{original}";
        }

        private static void PutBack(string backup, string original, bool wasPresent)
        {
            if (File.Exists(original))
                File.Delete(original);
            else if (Directory.Exists(original))
                Directory.Delete(original, true);

            if (!wasPresent)
                return;

            if (File.Exists(backup))
                File.Copy(backup, original);
            else if (Directory.Exists(backup))
                CopyDirectory(backup, original);
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
