using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Owns "an item let go by a hand inside a drop zone goes to that zone's target" (chest content, shoulder stash).
    /// Zones (trigger volumes) only report contacts; the target is asked one frame after the release, so that the
    /// backend has finished the release (and a slot that caught the item itself wins). Nothing runs while no item is
    /// pending.
    /// </summary>
    public class VrDropZoneService
    {
        // Contacts (colliders of an item inside the zone of a target).
        private readonly Dictionary<(IVrDropTarget, VrInteractable), int> _contacts = new();
        private readonly Dictionary<VrInteractable, int> _zonesPerItem = new();
        private readonly List<(IVrDropTarget Target, VrInteractable Item)> _pending = new();
        private readonly List<(IVrDropTarget Target, VrInteractable Item)> _flushBuffer = new();
        private readonly List<(IVrDropTarget, VrInteractable)> _removeBuffer = new();
        private bool _isFlushScheduled;

        /// <summary>
        /// True: first contact of the item with the target (not another collider of the same item).
        /// </summary>
        public bool ItemEntered(IVrDropTarget target, VrInteractable item)
        {
            _contacts.TryGetValue((target, item), out var count);
            _contacts[(target, item)] = count + 1;
            if (count > 0)
                return false;

            _zonesPerItem.TryGetValue(item, out var zones);
            _zonesPerItem[item] = zones + 1;
            if (zones == 0)
                item.Released += OnReleased;

            return true;
        }

        public void ItemExited(IVrDropTarget target, VrInteractable item)
        {
            if (!_contacts.TryGetValue((target, item), out var count))
                return;

            if (count > 1)
            {
                _contacts[(target, item)] = count - 1;
                return;
            }

            _contacts.Remove((target, item));
            Forget(item);
        }

        /// <summary>
        /// The zone of the target is gone or switched off: forget all its contacts and pending drops.
        /// </summary>
        public void Unregister(IVrDropTarget target)
        {
            _removeBuffer.Clear();
            foreach (var key in _contacts.Keys)
            {
                if (key.Item1 == target)
                    _removeBuffer.Add(key);
            }

            foreach (var key in _removeBuffer)
            {
                _contacts.Remove(key);
                if (key.Item2 != null)
                    Forget(key.Item2);
            }

            _removeBuffer.Clear();
            _pending.RemoveAll(entry => entry.Target == target);
        }

        private void Forget(VrInteractable item)
        {
            if (!_zonesPerItem.TryGetValue(item, out var zones))
                return;

            if (zones > 1)
            {
                _zonesPerItem[item] = zones - 1;
                return;
            }

            _zonesPerItem.Remove(item);
            item.Released -= OnReleased;
        }

        private void OnReleased(VrGrabArgs args)
        {
            if (!args.IsHandOrDistance)
                return;

            foreach (var key in _contacts.Keys)
            {
                if (key.Item2 != args.Interactable || !key.Item1.CanTake(args.Interactable))
                    continue;

                var entry = (key.Item1, args.Interactable);
                if (!_pending.Contains(entry))
                    _pending.Add(entry);
            }

            if (_pending.Count > 0 && !_isFlushScheduled)
                FlushNextFrame();
        }

        private async void FlushNextFrame()
        {
            _isFlushScheduled = true;
            try
            {
                await Awaitable.NextFrameAsync();

                _flushBuffer.AddRange(_pending);
                _pending.Clear();

                foreach (var (target, item) in _flushBuffer)
                {
                    // Destroyed, grabbed again (other hand) or already taken (caught by a slot, other zone).
                    if (item == null || item.IsHeld || !item.isActiveAndEnabled)
                        continue;

                    target.TryTake(item);
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"[VrDropZoneService] Drop failed: {e}", LogCat.VR);
            }
            finally
            {
                _flushBuffer.Clear();
                _isFlushScheduled = false;
            }
        }
    }
}
