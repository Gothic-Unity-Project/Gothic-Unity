using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.UI;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Models.Doc;
using Gothic.Core.Creator;
using MyBox;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Random = UnityEngine.Random;
using Time = UnityEngine.Time;

namespace Gothic.Core.Domain.Vm
{
    /// <summary>
    /// Daedalus AI_* externals: the queued NPC actions (walk, turn, play animation, dialog, weapons).
    /// </summary>
    public class VmAiExternalDomain
    {
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        private readonly VmExternalSupport _support;

        private readonly HashSet<string> _externalsCalledWithoutNpc = new();

        public VmAiExternalDomain(VmExternalSupport support)
        {
            _support = support;
            this.Inject();
        }

        public void Register(VmExternalGuard vm)
        {
            vm.RegisterExternal<NpcInstance>("Ai_Attack", Ai_Attack);
            vm.RegisterExternal<NpcInstance>("AI_StandUp", AI_StandUp);
            vm.RegisterExternal<NpcInstance>("AI_StandUpQuick", AI_StandUpQuick);
            vm.RegisterExternal<NpcInstance, int>("AI_SetWalkMode", AI_SetWalkMode);
            vm.RegisterExternal<NpcInstance>("AI_AlignToFP", AI_AlignToFP);
            vm.RegisterExternal<NpcInstance>("AI_AlignToWP", AI_AlignToWP);
            vm.RegisterExternal<NpcInstance, string>("AI_GotoFP", AI_GotoFP);
            vm.RegisterExternal<NpcInstance, string>("AI_GotoWP", AI_GotoWP);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_GotoNpc", AI_GotoNpc);
            vm.RegisterExternal<NpcInstance, string>("AI_PlayAni", AI_PlayAni);
            vm.RegisterExternal<NpcInstance, int, int, string>("AI_StartState", AI_StartState);
            vm.RegisterExternal<NpcInstance, int, int>("AI_UseItemToState", AI_UseItemToState);
            VmExternalSupport.RegisterIfDeclared(vm, "AI_UseItem",
                () => vm.RegisterExternal<NpcInstance, int>("AI_UseItem", AI_UseItem));
            VmExternalSupport.RegisterIfDeclared(vm, "AI_UnequipWeapons",
                () => vm.RegisterExternal<NpcInstance>("AI_UnequipWeapons", AI_UnequipWeapons));
            vm.RegisterExternal<NpcInstance, float>("AI_Wait", AI_Wait);
            vm.RegisterExternal<NpcInstance, int>("AI_WaitMs", AI_WaitMs);
            vm.RegisterExternal<int, NpcInstance, string, int>("AI_UseMob", AI_UseMob);
            vm.RegisterExternal<NpcInstance, string>("AI_Teleport", AI_Teleport);
            vm.RegisterExternal<NpcInstance, string>("AI_GoToNextFP", AI_GoToNextFP);
            vm.RegisterExternal<NpcInstance>("AI_DrawWeapon", AI_DrawWeapon);
            vm.RegisterExternal<NpcInstance>("AI_ReadyMeleeWeapon", AI_ReadyMeleeWeapon);
            vm.RegisterExternal<NpcInstance>("AI_ReadyRangedWeapon", AI_ReadyRangedWeapon);
            vm.RegisterExternal<NpcInstance>("AI_RemoveWeapon", AI_RemoveWeapon);
            vm.RegisterExternal<NpcInstance>("AI_EquipBestMeleeWeapon", AI_EquipBestMeleeWeapon);
            vm.RegisterExternal<NpcInstance>("AI_EquipBestRangedWeapon", AI_EquipBestRangedWeapon);
            vm.RegisterExternal<NpcInstance, int, int>("AI_ReadySpell", AI_ReadySpell);
            vm.RegisterExternal<NpcInstance>("AI_UnreadySpell", AI_UnreadySpell);
            vm.RegisterExternal<NpcInstance>("AI_StopAim", AI_StopAim);
            vm.RegisterExternal<NpcInstance, NpcInstance, string>("AI_Output", AI_Output);
            vm.RegisterExternal<NpcInstance>("AI_ProcessInfos", AI_ProcessInfos);
            vm.RegisterExternal<NpcInstance>("AI_StopProcessInfos", AI_StopProcessInfos);
            vm.RegisterExternal<NpcInstance, string>("AI_LookAt", AI_LookAt);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_LookAtNPC", AI_LookAtNPC);
            VmExternalSupport.RegisterIfDeclared(vm, "AI_Quicklook",
                () => vm.RegisterExternal<NpcInstance, NpcInstance>("AI_Quicklook", AI_Quicklook));
            vm.RegisterExternal<NpcInstance>("AI_StopLookAt", AI_StopLookAt);
            vm.RegisterExternal<NpcInstance>("AI_ContinueRoutine", AI_ContinueRoutine);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_TurnToNPC", AI_TurnToNPC);
            if (vm.GetSymbolByName("AI_WaitTillEnd") != null)
                vm.RegisterExternal<NpcInstance, NpcInstance>("AI_WaitTillEnd", AI_WaitTillEnd);
            if (vm.GetSymbolByName("AI_AimAt") != null)
                vm.RegisterExternal<NpcInstance, NpcInstance>("AI_AimAt", AI_AimAt);
            if (vm.GetSymbolByName("AI_StopFX") != null)
                vm.RegisterExternal<NpcInstance, string>("AI_StopFX", AI_StopFX);
            vm.RegisterExternal<NpcInstance, string, int>("AI_PlayAniBS", AI_PlayAniBS);
            vm.RegisterExternal<NpcInstance>("AI_Dodge", AI_Dodge);
            vm.RegisterExternal<NpcInstance>("AI_UnequipArmor", AI_UnequipArmor);
            // Item as int like OpenGothic - the armor symbol usually has no initialized C_ITEM instance.
            vm.RegisterExternal<NpcInstance, int>("AI_EquipArmor", AI_EquipArmor);
            vm.RegisterExternal<NpcInstance>("AI_EquipBestArmor", AI_EquipBestArmor);
            vm.RegisterExternal<NpcInstance, NpcInstance, string>("AI_OutputSVM", AI_OutputSVM);
            vm.RegisterExternal<NpcInstance, NpcInstance, string>("AI_OutputSVM_Overlay", AI_OutputSVM_Overlay);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_WhirlAround", AI_WhirlAround);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_TurnAway", AI_TurnAway);
            vm.RegisterExternal<NpcInstance, NpcInstance>("AI_FinishingMove", AI_FinishingMove);
            vm.RegisterExternal<NpcInstance, int>("AI_DropItem", AI_DropItem);
            vm.RegisterExternal<DaedalusInstance, DaedalusInstance>("AI_TakeItem",
                (n, i) =>
                {
                    if (_support.AsNpc(n, "AI_TakeItem") is { } npc &&
                        _support.AsItem(i, "AI_TakeItem") is { } item)
                        AI_TakeItem(npc, item);
                });
            VmExternalSupport.RegisterIfDeclared(vm, "AI_GotoItem",
                () => vm.RegisterExternal<DaedalusInstance, DaedalusInstance>("AI_GotoItem",
                    (n, i) =>
                    {
                        if (_support.AsNpc(n, "AI_GotoItem") is { } npc &&
                            _support.AsItem(i, "AI_GotoItem") is { } item)
                            AI_GotoItem(npc, item);
                    }));
            if (vm.GetSymbolByName("AI_Flee") != null)
                vm.RegisterExternal<NpcInstance>("AI_Flee", AI_Flee);
        }

