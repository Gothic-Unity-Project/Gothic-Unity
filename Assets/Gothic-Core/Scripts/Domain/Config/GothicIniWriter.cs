using System;
using System.IO;
using System.Linq;
using Gothic.Core.Logging;
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
    }
}
