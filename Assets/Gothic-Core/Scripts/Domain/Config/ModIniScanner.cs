using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gothic.Core.Logging;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Config
{
    /// <summary>
    /// DeveloperConfig.EnableGameProfileIniChoice: the game inis of a mod folder - the ones GothicStarter offers.
    /// A mod ships one per mode (Mroczne Tajemnice: DM_E/DM_N/DM_T for easy/normal/hard) next to Gothic.ini,
    /// SystemPack.ini and others; a game ini has [INFO] title= and [FILES] game= or vdf=.
    /// </summary>
    public static class ModIniScanner
    {
        public class ModIni
        {
            public string FileName;
            public string Title;
        }

        // The base game's own ini - a mod folder keeps it, but the mod's modes are its other game inis.
        private const string _baseGameIni = "GothicGame.ini";

        public static List<ModIni> FindGameInis(string modPath)
        {
            var found = new List<ModIni>();
            var systemFolder = FindSystemFolder(modPath);
            if (systemFolder == null)
                return found;

            foreach (var path in Directory.GetFiles(systemFolder, "*.ini").OrderBy(Path.GetFileName))
            {
                if (TryReadGameIni(path, out var title))
                    found.Add(new ModIni { FileName = Path.GetFileName(path), Title = title });
            }

            if (found.Count > 1)
                found.RemoveAll(i => i.FileName.Equals(_baseGameIni, StringComparison.OrdinalIgnoreCase));

            Logger.Log($"[ModIniScanner] {systemFolder}: game inis " +
                       $"[{string.Join(", ", found.Select(i => $"{i.FileName} \"{i.Title}\""))}]", LogCat.Loading);
            return found;
        }

        private static string FindSystemFolder(string modPath)
        {
            if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath))
                return null;
            return Directory.GetDirectories(modPath)
                .FirstOrDefault(d => Path.GetFileName(d).Equals("system", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryReadGameIni(string path, out string title)
        {
            title = null;
            var hasGameFiles = false;
            var section = string.Empty;
            try
            {
                foreach (var rawLine in File.ReadLines(path, GetIniEncoding()))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";"))
                        continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim().ToUpperInvariant();
                        continue;
                    }

                    var keyValue = line.Split(new[] { '=' }, 2);
                    if (keyValue.Length != 2)
                        continue;
                    var key = keyValue[0].Trim().ToLowerInvariant();
                    if (section == "INFO" && key == "title")
                        title = keyValue[1].Trim().Trim('"');
                    else if (section == "FILES" && key is "game" or "vdf")
                        hasGameFiles = true;
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[ModIniScanner] {path} not readable: {e.Message}", LogCat.Loading);
                return false;
            }

            // Gothic.ini, SystemPack.ini and the like have neither.
            return hasGameFiles && !string.IsNullOrEmpty(title);
        }

        /// <summary>
        /// Gothic's inis are Windows-1250 in Polish mods ("Poziom łatwy") - Latin-1 where that code page is missing.
        /// </summary>
        private static Encoding GetIniEncoding()
        {
            try
            {
                return Encoding.GetEncoding(1250);
            }
            catch (Exception)
            {
                return Encoding.GetEncoding("iso-8859-1");
            }
        }
    }
}
