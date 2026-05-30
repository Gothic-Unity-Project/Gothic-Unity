using System;
using System.Collections.Generic;
using GUZ.Core.Domain.Config;

namespace GUZ.Core.Models.Config
{
    public class GothicIniConfig
    {
        private readonly Dictionary<string, string> _config;
        private readonly GothicIniWriter _gothicIniWriter;
        
        public readonly string IniFilePath;

        // GAME
        public bool IniSubtitles => Convert.ToBoolean(Convert.ToInt16(_config.GetValueOrDefault("subTitles", "1")));
        public bool IniPlayLogoVideos => Convert.ToBoolean(Convert.ToInt16(_config.GetValueOrDefault("playLogoVideos", "1")));

        // GRAPHICS
        public const string IniKeyVisualRange = "sightValue";
        // G1 default: 20 (aka 20m...300m)
        // UnZENity (example): 40...600
        public const int IniVisualRangeFactor = 40;
        public int IniVisualRange => Convert.ToInt32(_config.GetValueOrDefault(IniKeyVisualRange, "4"));
        
        // SOUND
        public bool IniMusicEnabled => Convert.ToBoolean(Convert.ToInt16(_config.GetValueOrDefault("musicEnabled", "1")));
        public float IniMusicVolume => Convert.ToSingle(_config.GetValueOrDefault("musicVolume", "1"));
        
        // SKY_OUTDOOR
        public string IniSkyDayColor(int index) => _config.GetValueOrDefault($"zDayColor{index}", "0 0 0");
        
        
        public GothicIniConfig(Dictionary<string, string> config, string iniFilePath)
        {
            _config = config;
            IniFilePath = iniFilePath;
            _gothicIniWriter = new GothicIniWriter(iniFilePath);
        }

        public string GetString(string settingName, string defaultValue = "")
        {
            if (_config.TryGetValue(settingName, out var value))
                return value;
            else
                return defaultValue;
        }
        
        private void SetString(string section, string key, string value)
        {
            _config[key] = value;

            _gothicIniWriter.WriteSetting(section, key, value);
            GlobalEventDispatcher.PlayerPrefUpdated.Invoke(key, value);
        }
        
        public int GetInt(string settingName, int defaultValue = 0)
        {
            return Convert.ToInt32(GetString(settingName, defaultValue.ToString()));
        }
        
        public void SetInt(string section, string key, int value)
        {
            SetString(section, key, value.ToString());
        }
        
        public float GetFloat(string settingName, float defaultValue = 1f)
        {
            return Convert.ToSingle(GetString(settingName, defaultValue.ToString()));
        }
        
        public void SetFloat(string section, string key, float value)
        {
            SetString(section, key, value.ToString());
        }
    }
}
