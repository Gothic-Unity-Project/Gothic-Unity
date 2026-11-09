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
    /// Daedalus externals about what an NPC carries: inventory items, equipped and readied weapons and armor.
    /// </summary>
    public class VmNpcInventoryExternalDomain
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly ProjectileService _projectileService;
        [Inject] private readonly GameStateService _gameStateService;

        private readonly VmExternalSupport _support;

        public VmNpcInventoryExternalDomain(VmExternalSupport support)
        {
            _support = support;
            this.Inject();
        }

        public void Register(VmExternalGuard vm)
        {
            vm.RegisterExternal<NpcInstance, int>("CreateInvItem", CreateInvItem);
            vm.RegisterExternal<NpcInstance, int, int>("CreateInvItems", CreateInvItems);
            vm.RegisterExternal<NpcInstance, int>("Npc_RemoveInvItem", Npc_RemoveInvItem);
            vm.RegisterExternal<NpcInstance, int, int>("Npc_RemoveInvItems", Npc_RemoveInvItems);
            vm.RegisterExternal<int, NpcInstance, int>("Npc_HasItems", Npc_HasItems);
            vm.RegisterExternal<ItemInstance, NpcInstance>("Npc_GetEquippedArmor", Npc_GetEquippedArmor);
            // PxVm.pxVmRegisterExternal(vmPtr, "Npc_GetInvItem", Npc_GetInvItem);
            vm.RegisterExternal<int, NpcInstance, int, int>("Npc_GetInvItemBySlot", Npc_GetInvItemBySlot);
            // PxVm.pxVmRegisterExternal(vmPtr, "Npc_RemoveInvItem", Npc_RemoveInvItem);
            // PxVm.pxVmRegisterExternal(vmPtr, "Npc_RemoveInvItems", Npc_RemoveInvItems);
            vm.RegisterExternal<NpcInstance, int>("EquipItem", EquipItem);
            vm.RegisterExternal<int, NpcInstance>("Npc_HasEquippedArmor", Npc_HasEquippedArmor);
            vm.RegisterExternal<ItemInstance, NpcInstance>("Npc_GetEquippedMeleeWeapon", Npc_GetEquippedMeleeWeapon);
            vm.RegisterExternal<int, NpcInstance>("Npc_HasEquippedMeleeWeapon", Npc_HasEquippedMeleeWeapon);
            vm.RegisterExternal<ItemInstance, NpcInstance>("Npc_GetEquippedRangedWeapon", Npc_GetEquippedRangedWeapon);
            vm.RegisterExternal<int, NpcInstance>("Npc_HasEquippedRangedWeapon", Npc_HasEquippedRangedWeapon);
            vm.RegisterExternal<int, NpcInstance>("Npc_HasReadiedMeleeWeapon", Npc_HasReadiedMeleeWeapon);
            vm.RegisterExternal<int, NpcInstance>("Npc_HasReadiedRangedWeapon", Npc_HasReadiedRangedWeapon);
            vm.RegisterExternal<ItemInstance, NpcInstance>("Npc_GetReadiedWeapon", Npc_GetReadiedWeapon);
            vm.RegisterExternal<int, NpcInstance>("Npc_HasRangedWeaponWithAmmo", Npc_HasRangedWeaponWithAmmo);
            // G2 only - G1's scripts don't declare it.
            if (vm.GetSymbolByName("Npc_ClearInventory") != null)
                vm.RegisterExternal<NpcInstance>("Npc_ClearInventory", Npc_ClearInventory);
            // G1: 24 uses - orc AI (ZS_Orc_Attack/WatchFight/AssessWarn) never saw a drawn weapon.
            if (vm.GetSymbolByName("Npc_HasReadiedWeapon") != null)
                vm.RegisterExternal<int, NpcInstance>("Npc_HasReadiedWeapon", Npc_HasReadiedWeapon);
        }

        /// <summary>
        /// Engine: any weapon in the hands - melee, ranged or magic (not fists).
        /// </summary>
        public int Npc_HasReadiedWeapon(NpcInstance npc)
        {
            var fightMode = (VmGothicEnums.WeaponState)(npc?.GetUserData()?.Vob.FightMode ?? 0);
            var ret = Convert.ToInt32(fightMode is VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H
                or VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow or VmGothicEnums.WeaponState.Mage);
            return _support.LogInstantExternal(nameof(Npc_HasReadiedWeapon), ret, npc);
        }

        public void CreateInvItem(NpcInstance npc, int itemId)
        {
            _npcInventoryService.ExtCreateInvItems(npc, itemId, 1);
            _support.LogInstantExternal(nameof(CreateInvItem), npc, itemId);
        }

        public void CreateInvItems(NpcInstance npc, int itemId, int amount)
        {
            _npcInventoryService.ExtCreateInvItems(npc, itemId, amount);
            _support.LogInstantExternal(nameof(CreateInvItem), npc, itemId, amount);
        }

        public void Npc_RemoveInvItem(NpcInstance npc, int itemId)
        {
            _npcInventoryService.ExtRemoveInvItems(npc, itemId, 1);
            GlobalEventDispatcher.ScriptRemovedInvItems.Invoke(npc.GetUserData(), itemId, 1);
            _support.LogInstantExternal(nameof(Npc_RemoveInvItem), npc, itemId);
        }

        public void Npc_RemoveInvItems(NpcInstance npc, int itemId, int amount)
        {
            _npcInventoryService.ExtRemoveInvItems(npc, itemId, amount);
            GlobalEventDispatcher.ScriptRemovedInvItems.Invoke(npc.GetUserData(), itemId, amount);
            _support.LogInstantExternal(nameof(Npc_RemoveInvItems), npc, itemId, amount);
        }

        public int Npc_HasItems(NpcInstance npc, int itemId)
        {
            var count = _npcInventoryService.ExtNpcHasItems(npc, itemId);
            return _support.LogInstantExternal(nameof(Npc_HasItems), count, npc, itemId);
        }

        public ItemInstance Npc_GetEquippedArmor(NpcInstance npc)
        {
            var item = _npcAiService.ExtGetEquippedArmor(npc);
            return _support.LogInstantExternal(nameof(Npc_GetEquippedArmor), item, npc);
        }

        public void Npc_GetInvItem(IntPtr vmPtr)
        {
            // NpcCreator.ExtGetInvItem();
        }

        public int Npc_GetInvItemBySlot(NpcInstance npc, int invType, int slot)
        {
            var amount = _npcInventoryService.ExtNpcGetInvItemBySlot(npc, invType, slot);
            return _support.LogInstantExternal(nameof(Npc_GetInvItemBySlot), amount, npc, invType, slot);
        }

        public void Npc_RemoveInvItems(IntPtr vmPtr)
        {
            // NpcCreator.ExtRemoveInvItems();
        }

        public void EquipItem(NpcInstance npc, int itemId)
        {
            _npcInventoryService.ExtEquipItem(npc, itemId);
        }

        public int Npc_HasEquippedArmor(NpcInstance npc)
        {
            var ret = _npcAiService.ExtNpcHasEquippedArmor(npc) ? 1 : 0;
            return _support.LogInstantExternal(nameof(Npc_HasEquippedArmor), ret, npc);
        }

        public ItemInstance Npc_GetEquippedMeleeWeapon(NpcInstance npc)
        {
            var item = npc.Index == _gameStateService.GothicVm.GlobalHero?.Index
                ? _npcHelperService.GetHeroEquippedMeleeWeapon()
                : _npcHelperService.ExtNpcGetEquippedMeleeWeapon(npc);
            return _support.LogInstantExternal(nameof(Npc_GetEquippedMeleeWeapon), item, npc);
        }

        public int Npc_HasEquippedMeleeWeapon(NpcInstance npc)
        {
            var ret = _npcHelperService.ExtNpcHasEquippedMeleeWeapon(npc) ? 1 : 0;
            return _support.LogInstantExternal(nameof(Npc_HasEquippedMeleeWeapon), ret, npc);
        }

        public ItemInstance Npc_GetEquippedRangedWeapon(NpcInstance npc)
        {
            var item = _npcHelperService.ExtNpcGetEquippedRangedWeapon(npc);
            return _support.LogInstantExternal(nameof(Npc_GetEquippedRangedWeapon), item, npc);
        }

        public int Npc_HasEquippedRangedWeapon(NpcInstance npc)
        {
            var ret = _npcHelperService.ExtNpcHasEquippedRangedWeapon(npc) ? 1 : 0;
            return _support.LogInstantExternal(nameof(Npc_HasEquippedRangedWeapon), ret, npc);
        }

        public int Npc_HasReadiedMeleeWeapon(NpcInstance npc)
        {
            var fightMode = (VmGothicEnums.WeaponState)npc.GetUserData().Vob.FightMode;
            var ret = Convert.ToInt32(fightMode == VmGothicEnums.WeaponState.W1H ||
                                     fightMode == VmGothicEnums.WeaponState.W2H);
            return _support.LogInstantExternal(nameof(Npc_HasReadiedMeleeWeapon), ret, npc);
        }

        public int Npc_HasReadiedRangedWeapon(NpcInstance npc)
        {
            var fightMode = (VmGothicEnums.WeaponState)npc.GetUserData().Vob.FightMode;
            var ret = Convert.ToInt32(fightMode == VmGothicEnums.WeaponState.Bow ||
                                     fightMode == VmGothicEnums.WeaponState.CBow);
            return _support.LogInstantExternal(nameof(Npc_HasReadiedRangedWeapon), ret, npc);
        }

        public ItemInstance Npc_GetReadiedWeapon(NpcInstance npc)
        {
            var fightMode = (VmGothicEnums.WeaponState)npc.GetUserData().Vob.FightMode;
            var isHero = npc.Index == _gameStateService.GothicVm.GlobalHero?.Index;
            ItemInstance item = fightMode switch
            {
                // The VR hero's readied weapon is the one in his hand (Ulu-Mulu: orcs tolerate him).
                VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H when isHero
                    => _npcHelperService.HeroReadiedMeleeWeapon,
                VmGothicEnums.WeaponState.W1H or VmGothicEnums.WeaponState.W2H
                    => _npcHelperService.ExtNpcGetEquippedMeleeWeapon(npc),
                VmGothicEnums.WeaponState.Bow or VmGothicEnums.WeaponState.CBow
                    => _npcHelperService.ExtNpcGetEquippedRangedWeapon(npc),
                _ => null
            };
            return _support.LogInstantExternal(nameof(Npc_GetReadiedWeapon), item, npc);
        }

        public int Npc_HasRangedWeaponWithAmmo(NpcInstance npc)
        {
            // Like the engine, only a ranged weapon with its munition counts -
            // B_SelectWeapon then switches to the melee weapon when the arrows/bolts are gone (B_FillQuiver refills
            // pure
            // archers, FAI_HUMAN_RANGED, while they still have some). Before, an archer without bolts kept "shooting".
            var hasAmmo = _projectileService.HasRangedWeaponWithAmmo(npc.GetUserData());
            if (!hasAmmo)
                KeepChasingOutOfAmmo(npc);
            return _support.LogInstantExternal(nameof(Npc_HasRangedWeaponWithAmmo), hasAmmo ? 1 : 0, npc);
        }

        /// <summary>
        /// An archer whose arrows/bolts ran out fights on with his melee
        /// weapon - but G1's ZS_Attack_Loop gives a melee fight up beyond HAI_DIST_ABORT_MELEE (10 m) unless the last
        /// damage came from a ranged weapon (AIV_LASTHITBYRANGEDWEAPON). Lukis gave up, ZS_AssessEnemy started the
        /// fight
        /// again at once: draw, put away, draw every 2 s. Marked like that, he chases until the script's pursuit limit
        /// (HAI_TIME_FOLLOW); the next melee hit resets it (B_CombatReactToDamage).
        /// </summary>
        private void KeepChasingOutOfAmmo(NpcInstance npc)
        {
            if (_npcHelperService.ExtNpcGetEquippedRangedWeapon(npc) == null)
                return;

            var aivar = _gameStateService.GothicVm.GetSymbolByName(DaedalusConst.AivLastHitByRangedWeapon)
                ?.GetInt(0) ?? -1;
            if (aivar < 0 || npc.GetAiVar(aivar) != 0)
                return;

            npc.SetAiVar(aivar, 1);
            Logger.Log($"[Projectile] {npc.GetName(NpcNameSlot.Slot0)} is out of ammo - keeps chasing with his melee " +
                       "weapon (AIV_LASTHITBYRANGEDWEAPON)", LogCat.Fight);
        }

        public void Npc_ClearInventory(NpcInstance npc)
        {
            _npcInventoryService.ExtNpcClearInventory(npc);
            _support.LogInstantExternal(nameof(Npc_ClearInventory), npc);
        }
    }
}