        public void Ai_Attack(NpcInstance npc)
        {
            _npcAiService.ExtAttack(npc);
        }

        public void AI_FinishingMove(NpcInstance self, NpcInstance other)
        {
            var attacker = self?.GetUserData();
            var target = other?.GetUserData();
            if (attacker == null || target == null) return;
            if (target.Props.BodyState == VmGothicEnums.BodyState.BsDead) return;

            Logger.Log($"[VmAiExternalDomain.AI_FinishingMove] {self.GetName(NpcNameSlot.Slot0)} " +
                       $"executes {other.GetName(NpcNameSlot.Slot0)}",
                LogCat.Fight);

            // Queued like the engine - walk there, turn, the finishing stab.
            _npcAiService.ExtAiFinishingMove(self, other);
        }

        public void AI_StandUp(NpcInstance npc)
        {
            _npcAiService.ExtAiStandUp(npc);
        }

        public void AI_StandUpQuick(NpcInstance npc)
        {
            _npcAiService.ExtAiStandUpQuick(npc);
        }

        public void AI_SetWalkMode(NpcInstance npc, int walkMode)
        {
            _npcAiService.ExtAiSetWalkMode(npc, (VmGothicEnums.WalkMode)walkMode);
        }

        public void AI_AlignToFP(NpcInstance npc)
        {
            _npcAiService.ExtAiAlignToFp(npc);
        }

        public void AI_AlignToWP(NpcInstance npc)
        {
            _npcAiService.ExtAiAlignToWp(npc);
        }

        public void AI_GotoFP(NpcInstance npc, string freePointName)
        {
            _npcAiService.ExtAiGoToFp(npc, freePointName);
        }

