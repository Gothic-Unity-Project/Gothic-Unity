using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Gothic.Core.Domain.Vobs
{
    /// <summary>
    /// DeveloperConfig.EnableVisualFxLights: Gothic's light presets (LIGHTPRESETS.ZEN, an ASCII archive of
    /// zCVobLightPreset) - a VISUALFX lightPresetName (REDAMBIENCE, CATACLYSM, ...) is one of them, like OpenGothic's
    /// LightGroup::findPreset.
    /// </summary>
    public static class LightPresetLoader
    {
        public class LightPreset
        {
            public string Name;
            public Color Color;
            public float RangeMeters;
            public float[] RangeScales = Array.Empty<float>();
            public float RangeFps;
            public bool IsRangeSmooth;
            public Color[] Colors = Array.Empty<Color>();
            public float ColorFps;
            public bool IsColorSmooth;
        }

        public static Dictionary<string, LightPreset> Load(byte[] bytes)
        {
            var presets = new Dictionary<string, LightPreset>(StringComparer.OrdinalIgnoreCase);
            if (bytes == null)
                return presets;

            LightPreset current = null;
            foreach (var rawLine in Encoding.GetEncoding("iso-8859-1").GetString(bytes).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[% zCVobLightPreset", StringComparison.OrdinalIgnoreCase))
                {
                    current = new LightPreset();
                    continue;
                }
                if (current == null)
                    continue;
                if (line == "[]")
                {
                    if (!string.IsNullOrEmpty(current.Name))
                        presets[current.Name] = current;
                    current = null;
                    continue;
                }

                // key=type:value
                var equals = line.IndexOf('=');
                var colon = line.IndexOf(':', Math.Max(0, equals));
                if (equals <= 0 || colon <= equals)
                    continue;
                var key = line.Substring(0, equals);
                var value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "presetName":
                        current.Name = value;
                        break;
                    case "range":
                        current.RangeMeters = ParseFloat(value) / 100f;
                        break;
                    case "color":
                        current.Color = ParseColor(value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                        break;
                    case "rangeAniScale":
                        current.RangeScales = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(ParseFloat).ToArray();
                        break;
                    case "rangeAniFPS":
                        current.RangeFps = ParseFloat(value);
                        break;
                    case "rangeAniSmooth":
                        current.IsRangeSmooth = value == "1";
                        break;
                    case "colorAniList":
                        current.Colors = ParseColorList(value);
                        break;
                    case "colorAniFPS":
                        current.ColorFps = ParseFloat(value);
                        break;
                    case "colorAniSmooth":
                        current.IsColorSmooth = value == "1";
                        break;
                }
            }
            return presets;
        }

        private static float ParseFloat(string value)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : 0f;
        }

        private static Color ParseColor(IReadOnlyList<string> rgb)
        {
            return rgb.Count >= 3
                ? new Color(ParseFloat(rgb[0]) / 255f, ParseFloat(rgb[1]) / 255f, ParseFloat(rgb[2]) / 255f)
                : Color.white;
        }

        /// <summary>
        /// "(255 0 0) (255 128 0) 255" - a bare number is a grey.
        /// </summary>
        private static Color[] ParseColorList(string value)
        {
            var colors = new List<Color>();
            var tokens = value.Replace("(", " ( ").Replace(")", " ) ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == "(")
                {
                    var rgb = new List<string>();
                    for (i++; i < tokens.Length && tokens[i] != ")"; i++)
                        rgb.Add(tokens[i]);
                    colors.Add(ParseColor(rgb));
                    continue;
                }
                var grey = ParseFloat(tokens[i]) / 255f;
                colors.Add(new Color(grey, grey, grey));
            }
            return colors.ToArray();
        }
    }
}
