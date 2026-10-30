using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Models.Vob;

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
    }
}
