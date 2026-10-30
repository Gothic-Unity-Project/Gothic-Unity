using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vm;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// Platform-neutral part of using a mob (wheel, lever, shrine, ...) with the hero. The platform layer only
    /// detects the grab and provides the position of the dialog anchor.
    /// </summary>
    public class MobInteractionService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        // Per mob scheme (PAN, BSFIRE, ...): the item its use turns the used item into, null for none, and whether its
        // use destroys the used item.
        private readonly Dictionary<string, string> _exchangeItems = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _destroyingSchemes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A mob used with an item and without a script of its own (pan, forge, anvil, cooling barrel, grindstone):
        /// like the engine, its animation swaps the item (Humans.mds t_SCHEME_S1_2_S0, ItemExchange) - raw mutton
        /// becomes fried mutton, raw steel hot steel, then a hot blade, a blade and a sword.
        /// </summary>
        public bool IsItemExchangeMob(IVirtualObject vob)
        {
            return vob is IInteractiveObject mob && vob is not IContainer && vob is not IDoor &&
                   !string.IsNullOrEmpty(mob.Item) && string.IsNullOrEmpty(mob.OnStateChangeFunction) &&
                   GetExchangeItem(GetScheme(vob)) != null;
        }

        /// <summary>
        /// Turns the mob's item into what the mob makes of it. The hero needs the item: held in his hand (the platform
        /// takes the held one away, so only the new item is added here) or in his inventory (taken from there).
        /// </summary>
        /// <returns>The new item's instance name, null if nothing changed.</returns>
        public string TryExchangeMobItem(IVirtualObject vob, bool isItemHeld)
        {
            if (!IsItemExchangeMob(vob))
                return null;

            var mob = (IInteractiveObject)vob;
            var hero = _playerService.HeroContainer;
            var usedItem = _vmCacheService.TryGetItemData(mob.Item);
            var newItem = _vmCacheService.TryGetItemData(GetExchangeItem(GetScheme(vob)));
            if (usedItem == null || newItem == null)
                return null;

            if (!isItemHeld)
            {
                if (_npcInventoryService.ExtNpcHasItems(hero.Instance, usedItem.Index) <= 0)
                {
                    Logger.Log($"[MobInteractionService] '{vob.Name}' needs '{mob.Item}'.", LogCat.Vob);
                    PlayMobMissingItemReaction(hero);
                    return null;
                }

                _npcInventoryService.ExtRemoveInvItems(hero.Instance, usedItem.Index, 1);
            }

            _npcInventoryService.ExtCreateInvItems(hero.Instance, newItem.Index, 1);
            var newItemName = _gameStateService.GothicVm.GetSymbolByIndex(newItem.Index)?.Name;
            Logger.Log($"[MobInteractionService] '{vob.Name}' turns {mob.Item} into {newItemName}", LogCat.Vob);
            return newItemName;
        }

        private static string GetScheme(IVirtualObject vob)
        {
            var visual = vob.Visual?.Name ?? string.Empty;
            var end = visual.IndexOf('_');
            if (end < 0)
                end = visual.IndexOf('.');
            return end > 0 ? visual.Substring(0, end) : visual;
        }

        /// <summary>
        /// The item the scheme's use animations swap the used one for (ItemExchange), null for none. Like the engine,
        /// both transitions count: G1's pan, forge, anvil and cooling barrel swap on the way out (t_SCHEME_S1_2_S0), G2's
        /// forge on the way in (t_BSFIRE_S0_2_S1, found on BoroBongo's branch).
        /// </summary>
        private string GetExchangeItem(string scheme)
        {
            if (string.IsNullOrEmpty(scheme))
                return null;
            if (_exchangeItems.TryGetValue(scheme, out var cached))
                return cached;

            string result = null;
            var isDestroying = false;
            var humans = _resourceCacheService.TryGetModelScript("Humans");
            if (humans != null)
            {
                var useStart = $"t_{scheme}_S0_2_S1";
                var useEnd = $"t_{scheme}_S1_2_S0";
                foreach (var animation in humans.Animations)
                {
                    if (!animation.Name.EqualsIgnoreCase(useStart) && !animation.Name.EqualsIgnoreCase(useEnd))
                        continue;

                    foreach (var tag in animation.EventTags)
                    {
                        if (tag.Type == ZenKit.EventType.ItemExchange && !string.IsNullOrEmpty(tag.Item))
                            result ??= tag.Item;
                        else if (tag.Type == ZenKit.EventType.ItemDestroy)
                            isDestroying = true;
                    }
                }
            }

            _exchangeItems[scheme] = result;
            _destroyingSchemes[scheme] = isDestroying;
            return result;
        }

        /// <summary>
        /// The scheme's use animations destroy the used item (ItemDestroy) without swapping it for another.
        /// </summary>
        private bool IsDestroyingScheme(string scheme)
        {
            if (GetExchangeItem(scheme) != null)
                return false;

            return _destroyingSchemes.TryGetValue(scheme ?? "", out var isDestroying) && isDestroying;
        }

        /// <summary>
        /// Doors use VR hinge physics and chests their lid physics - not the mob/trigger path.
        /// Beds are oCMobDoors too (BED*/BEDHIGH* visuals), but get used like other mobs (SLEEPABIT_S1).
        /// </summary>
        public bool ShouldHandleGrab(IVirtualObject vob)
        {
            if (vob is not IInteractiveObject)
                return false;

            return !((vob is IDoor && !vob.IsBed()) || vob is IContainer);
        }

        /// <summary>
        /// The hero grabbed a mob (wheel, lever, ...): like AI_UseMob, its ConditionFunction decides whether it may be
        /// used, then its Target (e.g. a gate mover, a code master or a trigger list) is triggered.
        /// createDialogAnchor is only called if the mob opens a dialog (platform specific position).
        /// </summary>
        public void HandleMobGrab(VobLoader loader, Func<GameObject> createDialogAnchor)
        {
            var vm = _gameStateService.GothicVm;
            if (loader.Container.Vob is not IInteractiveObject mob || vm == null)
                return;

            var condFunc = mob.ConditionFunction ?? "";
            Logger.Log($"[MobInteractionService] HandleMobGrab mob={loader.gameObject.name} condFunc={condFunc}",
                LogCat.Vob);

            // ConditionFunction is a Daedalus check: returns 0 = interaction denied.
            // Like the engine (OpenGothic invokeCond): runs with self = hero. A script error denies the interaction.
            if (condFunc.Length > 0)
            {
                if (!_vmService.CallAsHero<int>(condFunc, LogCat.Vob, out var condResult))
                {
                    Logger.LogWarning($"[MobInteractionService] ConditionFunction '{condFunc}' not found in VM",
                        LogCat.Vob);
                    return;
                }

                if (condResult == 0)
                {
                    Logger.Log($"[MobInteractionService] HandleMobGrab: '{condFunc}' denied (returned 0)", LogCat.Vob);
                    return;
                }
            }

            // Mobs without a mover target but with an onStateFunc (G2 shrines, alchemy/rune tables, bookstands, ...).
            if (string.IsNullOrEmpty(mob.Target) && TryUseMobStateFunction(loader, mob, createDialogAnchor))
                return;

            var target = mob.Target;
            if (string.IsNullOrEmpty(target))
            {
                Logger.Log($"[MobInteractionService] HandleMobGrab: no Target on {loader.gameObject.name}",
                    LogCat.Vob);
                return;
            }

            // Like the engine (OpenGothic Interactive::implTick): the mob keeps its state. Used from state 0 it sends a
            // trigger, used again from its upper state an untrigger (e.g. a TriggerControl gate closes again).
            // Movers react per their behavior (MoverAdapter.OnTrigger/OnUntrigger), other targets (CodeMaster,
            // TriggerList, TriggerScript, ...) run their chain. The mob's VOB name is the sender, so a CodeMaster
            // can match it against its Slaves list.
            var senderName = loader.Container.Vob.Name;
            if (mob.State > 0)
            {
                mob.State = 0;
                _vobService.DispatchUntrigger(target, senderName);
            }
            else
            {
                mob.State = 1;
                _vobService.DispatchTrigger(target, senderName);
            }
        }

        /// <summary>
        /// Like the engine reaching mob state 1: call "[onStateFunc]_S1" with self = hero. MOBSI scripts like
        /// PrayShrine_S1 then open a dialog via AI_ProcessInfos(hero) whose choices are C_INFOs with npc = PC_Hero.
        /// The dialog box is anchored next to the mob (like at an NPC).
        /// </summary>
        private bool TryUseMobStateFunction(VobLoader loader, IInteractiveObject mob,
            Func<GameObject> createDialogAnchor)
        {
            if (string.IsNullOrEmpty(mob.OnStateChangeFunction))
                return false;

            // X marks are dug up by hitting them (VRDigSpot) - grabbing must not dig.
            if (_vmCacheService.IsDigSpot(loader.Container.Vob))
                return false;

            var functionName = mob.OnStateChangeFunction + "_S1";
            if (_gameStateService.GothicVm.GetSymbolByName(functionName) == null)
            {
                Logger.Log($"[MobDialog] '{functionName}' not in the scripts - mob '{loader.name}' ignored.",
                    LogCat.Dialog);
                return false;
            }

            // Like the engine: a mob used with an item needs it in the inventory (e.g. a lab flask).
            var hero = _playerService.HeroContainer;
            if (!string.IsNullOrEmpty(mob.Item))
            {
                var item = _vmCacheService.TryGetItemData(mob.Item);
                if (item != null && _npcInventoryService.ExtNpcHasItems(hero.Instance, item.Index) <= 0)
                {
                    Logger.Log($"[MobDialog] '{loader.name}' needs '{mob.Item}'.", LogCat.Dialog);
                    PlayMobMissingItemReaction(hero);
                    return true;
                }

                // G2's anvil: its use animation destroys the hot steel before SMITHWEAPON offers the weapons (the
                // script's end choice gives it back). Found on BoroBongo's branch.
                if (item != null && IsDestroyingScheme(GetScheme(loader.Container.Vob)))
                {
                    _npcInventoryService.ExtRemoveInvItems(hero.Instance, item.Index, 1);
                    Logger.Log($"[MobDialog] '{loader.name}' takes {mob.Item}.", LogCat.Dialog);
                }
            }

            _dialogService.PrepareMobDialog(createDialogAnchor());
            _vmService.CallAsHero(functionName, LogCat.Dialog);
            Logger.Log($"[MobDialog] Called '{functionName}' for '{loader.name}'.", LogCat.Dialog);

            // Not every _S1 opens a dialog (e.g. a script only prints a message) - then the anchor isn't needed.
            _dialogService.ReleaseUnusedMobDialogAnchor();

            return true;
        }

        /// <summary>
        /// Like the engine (OpenGothic printMobMissingItem): G2 scripts have PLAYER_MOB_MISSING_ITEM (missing item
        /// text + the hero's "$MISSINGITEM" line). G1 has none - nothing to say then.
        /// </summary>
        private void PlayMobMissingItemReaction(NpcContainer hero)
        {
            if (_gameStateService.GothicVm.GetSymbolByName("PLAYER_MOB_MISSING_ITEM") == null)
                return;

            hero.Props.AnimationQueue.Clear();
            _vmService.CallAsHero("PLAYER_MOB_MISSING_ITEM", LogCat.Dialog);
            _dialogService.RunHeroQueue();
        }
    }
}
