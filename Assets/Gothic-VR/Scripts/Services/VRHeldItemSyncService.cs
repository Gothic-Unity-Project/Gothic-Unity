using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// V1: items in VR hands and body holsters count as hero inventory. Keeps those physical copies in sync when a
    /// script takes items from the hero.
    /// </summary>
    public class VRHeldItemSyncService
    {
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VRCarriedItemsSaveService _carriedItemsSaveService;

        public void Init()
        {
            GlobalEventDispatcher.ScriptRemovedInvItems.AddListener(OnScriptRemovedInvItems);
        }

        /// <summary>
        /// When a script takes items (B_GiveInvItems in a dialog, Npc_RemoveInvItems), the physical copies
        /// must vanish too - otherwise the hero keeps e.g. Lobart's clothes in a holster after handing them over.
        /// Only surplus copies (more physical than the inventory still has) are removed. Backpack sockets are skipped:
        /// the backpack refills from the inventory anyway.
        /// </summary>
        private void OnScriptRemovedInvItems(NpcContainer npc, int itemIndex, int amount)
        {
            var hero = _playerService.HeroContainer;
            if (npc == null || hero == null || npc != hero)
                return;

            var itemName = _gameStateService.GothicVm.GetSymbolByIndex(itemIndex)?.Name;
            if (itemName == null)
                return;

            var remaining = _npcInventoryService.ExtNpcHasItems(hero.Instance, itemIndex);

            var socketed = new List<(IVrSocket socket, VobContainer container)>();
            var holsters = _carriedItemsSaveService.Holsters;
            for (var i = 0; i < holsters.Count; i++)
            {
                var socket = holsters[i];
                if (socket.Held == null)
                    continue;

                var container = socket.Held.GetComponentInParent<VobLoader>(true)?.Container;
                if (IsSameItem(container, itemName))
                    socketed.Add((socket, container));
            }

            var held = new List<(IVrGrabber hand, VobContainer container)>();
            foreach (var hand in _vrPlayerService.GetHandGrabbers())
            {
                var container = hand.Held?.GetComponentInParent<VobLoader>()?.Container;
                if (IsSameItem(container, itemName) && !held.Exists(h => h.container == container))
                    held.Add((hand, container));
            }

            var surplus = socketed.Sum(s => GetAmount(s.container)) + held.Sum(h => GetAmount(h.container)) - remaining;
            if (surplus <= 0)
                return;

            Logger.Log($"[VRHeldItemSyncService] Script took {amount}x '{itemName}' - removing {surplus} physical " +
                       $"cop(y/ies) from holsters/hands (inventory left: {remaining}).", LogCat.VR);

            // Holsters first - a script taking an item rarely means the one the player is actively holding.
            foreach (var (socket, container) in socketed)
            {
                if (surplus <= 0)
                    return;
                surplus -= GetAmount(container);
                socket.ForceRelease();
                _vobService.RemoveWorldItem(container);
            }

            foreach (var (_, container) in held)
            {
                if (surplus <= 0)
                    return;
                surplus -= GetAmount(container);

                // Releasing from the hand subtracts the item from the inventory (VRPlayerService.UnsetGrab), but the
                // script already did that. Add it back first so the release doesn't remove it twice.
                _playerService.AddItem(itemName, GetAmount(container));
                foreach (var hand in _vrPlayerService.GetHandGrabbers())
                {
                    if (hand.Held?.GetComponentInParent<VobLoader>()?.Container == container)
                        hand.ForceRelease();
                }
                _vobService.RemoveWorldItem(container);
            }
        }

        private static bool IsSameItem(VobContainer container, string itemName)
        {
            if (container?.Vob is not IItem item)
                return false;
            var instanceName = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
            return instanceName.EqualsIgnoreCase(itemName);
        }

        private static int GetAmount(VobContainer container)
        {
            return Mathf.Max(1, ((IItem)container.Vob).Amount);
        }
    }
}