        public void AI_GotoWP(NpcInstance npc, string wayPointName)
        {
            _npcAiService.ExtAiGoToWp(npc, wayPointName);
        }

        public void AI_GotoNpc(NpcInstance self, NpcInstance other)
        {
            _npcAiService.ExtAiGoToNpc(self, other);
        }

        public void AI_PlayAni(NpcInstance npc, string name)
        {
            _npcAiService.ExtAiPlayAni(npc, name);
        }

        public void AI_StartState(NpcInstance npc, int function, int stateBehaviour, string wayPointName)
        {
            _npcAiService.ExtAiStartState(npc, function, Convert.ToBoolean(stateBehaviour), wayPointName);
        }

        /// <summary>
        /// The NPC takes the item out, uses it up (on_state[0]: potions heal) and puts the hand down - like the engine.
        /// NPCs drink a potion after a fight (ZS_HealSelf); unhandled, the queue got stuck there.
        /// </summary>
        public void AI_UseItem(NpcInstance npc, int itemId)
        {
            var resolved = ResolveItemId(itemId);
            if (resolved < 0)
            {
                Logger.LogWarning(
                    $"AI_UseItem: the global item of {npc?.GetName(NpcNameSlot.Slot0)} is empty - skipped",
                    LogCat.Ai);
                return;
            }
            _npcAiService.ExtAiUseItem(npc, resolved);
        }

        public void AI_UseItemToState(NpcInstance npc, int itemId, int expectedInventoryCount)
        {
            var resolved = ResolveItemId(itemId);
            if (resolved < 0 || npc?.GetUserData()?.Props == null)
            {
                Logger.LogWarning($"AI_UseItemToState: no NPC or the global item of " +
                                  $"{npc?.GetName(NpcNameSlot.Slot0)} " +
                                  "is empty - skipped", LogCat.Ai);
                return;
            }

            _npcAiService.ExtAiUseItemToState(npc, resolved, expectedInventoryCount);
        }

        /// <summary>
        /// `AI_UseItem(self, item)` hands over the global `item` variable
        /// (G1 ZS_HealSelf: food found by Npc_GetInvItemBySlot) - its symbol, not an item instance. The engine uses the
        /// item in it. The unknown "item" left Lukis frozen after a fight (the use never finished).
        /// </summary>
        private int ResolveItemId(int itemId)
        {
            var vm = _gameStateService.GothicVm;
            var symbol = vm.GetSymbolByIndex(itemId);
            if (symbol == null || !string.Equals(symbol.Name, "ITEM", StringComparison.OrdinalIgnoreCase))
                return itemId;

            // -1: the global item is empty - nothing to use.
            return vm.GlobalItem is ItemInstance item ? item.Index : -1;
        }

        public void AI_Wait(NpcInstance npc, float seconds)
        {
            _npcAiService.ExtAiWait(npc, seconds);
        }

        public void AI_WaitMs(NpcInstance npc, int miliseconds)
        {
            _npcAiService.ExtAiWait(npc, miliseconds / 1000f);
        }

        public int AI_UseMob(NpcInstance npc, string target, int state)
        {
            _npcAiService.ExtAiUseMob(npc, target, state);

            // Hint: It seems the int value is a bug as no G1 Daedalus usage needs it.
            var ret = 0;
            return _support.LogInstantExternal(nameof(AI_UseMob), ret, npc, target, state);
        }

        public void AI_Teleport(NpcInstance npc, string waypointName)
        {
            var container = npc.GetUserData();
            var waypoint = _wayNetService.GetWayNetPoint(waypointName);

            if (waypoint == null)
            {
                Logger.LogWarning($"AI_Teleport: waypoint '{waypointName}' not found", LogCat.Npc);
                return;
            }

            if (container.PrefabProps.IsHero())
            {
                GlobalEventDispatcher.HeroScriptTeleporting.Invoke();
                _contextInteractionService.TeleportPlayerTo(waypoint.Position);
                Logger.Log($"[AI_Teleport] Hero → {waypointName}", LogCat.Npc);
            }
            else if (container.Go != null)
            {
                container.Go.transform.position = waypoint.Position;
                Logger.Log($"[AI_Teleport] {container.Go.name} → {waypointName}", LogCat.Npc);
            }
        }

        public void AI_GoToNextFP(NpcInstance npc, string fpNamePart)
        {
            _npcAiService.ExtAiGoToNextFp(npc, fpNamePart);
        }

        public void AI_DrawWeapon(NpcInstance npc)
        {
            _npcAiService.ExtAiDrawWeapon(npc);
        }

