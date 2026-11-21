using System.Collections.Generic;
using Gothic.Core.Domain.Meshes;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Services.Caches
{
    /// <summary>
    /// Items drawn as small pictures (achievement icons), one per item instance for the whole session. Drawn in
    /// Prewarm, behind a loading screen - TryGetIcon only reads, so a UI line never draws during play.
    /// </summary>
    public class ItemIconService
    {
        private readonly Dictionary<string, Texture2D> _icons = new();
        private ItemIconRenderDomain _renderDomain;

        /// <summary>
        /// Draws every item that has no picture yet. Call while a loading screen hides the frame.
        /// </summary>
        public void Prewarm(IEnumerable<string> itemInstances)
        {
            _renderDomain ??= new ItemIconRenderDomain().Inject();
            var count = 0;
            foreach (var itemInstance in itemInstances)
            {
                if (string.IsNullOrEmpty(itemInstance))
                    continue;

                var key = itemInstance.ToUpperInvariant();
                if (_icons.ContainsKey(key))
                    continue;

                var icon = _renderDomain.Render(itemInstance);
                _icons[key] = icon;
                count++;
                if (icon == null)
                    Logger.LogWarning($"[ItemIcon] {itemInstance} has no mesh to draw.", LogCat.Vob);
            }

            if (count > 0)
                Logger.Log($"[ItemIcon] Drew {count} item icon(s).", LogCat.Vob);
        }

        /// <summary>
        /// The item's picture, or null (not drawn yet, no such item, no mesh).
        /// </summary>
        public Texture2D TryGetIcon(string itemInstance)
        {
            if (string.IsNullOrEmpty(itemInstance))
                return null;

            return _icons.TryGetValue(itemInstance.ToUpperInvariant(), out var icon) ? icon : null;
        }

        public void Dispose()
        {
            foreach (var icon in _icons.Values)
            {
                if (icon != null)
                    Object.Destroy(icon);
            }
            _icons.Clear();
        }
    }
}
