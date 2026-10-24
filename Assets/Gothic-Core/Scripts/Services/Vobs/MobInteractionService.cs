using System;
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