        public void AI_ReadyMeleeWeapon(NpcInstance npc)
        {
            _npcAiService.ExtAiDrawWeapon(npc);
        }

        public void AI_ReadyRangedWeapon(NpcInstance npc)
        {
            _npcAiService.ExtAiReadyRangedWeapon(npc);
        }

        public void AI_RemoveWeapon(NpcInstance npc)
        {
            _npcAiService.ExtAiUndrawWeapon(npc);
        }

        public void AI_EquipBestMeleeWeapon(NpcInstance npc)
        {
            _npcInventoryService.ExtAiEquipBestMeleeWeapon(npc);
        }

        public void AI_EquipBestRangedWeapon(NpcInstance npc)
        {
            _npcInventoryService.ExtAiEquipBestRangedWeapon(npc);
        }

        public void AI_ReadySpell(NpcInstance npc, int spellId, int investMana)
        {
            _npcAiService.ExtAiReadySpell(npc, spellId, investMana);
            _support.LogInstantExternal(nameof(AI_ReadySpell), npc, spellId, investMana);
        }

        public void AI_UnreadySpell(NpcInstance npc)
        {
            _npcAiService.ExtAiUndrawWeapon(npc);
            _support.LogInstantExternal(nameof(AI_UnreadySpell), npc);
        }

        public void AI_StopAim(NpcInstance npc)
        {
            // Stub — NPC ranged aiming not yet implemented; NPC will sheath via AI_RemoveWeapon.
        }

        public void AI_Output(NpcInstance self, NpcInstance target, string outputName)
        {
            _dialogService.ExtAiOutput(self, target, outputName);
        }

        public void AI_ProcessInfos(NpcInstance npc)
        {
            _dialogService.ExtAiProcessInfos(npc ?? GetHeroForMissingNpc(nameof(AI_ProcessInfos)));
        }

        public void AI_StopProcessInfos(NpcInstance npc)
        {
            _dialogService.ExtAiStopProcessInfos(npc ?? GetHeroForMissingNpc(nameof(AI_StopProcessInfos)));
        }

        /// <summary>
        /// G1's SLEEPABIT_S1 calls AI_ProcessInfos(Hlp_GetNpc(PC_Hero)) - empty when the mod's hero is another instance
        /// (Mroczne Tajemnice: PC_Rockefeller), and the bed's sleep menu threw. The scripts mean the hero there.
        /// </summary>
        private NpcInstance GetHeroForMissingNpc(string externalName)
        {
            if (_externalsCalledWithoutNpc.Add(externalName))
                Logger.LogWarning($"[{externalName}] called with no NPC - using the hero.", LogCat.Dialog);
            return (NpcInstance)_gameStateService.GothicVm.GlobalHero;
        }

        public void AI_LookAt(NpcInstance npc, string waypoint)
        {
            _npcAiService.ExtAiLookAt(npc, waypoint);
        }

        public void AI_LookAtNPC(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtAiLookAtNpc(npc, target);
        }

        public void AI_Quicklook(NpcInstance self, NpcInstance other)
        {
            _npcAiService.ExtAiQuicklook(self, other);
        }

        public void AI_StopLookAt(NpcInstance npc)
        {
            _npcAiService.ExtAiStopLookAt(npc);
        }

        public void AI_Dodge(NpcInstance npc)
        {
            _npcAiService.ExtAiDodge(npc);
        }

        public void AI_ContinueRoutine(NpcInstance npc)
        {
            _npcAiService.ExtAiContinueRoutine(npc);
        }

        public void AI_TurnToNPC(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtAiTurnToNpc(npc, target);
        }

        public void AI_WaitTillEnd(NpcInstance npc, NpcInstance other)
        {
            _npcAiService.ExtAiWaitTillEnd(npc, other);
        }

        public void AI_AimAt(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtAiAimAt(npc, target);
        }

        /// <summary>
        /// There are no effects on NPCs to stop yet (AI_PlayFX isn't implemented either).
        /// </summary>
        public void AI_StopFX(NpcInstance npc, string effect)
        {
            _support.LogInstantExternal(nameof(AI_StopFX), new object[] { npc, effect });
        }

        public void AI_PlayAniBS(NpcInstance npc, string name, int bodyState)
        {
            _npcAiService.ExtAiPlayAniBs(npc, name, bodyState);
        }

        public void AI_UnequipWeapons(NpcInstance npc)
        {
            _npcInventoryService.ExtAiUnequipWeapons(npc);
        }

