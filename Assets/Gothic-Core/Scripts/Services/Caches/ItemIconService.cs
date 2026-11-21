using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Gothic.Core.Domain.Meshes;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Services.Caches
{
    /// <summary>
    /// Items drawn as small pictures (achievement icons), one per item instance for the whole session. Drawn on
    /// request (the achievements page opens, a toast shows), at most one per frame, never at world load. Each
    /// finished picture raises GlobalEventDispatcher.ItemIconReady; TryGetIcon only reads.
    /// </summary>
    public class ItemIconService
    {
        [Inject] private readonly UnityMonoService _unityMonoService;

        // null value: drawn, but the item has no mesh to draw.
        private readonly Dictionary<string, RenderTexture> _icons = new();
        private readonly Queue<string> _requests = new();
        private readonly HashSet<string> _requested = new();
        private ItemIconRenderDomain _renderDomain;
        private bool _isDrawing;


        /// <summary>
        /// Draws the item in a later frame if it has no picture yet. ItemIconReady tells when it's there.
        /// </summary>
        public void Request(string itemInstance)
        {
            if (string.IsNullOrEmpty(itemInstance))
                return;

            var key = ToKey(itemInstance);
            if (_icons.ContainsKey(key) || !_requested.Add(key))
                return;

            _requests.Enqueue(itemInstance);
            if (_isDrawing)
                return;

            _isDrawing = true;
            _unityMonoService.StartCoroutine(DrawRequests());
        }

        /// <summary>
        /// The item's picture, or null (not drawn yet, no such item, no mesh).
        /// </summary>
        public Texture TryGetIcon(string itemInstance)
        {
            if (string.IsNullOrEmpty(itemInstance))
                return null;

            var key = ToKey(itemInstance);
            if (!_icons.TryGetValue(key, out var icon) || icon == null)
                return null;

            // A GPU texture can lose its content (graphics device reset): drawn again on the next request.
            if (icon.IsCreated())
                return icon;

            Object.Destroy(icon);
            _icons.Remove(key);
            return null;
        }

        /// <summary>
        /// One icon per frame. One summary line per batch: count and CPU time (mesh build + render submit).
        /// </summary>
        private IEnumerator DrawRequests()
        {
            _renderDomain ??= new ItemIconRenderDomain().Inject();
            var count = 0;
            var totalMs = 0.0;
            var slowestMs = 0.0;
            var slowestItem = string.Empty;
            var stopwatch = new Stopwatch();

            while (_requests.Count > 0)
            {
                yield return null;

                var itemInstance = _requests.Dequeue();
                var key = ToKey(itemInstance);
                _requested.Remove(key);

                stopwatch.Restart();
                RenderTexture icon;
                try
                {
                    icon = _renderDomain.Render(itemInstance);
                }
                catch (System.Exception e)
                {
                    Logger.LogWarning($"[ItemIcon] Drawing {itemInstance} failed: {e.Message}", LogCat.Vob);
                    icon = null;
                }
                stopwatch.Stop();

                _icons[key] = icon;
                count++;
                var ms = stopwatch.Elapsed.TotalMilliseconds;
                totalMs += ms;
                if (ms > slowestMs)
                {
                    slowestMs = ms;
                    slowestItem = itemInstance;
                }

                if (icon == null)
                    Logger.LogWarning($"[ItemIcon] {itemInstance} has no mesh to draw.", LogCat.Vob);

                GlobalEventDispatcher.ItemIconReady.Invoke(itemInstance);
            }

            _isDrawing = false;
            if (count == 0)
                yield break;

            Logger.Log($"[ItemIcon] Drew {count} item icon(s), one per frame: {totalMs:0.0} ms in total, " +
                       $"{totalMs / count:0.0} ms average, slowest {slowestItem} {slowestMs:0.0} ms.", LogCat.Vob);
        }

        private static string ToKey(string itemInstance) => itemInstance.ToUpperInvariant();

        public void Dispose()
        {
            foreach (var icon in _icons.Values)
            {
                if (icon != null)
                    Object.Destroy(icon);
            }
            _icons.Clear();
            _requests.Clear();
            _requested.Clear();
        }
    }
}
