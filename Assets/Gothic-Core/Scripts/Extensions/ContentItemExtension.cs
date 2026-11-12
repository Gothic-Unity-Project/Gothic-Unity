using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vob;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Extensions
{
    /// <summary>
    /// Item lists as Gothic stores them as text: oCMobContainer.contents, oCNpc packed inventory, Marvin/config values.
    /// E.g. "ITFO_APPLE:5;ITMW_1H_SWORD_01" (an item without amount counts once).
    /// </summary>
    public static class ContentItemExtension
    {
        private static readonly char[] _itemNameSeparators = { ';', ',' };
        private static readonly char[] _itemCountSeparators = { ':', '.' };


        public static List<ContentItem> UnpackItems(this string contents)
        {
            List<ContentItem> result = new();

            if (string.IsNullOrEmpty(contents))
                return result;

            var items = contents.Split(_itemNameSeparators);

            foreach (var item in items)
            {
                var count = 1;
                var nameCountSplit = item.Split(_itemCountSeparators);

                if (nameCountSplit.Length != 1)
                    count = int.Parse(nameCountSplit[1]);

                result.Add(new ContentItem(nameCountSplit[0], count));
            }

            return result;
        }

        public static string PackItems(this IEnumerable<ContentItem> items, char separator = ';')
        {
            return string.Join(separator, items.Select(i => $"{i.Name}:{i.Amount}"));
        }

        /// <summary>
        /// Reads oCMobContainer.contents. A mod's broken text (ITFO_X:abc, ITFO_X:99999999999) gives an empty list
        /// plus a warning instead of an exception.
        /// </summary>
        public static List<ContentItem> UnpackContainerContents(this IContainer container)
        {
            try
            {
                return (container?.Contents).UnpackItems()
                    .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                    .Select(i => new ContentItem(i.Name.Trim(), i.Amount))
                    .ToList();
            }
            catch (Exception e) when (e is FormatException or OverflowException)
            {
                Logger.LogWarning($"[ContentItemExtension] {container.Name}: contents '{container.Contents}' can't " +
                                  $"be read ({e.Message}) - treated as empty.", LogCat.Vob);
                return new List<ContentItem>();
            }
        }

        /// <summary>
        /// Writes oCMobContainer.contents, the one format for every writer (chest slots, loot).
        /// </summary>
        public static void PackContainerContents(this IContainer container, IEnumerable<ContentItem> items)
        {
            container.Contents = items.PackItems(',');
        }
    }
}
