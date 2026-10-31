using System.IO;
using System.Linq;

namespace Gothic.Core.Extensions
{
    /// <summary>
    /// Paths inside a Gothic installation. Windows ignores their case, Linux doesn't: G1 has
    /// _work/DATA/scripts/content/CUTSCENE/OU.BIN, G2 _work/Data/Scripts/CONTENT/CUTSCENE/ou.bin.
    /// </summary>
    public static class PathExtension
    {
        /// <summary>
        /// The existing file or folder below root whose path matches the parts (folders separated by '/') in any case.
        /// null if there is none.
        /// </summary>
        public static string FindIgnoringCase(string root, params string[] parts)
        {
            var current = root;
            foreach (var part in parts.SelectMany(i => i.Split('/', '\\')).Where(i => i.Length > 0))
            {
                if (!Directory.Exists(current))
                    return null;

                var exact = Path.Combine(current, part);
                if (File.Exists(exact) || Directory.Exists(exact))
                {
                    current = exact;
                    continue;
                }

                current = Directory.EnumerateFileSystemEntries(current)
                    .FirstOrDefault(entry => Path.GetFileName(entry).EqualsIgnoreCase(part));
                if (current == null)
                    return null;
            }

            return current;
        }
    }
}