        public void AI_UnequipArmor(NpcInstance npc)
        {
            // An NPC's own equipment can't undress the hero. Mroczne
            // Tajemnice's helmets (Equip_helms) and armors check and undress `hero`, not `self` - NPCs created after
            // the hero (we load them later than the engine) took PC_Rockefeller's armor off at the start.
            var owner = _npcInventoryService.ItemFunctionOwner;
            if (owner != null && npc != null &&
                owner.Index != npc.Index && npc.Index == _gameStateService.GothicVm.GlobalHero?.Index)
            {
                Logger.Log($"[AI_UnequipArmor] {owner.GetName(NpcNameSlot.Slot0)}'s item function wanted to undress " +
                           "the hero - ignored", LogCat.Npc);
                return;
            }

            _npcAiService.ExtAiUnequipArmor(npc);
        }

        public void AI_EquipArmor(NpcInstance npc, int itemIndex)
        {
            _npcAiService.ExtAiEquipArmor(npc, itemIndex);
        }

        public void AI_EquipBestArmor(NpcInstance npc)
        {
            _npcAiService.ExtAiEquipBestArmor(npc);
        }

        public void AI_OutputSVM(NpcInstance npc, NpcInstance target, string svmname)
        {
            _dialogService.ExtAiOutputSvm(npc, target, svmname);
        }

        public void AI_OutputSVM_Overlay(NpcInstance npc, NpcInstance target, string svmname)
        {
            _dialogService.ExtAiOutputSvmOverlay(npc, target, svmname);
        }

        public void AI_WhirlAround(NpcInstance npc, NpcInstance target)
        {
            // The startled jump around (t_surprise_CW/CCW), not a plain turn.
            _npcAiService.ExtAiWhirlAround(npc, target);
        }

        public void AI_TurnAway(NpcInstance npc, NpcInstance target)
        {
            _npcAiService.ExtAiTurnAway(npc, target);
        }

        public void AI_DropItem(NpcInstance npc, int itemInstanceId)
        {
            var npcContainer = npc.GetUserData();
            if (npcContainer?.Go == null)
            {
                Logger.LogWarning($"[AI_DropItem] NPC container or GO is null for {npc.GetName(NpcNameSlot.Slot0)}",
                    LogCat.Npc);
                return;
            }

            Logger.Log($"[AI_DropItem] {npc.GetName(NpcNameSlot.Slot0)} drops item id={itemInstanceId}", LogCat.Npc);
            _vobService.DropItemAtPosition(itemInstanceId, npcContainer.Go.transform.position);
        }

        public void AI_TakeItem(NpcInstance npc, ItemInstance item)
        {
            if (npc == null || item == null) return;
            var npcContainer = npc.GetUserData();
            if (npcContainer == null) return;

            var sym = _gameStateService.GothicVm.GetSymbolByIndex(item.Index);
            if (sym == null) return;

            Logger.Log($"[AI_TakeItem] {npc.GetName(NpcNameSlot.Slot0)} queuing pickup of '{sym.Name}'", LogCat.Npc);

            // Enqueue pickup so it executes after any pending stand-up / dialog animations
            npcContainer.Props.AnimationQueue.Enqueue(
                new TakeItem(new AnimationAction(), npcContainer, item.Index, sym.Name));
        }

        /// <summary>
        /// Runs away from the NPC's enemy (target, or other) - ZS_Flee_Loop calls it every loop.
        /// </summary>
        public void AI_Flee(NpcInstance npc)
        {
            var container = npc?.GetUserData();
            if (container?.Props == null)
                return;
            var enemy = container.Props.EnemyNpc ?? container.Props.TargetNpc ??
                        _gameStateService.GothicVm.GlobalOther as NpcInstance;
            container.Props.AnimationQueue.Enqueue(new Gothic.Core.Domain.Npc.Actions.AnimationActions.Flee(
                new Gothic.Core.Domain.Npc.Actions.AnimationAction(instance0: enemy), container));
        }

        /// <summary>
        /// Walks to a world item (G2's wisp shows the hero where items are).
        /// </summary>
        public void AI_GotoItem(NpcInstance npc, ItemInstance item)
        {
            var npcContainer = npc?.GetUserData();
            var itemContainer = _npcHelperService.GetWorldItemContainer(npc, item);
            if (npcContainer == null || itemContainer?.Go == null)
            {
                Logger.Log($"[AI_GotoItem] {npc?.GetName(NpcNameSlot.Slot0)}: no world item for '{item?.Name}' " +
                           "- skipped",
                    LogCat.Ai);
                return;
            }

            npcContainer.Props.AnimationQueue.Enqueue(
                new Gothic.Core.Domain.Npc.Actions.AnimationActions.GoToItem(new AnimationAction(), npcContainer,
                    itemContainer.Go));
        }
    }
}
