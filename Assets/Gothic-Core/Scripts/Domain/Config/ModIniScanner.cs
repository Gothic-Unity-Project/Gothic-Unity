using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gothic.Core.Logging;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Config
{
    /// <summary>
    /// The game inis of a Gothic installation's system/ folder - the ones GothicStarter offers.
    /// The game's own GothicGame.ini plus one per installed mod or mode (Mroczne Tajemnice: DM_E/DM_N/DM_T for
    /// easy/normal/hard) next to Gothic.ini, SystemPack.ini and others; a game ini has [INFO] title= and
    /// [FILES] game= or vdf=.
    /// </summary>
    public static class ModIniScanner
    {
        public class ModIni
        {
            public string FileName;
            public string Title;
        }

        public static List<ModIni> FindGameInis(string rootPath)
        {
            var found = new List<ModIni>();
            var systemFolder = FindSystemFolder(rootPath);
            if (systemFolder == null)
                return found;

            foreach (var path in Directory.GetFiles(systemFolder, "*.ini").OrderBy(Path.GetFileName))
            {
                if (TryReadGameIni(path, out var title))
                    found.Add(new ModIni { FileName = Path.GetFileName(path), Title = title });
            }

            Logger.Log($"[ModIniScanner] {systemFolder}: game inis " +
                       $"[{string.Join(", ", found.Select(i => $"{i.FileName} \"{i.Title}\""))}]", LogCat.Loading);
            return found;
        }

        private static string FindSystemFolder(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                return null;
            return Directory.GetDirectories(rootPath)
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
        /// Same code pages as BootstrapDomain.SetLanguage(). The scan runs before the VM detects the language,
        /// so we take Central Europe: Polish mods ("Poziom łatwy") need it and ASCII titles read the same.
        /// </summary>
        private static Encoding GetIniEncoding()
        {
            return Encoding.GetEncoding((int)StringEncoding.CentralEurope);
        }
    }
}
