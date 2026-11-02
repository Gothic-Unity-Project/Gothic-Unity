using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableHolsterSaveState: the hip/shoulder holsters (VrSocketSlots under a VrBodyAnchor) keep their
    /// items over saving and loading. A holstered item is a world item, not inventory: it was saved as lying in the
    /// world and floated where the holster had been after a load, the holster empty.
    /// - Placed into a holster, the item leaves the world save; taken out by hand, VRPlayerItemAdapter puts it back.
    /// - The contents are NpcProperties.HolsterItems ("slot|instance|amount", UNITYSAVE.json).
    /// - After loading a save (IsHolsterRestorePending), the items are created again and put into their holsters.
    /// World changes need nothing: the holsters and their items are part of the player.
    /// </summary>
    public class VRHolsterSaveState : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly SaveGameService _saveGameService;

        private const char _separator = '|';

        private readonly Dictionary<string, VrSocketSlot> _holsters = new();
        private bool _isRestoring;

        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            foreach (var slot in transform.root.GetComponentsInChildren<VrSocketSlot>(true))
            {
                var anchor = slot.GetComponentInParent<VrBodyAnchor>(true);
                if (anchor == null)
                    continue;

                var key = $"{anchor.name}/{slot.name}";
                if (!_holsters.TryAdd(key, slot))
                {
                    Logger.LogWarning($"[Holster] Two holsters named '{key}' - the second isn't saved", LogCat.VR);
                    continue;
                }
                slot.Grabbed += OnPlaced;
                slot.Released += OnRemoved;
            }

            Logger.Log($"[Holster] Tracking {_holsters.Count} holster(s): {string.Join(", ", _holsters.Keys)}",
                LogCat.VR);
        }

        private void OnDestroy()
        {
            foreach (var slot in _holsters.Values)
            {
                if (slot == null)
                    continue;
                slot.Grabbed -= OnPlaced;
                slot.Released -= OnRemoved;
            }
        }

        private void OnPlaced(VrGrabArgs args)
        {
            var container = args.Interactable != null
                ? args.Interactable.GetComponentInParent<VobLoader>()?.Container
                : null;
            if (container?.Vob is IItem)
                _saveGameService.RemoveItemFromWorld(container);
            Sync();
        }

        private void OnRemoved(VrGrabArgs args)
        {
            Sync();
        }

        /// <summary>
        /// The holsters' contents into the hero's properties - what the next save writes.
        /// </summary>
        private void Sync()
        {
            var hero = _playerService.HeroContainer;
            if (hero?.Props == null || hero.Props.IsHolsterRestorePending)
                return;

            hero.Props.HolsterItems.Clear();
            foreach (var (key, slot) in _holsters)
            {
                var held = slot != null ? slot.Held : null;
                var item = held != null ? held.GetComponentInParent<VobLoader>()?.Container?.Vob as IItem : null;
                if (item == null)
                    continue;

                var instanceName = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
                hero.Props.HolsterItems.Add($"{key}{_separator}{instanceName}{_separator}{Mathf.Max(1, item.Amount)}");
            }
        }

        /// <summary>
        /// The save's hero data is applied while the world loads - whenever that is, the holsters are filled after it.
        /// </summary>
        private void Update()
        {
            if (_isRestoring || !_configService.Dev.EnableHolsterSaveState)
                return;

            var hero = _playerService.HeroContainer;
            if (hero?.Props == null || !hero.Props.IsHolsterRestorePending || hero.Go == null)
                return;

            _isRestoring = true;
            StartCoroutine(Restore(hero.Props.HolsterItems.ToArray()));
        }

        private IEnumerator Restore(string[] entries)
        {
            // The world's items are created over the first frames - the holstered ones after them.
            yield return new WaitForSeconds(1f);

            // The holsters are part of the player and survive loading - the previous game's items stayed in them.
            var removed = 0;
            foreach (var slot in _holsters.Values)
            {
                var held = slot != null ? slot.Held : null;
                if (held == null || !VrSocketSlot.IsItem(held))
                    continue;

                var root = held.GetComponentInParent<VobLoader>()?.gameObject ?? held.gameObject;
                slot.ForceRelease();
                Destroy(root);
                removed++;
            }
            if (removed > 0)
                yield return null;

            var restored = 0;
            foreach (var entry in entries)
            {
                var parts = entry.Split(_separator);
                if (parts.Length != 3 || !_holsters.TryGetValue(parts[0], out var slot) || slot == null ||
                    !int.TryParse(parts[2], out var amount))
                {
                    Logger.LogWarning($"[Holster] Saved holster item '{entry}' has no holster - skipped", LogCat.VR);
                    continue;
                }
                if (slot.IsOccupied)
                    continue;

                var container = _vobService.CreateItem(new Item
                {
                    Name = parts[1],
                    Visual = new VisualMesh(),
                    Instance = parts[1],
                    Amount = amount
                });
                yield return null;

                var interactable = container?.Go != null
                    ? container.Go.GetComponentInChildren<VrInteractable>(true)
                    : null;
                if (interactable != null && slot.TryPlace(interactable, true))
                    restored++;
                else
                    Logger.LogWarning($"[Holster] '{parts[1]}' couldn't be put into '{parts[0]}'", LogCat.VR);
            }

            var hero = _playerService.HeroContainer;
            if (hero?.Props != null)
                hero.Props.IsHolsterRestorePending = false;
            _isRestoring = false;
            Sync();
            Logger.Log($"[Holster] After loading: {removed} item(s) of the previous game removed, " +
                       $"{restored}/{entries.Length} saved item(s) restored", LogCat.VR);
        }
    }
}
