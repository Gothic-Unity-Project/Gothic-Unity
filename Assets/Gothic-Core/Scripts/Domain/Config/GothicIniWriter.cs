using System;
using System.IO;
using System.Linq;
using GUZ.Core.Logging;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Domain.Config
{
    public class GothicIniWriter
    {
        private string _iniFilePath;
        
        public GothicIniWriter(string iniFilePath)
        {
            _iniFilePath = iniFilePath;
        }
        
        public void WriteSetting(string section, string key, string value)
        {
            TryCreateIniBackup();

            if (IsUnZENitySetting(section))
                WriteSetting(section, key, value, true);
            else
                WriteSetting(section, key, value, false);
        }

        /// <summary>
        /// Check if an UnZENity backup file already exists before saving Gothic.ini for the first time.
        /// If not, create backup now.
        /// </summary>
        private void TryCreateIniBackup()
        {
            // aka $GOTHICPATH\system\Gothic.ini.UnZENity.bak
            var backupPath = Path.Combine(Path.GetDirectoryName(_iniFilePath)!, _iniFilePath + ".UnZENity.bak");
            if (!File.Exists(backupPath))
                File.Copy(_iniFilePath!, backupPath);
        }

        private bool IsUnZENitySetting(string section)
        {
            return section.Contains("UNZENITY");
        }
        
        /// <summary>
        /// Setting is only written if:
        /// a. Section + Key is found -> Update
        /// b. !Section + !Key is found -> Create section and key, but only if isCreateSettingAllowed=true
        /// c. Section + !Key is found -> Create key, but only if isCreateSettingAllowed=true
        /// </summary>
        private void WriteSetting(string section, string key, string value, bool isCreateSettingAllowed)
        {
            var lines = File.ReadAllLines(_iniFilePath).ToList();
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
            File.WriteAllLines(_iniFilePath, lines);
        }
    }
}
