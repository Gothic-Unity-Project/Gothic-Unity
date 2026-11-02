using System;
using System.Linq;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vm;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using EventType = ZenKit.EventType;
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
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

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
        /// beginStateFunction (optional) is called right before "[onStateFunc]_S1" and returns what to do after it
        /// (e.g. VR: a bookstand's page for Doc_Show).
        /// </summary>
        public void HandleMobGrab(VobLoader loader, Func<GameObject> createDialogAnchor,
            Func<Action> beginStateFunction = null)
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

            // DeveloperConfig.EnableFireStates: touching a fireplace/torch lights it or puts it out.
            if (_configService.Dev.EnableFireStates && mob is IFire &&
                loader.GetComponentInChildren<VobFireHandler>(true) is { } fire)
            {
                fire.Toggle();
                return;
            }

            // Crafting mobs that take their useWithItem into the hand (forge, anvil, ...): items swapped like the engine.
            if (_configService.Dev.EnableMobItemEvents && string.IsNullOrEmpty(mob.Target) &&
                TryUseMobWithItemEvents(loader, mob, createDialogAnchor, beginStateFunction))
                return;

            // Mobs without a mover target but with an onStateFunc (G2 shrines, alchemy/rune tables, bookstands, ...).
            if (string.IsNullOrEmpty(mob.Target) &&
                TryUseMobStateFunction(loader, mob, createDialogAnchor, beginStateFunction))
                return;

            var target = mob.Target;
            if (string.IsNullOrEmpty(target))
            {
                Logger.Log($"[MobInteractionService] HandleMobGrab: no Target on {loader.gameObject.name}",
                    LogCat.Vob);
                return;
            }

            if (IsMissingRequiredItem(mob))
                return;

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
            Func<GameObject> createDialogAnchor, Func<Action> beginStateFunction, bool isItemInHand = false)
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
            // Not when it was already taken into the hand (TryUseMobWithItemEvents).
            var hero = _playerService.HeroContainer;
            if (!isItemInHand && !string.IsNullOrEmpty(mob.Item))
            {
                var item = _vmCacheService.TryGetItemData(mob.Item);
                if (item != null && _npcInventoryService.ExtNpcHasItems(hero.Instance, item.Index) <= 0)
                {
                    Logger.Log($"[MobDialog] '{loader.name}' needs '{mob.Item}'.", LogCat.Dialog);
                    PlayMobMissingItemReaction(hero);
                    return true;
                }
            }

            var endStateFunction = beginStateFunction?.Invoke();
            _dialogService.PrepareMobDialog(createDialogAnchor());
            _vmService.CallAsHero(functionName, LogCat.Dialog);
            Logger.Log($"[MobDialog] Called '{functionName}' for '{loader.name}'.", LogCat.Dialog);
            endStateFunction?.Invoke();

            // Not every _S1 opens a dialog (e.g. a script only prints a message) - then the anchor isn't needed.
            _dialogService.ReleaseUnusedMobDialogAnchor();

            return true;
        }

        /// <summary>
        /// DeveloperConfig.EnableMobRequiredItem: like the engine (OpenGothic Interactive::checkUseConditions), a mob
        /// with useWithItem only works when the player has that item, else Player_Mob_Missing_Item is said. G2's
        /// Jharkendar portal (ItMi_PortalRing_Addon) opened without the ornament ring. Only for mobs with a trigger
        /// target - the crafting mobs (anvil, stove, ...) are handled above. The item isn't taken, as in OpenGothic.
        /// </summary>
        private bool IsMissingRequiredItem(IInteractiveObject mob)
        {
            if (!_configService.Dev.EnableMobRequiredItem || string.IsNullOrEmpty(mob.Item))
                return false;

            var hero = _playerService.HeroContainer;
            var itemSymbol = _gameStateService.GothicVm.GetSymbolByName(mob.Item);
            if (itemSymbol == null || hero == null ||
                _npcInventoryService.ExtNpcHasItems(hero.Instance, itemSymbol.Index) > 0)
                return false;

            Logger.Log($"[MobInteractionService] HandleMobGrab: '{mob.Name}' needs '{mob.Item}' - the hero has none",
                LogCat.Vob);
            PlayMobMissingItemReaction(hero);
            return true;
        }

        /// <summary>
        /// DeveloperConfig.EnableMobItemEvents: like the engine, the human mob animations move the mob's useWithItem
        /// (HUMANS.MDS events of T_[scheme]_S0_2_S1 / _S1_2_S0): ItemInsert takes one into the hand, ItemExchange swaps
        /// it, ItemRemove puts it back into the inventory, ItemDestroy uses it up. G2's forge turns raw steel into hot
        /// steel (BSFIRE), the anvil takes the hot steel before SMITHWEAPON_S1 counts it (Npc_HasItems + 1). Before,
        /// the forge did nothing and the anvil only said "With what?".
        /// </summary>
        private bool TryUseMobWithItemEvents(VobLoader loader, IInteractiveObject mob,
            Func<GameObject> createDialogAnchor, Func<Action> beginStateFunction)
        {
            if (string.IsNullOrEmpty(mob.Item) || string.IsNullOrEmpty(mob.Visual?.Name))
                return false;

            var scheme = mob.Visual.Name.Split('_')[0];
            var mds = _resourceCacheService.TryGetModelScript("HUMANS", false);
            var useAnim = mds?.Animations.FirstOrDefault(a => a.Name.EqualsIgnoreCase($"T_{scheme}_S0_2_S1"));
            if (useAnim == null || useAnim.EventTags.All(e => e.Type != EventType.ItemInsert))
                return false;

            var hero = _playerService.HeroContainer;
            var item = _vmCacheService.TryGetItemData(mob.Item);
            if (item == null || _npcInventoryService.ExtNpcHasItems(hero.Instance, item.Index) <= 0)
            {
                Logger.Log($"[MobItem] '{loader.name}' ({scheme}) needs '{mob.Item}'.", LogCat.Vob);
                PlayMobMissingItemReaction(hero);
                return true;
            }

            _playerService.RemoveItem(mob.Item);
            var handItem = mob.Item;
            foreach (var tag in useAnim.EventTags)
            {
                if (tag.Type == EventType.ItemExchange && !string.IsNullOrEmpty(tag.Item))
                    handItem = tag.Item;
            }

            if (!string.IsNullOrEmpty(mob.OnStateChangeFunction))
                TryUseMobStateFunction(loader, mob, createDialogAnchor, beginStateFunction, isItemInHand: true);

            var leaveAnim = mds.Animations.FirstOrDefault(a => a.Name.EqualsIgnoreCase($"T_{scheme}_S1_2_S0"));
            var isPutBack = leaveAnim?.EventTags.Any(e => e.Type == EventType.ItemRemove) ?? false;
            if (isPutBack)
                _playerService.AddItem(handItem);

            Logger.Log($"[MobItem] '{loader.name}' ({scheme}): '{mob.Item}' taken" +
                       (isPutBack ? $", '{handItem}' back into the inventory" : " and used up"), LogCat.Vob);
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
