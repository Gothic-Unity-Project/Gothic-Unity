using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Config
{
    public class GothicIniWriter
    {
        private string _iniFilePath;
        
        public GothicIniWriter(string iniFilePath)
        {
            _iniFilePath = iniFilePath;
        }
        
        /// <summary>
        /// A failed write is logged, never thrown: the menus keep working and the value stays active for this session.
        /// </summary>
        public void WriteSetting(string section, string key, string value)
        {
            try
            {
                // Linux and Android are case-sensitive (e.g. Wine/Proton writes GOTHIC.INI). Like IniLoader,
                // write into the file that exists, whatever its casing.
                var iniFilePath = IniLoader.FindFileCaseInsensitive(_iniFilePath);
                if (iniFilePath == null)
                {
                    Logger.LogError($"Couldn't write setting [{section}]{key}: {_iniFilePath} doesn't exist.",
                        LogCat.Misc);
                    return;
                }

                TryCreateIniBackup(iniFilePath);
                WriteSetting(iniFilePath, section, key, value, IsGothicUnitySetting(section));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Logger.LogError($"Couldn't write setting [{section}]{key} to {_iniFilePath}: {e.Message}", LogCat.Misc);
            }
        }

        /// <summary>
        /// Check if an Gothic Unity backup file already exists before saving Gothic.ini for the first time.
        /// If not, create backup now.
        /// </summary>
        private void TryCreateIniBackup(string iniFilePath)
        {
            // aka $GOTHICPATH\system\Gothic.ini.Gothic.Unity.bak - any casing counts, the first backup is the original.
            var backupPath = iniFilePath + ".Gothic.Unity.bak";
            if (IniLoader.FindFileCaseInsensitive(backupPath) == null)
                File.Copy(iniFilePath, backupPath);
        }

        private bool IsGothicUnitySetting(string section)
        {
            return section.Contains("VR");
        }
        
        /// <summary>
        /// Setting is only written if:
        /// a. Section + Key is found -> Update
        /// b. !Section + !Key is found -> Create section and key, but only if isCreateSettingAllowed=true
        /// c. Section + !Key is found -> Create key, but only if isCreateSettingAllowed=true
        /// </summary>
        private void WriteSetting(string iniFilePath, string section, string key, string value,
            bool isCreateSettingAllowed)
        {
            var lines = File.ReadAllLines(iniFilePath).ToList();
            if (isCreateSettingAllowed && IsWriteInSectionOn())
            {
                WriteSettingInSection(lines, section, key, value);
                File.WriteAllLines(iniFilePath, lines);
                return;
            }
            var sectionFound = false;
            var keyFound = false;
            
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    // We passed the section already, where our key is located.
                    if (sectionFound)
                        break;

                    sectionFound = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
        
                if (!sectionFound) 
                    continue;
                
                if (!line.Contains("=")) 
                    continue;
                
                var parts = line.Split('=');
                if (parts[0].Trim() == key)
                {
                    lines[i] = $"{key}={value}";
                    keyFound = true;
                    break;
                }
            }

            if (!keyFound && !isCreateSettingAllowed)
            {
                Logger.LogError($"Couldn't write setting [{section}]{key}.", LogCat.Misc);
                return;
            }

            if (!sectionFound)
                lines.Add($"[{section}]");
            
            if (!keyFound)
                lines.Add($"{key}={value}");
        
            
            // Write full file now
            File.WriteAllLines(iniFilePath, lines);
        }

        private static bool IsWriteInSectionOn()
        {
            return ReflexProjectInstaller.DIContainer?.Resolve<ConfigService>()?.Dev?.EnableIniWriteInSection ?? false;
        }

        /// <summary>
        /// DeveloperConfig.EnableIniWriteInSection: a new key went to the end of the file - into the last section
        /// (VR immersion options under [GOTHIC_VR_ACCESSIBILITY]) - and wasn't found there the next time: every change
        /// added another line (marvinPanel 20 times). The loader takes the last value of a key in the whole file.
        /// Now our (VR) key is removed from every other VR section and written at the end of its own section.
        /// </summary>
        private static void WriteSettingInSection(List<string> lines, string section, string key, string value)
        {
            var currentSection = string.Empty;
            var insertAt = -1;
            var isKeyWritten = false;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Substring(1, line.Length - 2);
                    continue;
                }

                var isOwnSection = currentSection.Equals(section, StringComparison.OrdinalIgnoreCase);
                if (isOwnSection && line.Length > 0)
                    insertAt = i + 1;

                var separator = line.IndexOf('=');
                if (separator <= 0 ||
                    !line.Substring(0, separator).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (isOwnSection && !isKeyWritten)
                {
                    lines[i] = $"{key}={value}";
                    isKeyWritten = true;
                }
                else if (isOwnSection || currentSection.Contains("VR"))
                {
                    // A duplicate (own section) or a stray copy in another VR section - it would win when loading.
                    lines.RemoveAt(i);
                    if (insertAt > i)
                        insertAt--;
                    i--;
                }
            }

            if (isKeyWritten)
                return;

            if (insertAt < 0)
            {
                lines.Add($"[{section}]");
                lines.Add($"{key}={value}");
                return;
            }
            lines.Insert(insertAt, $"{key}={value}");
        }
    }
}
