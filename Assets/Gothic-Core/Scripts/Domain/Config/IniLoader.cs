using System;
using System.Collections.Generic;
using System.IO;
using GUZ.Core.Logging;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Domain.Config
{
    public static class IniLoader
    {
        public static Dictionary<string, string> LoadFile(string filePath)
        {
            var filePathCaseInsensitive = FindFileCaseInsensitive(filePath);
            if (filePathCaseInsensitive == null)
            {
                Logger.LogError($"The Gothic.ini/GothicGame.ini file does not exist at the specified path: {filePath}", LogCat.Loading);
                return null;
            }

            // StringComparer.OrdinalIgnoreCase -> Lookups will be case insensitive.
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadLines(filePathCaseInsensitive))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith(";"))
                {
                    continue;
                }

                // We don't need to store [section] information. Every property name is unique.
                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    continue;
                }
                else
                {
                    var keyValue = trimmedLine.Split(new[] { '=' }, 2);
                    if (keyValue.Length == 2)
                    {
                        data[keyValue[0].Trim()] = keyValue[1].Trim();
                    }
                }
            }

            return data;
        }

        public static string FindFileCaseInsensitive(string fullPath)
        {
            // Extract directory and filename components
            var directory = Path.GetDirectoryName(fullPath);
            var targetFileName = Path.GetFileName(fullPath);

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Directory not found: {directory}");
            }

            // Case-insensitive search in the target directory
            foreach (var filePath in Directory.EnumerateFiles(directory))
            {
                var currentFileName = Path.GetFileName(filePath);
                if (currentFileName.Equals(targetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return filePath; // Returns actual case-sensitive path
                }
            }

            return null; // File not found
        }
    }
}
