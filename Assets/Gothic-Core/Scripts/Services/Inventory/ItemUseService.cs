using System;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Caches;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using static Gothic.Core.Models.Vm.VmGothicEnums;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Inventory
{
    /// <summary>
    /// "An NPC uses an item" like the engine's "use item from inventory": the item's on_state[0] runs with
    /// self = the NPC and item = the item. Platforms only detect the gesture (VR: trigger, mouth, reading) and call it.
    /// </summary>
    public class ItemUseService
    {
        private const string _useEndAnimationScheme = "t_{0}_S0_2_Stand";

        // Used in their own ways: weapons, runes/scrolls, documents, food and potions. Wearables are equipped.
        private const int _notUsableCategories = (int)(ItemFlags.ItemKatNf | ItemFlags.ItemKatFf |
                                                       ItemFlags.ItemKatMun | ItemFlags.ItemKatArmor |
                                                       ItemFlags.ItemKatFood | ItemFlags.ItemKatDocs |
                                                       ItemFlags.ItemKatPotions | ItemFlags.ItemKatRune |
                                                       ItemFlags.ItemKatMagic);

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;


        /// <summary>
        /// Using a wearable (armor, amulet, ring, belt) means equipping it.
        /// </summary>
        public bool IsEquippedOnUse(ItemInstance item)
        {
            return item.IsWearable();
        }

        /// <summary>
        /// An item with an on_state[0] function that no other interaction handles, e.g. G2 pouches (ItSe_*) or mod
        /// items - or a wearable (IsEquippedOnUse).
        /// </summary>
        public bool IsUsable(ItemInstance item)
        {
            if (item == null)
                return false;

            if (IsEquippedOnUse(item))
                return true;

            return item.GetOnState(0) != 0 && (item.MainFlag & _notUsableCategories) == 0;
        }

        /// <summary>
        /// Used up like in the engine: the end animation of the item's scheme (t_[Scheme]_S0_2_Stand) fires
        /// DEF_DESTROY_ITEM - e.g. MAPSEALED (sealed letters, G2 pouches). Items like maps (MAP -> DEF_REMOVE_ITEM)
        /// stay.
        /// </summary>
        public bool IsConsumedOnUse(ItemInstance item)
        {
            if (string.IsNullOrEmpty(item?.SchemeName))
                return false;

            var animationName = string.Format(_useEndAnimationScheme, item.SchemeName);
            var anim = _resourceCacheService.TryGetModelScript("Humans")?.Animations
                .FirstOrDefault(i => i.Name.EqualsIgnoreCase(animationName));

            return anim != null && anim.EventTags.Any(i => i.Type == EventType.ItemDestroy);
        }

        /// <summary>
        /// Calls the item's on_state[0] for the NPC. Returns false if there is none or the call failed.
        /// Consuming the item is up to the caller (IsConsumedOnUse), as it knows where the item is (hand, inventory).
        /// </summary>
        public bool UseItem(NpcInstance npc, ItemInstance item)
        {
            if (npc == null || item == null)
            {
                Logger.LogWarning($"[ItemUse] No NPC or item to use (item='{item?.Name}').", LogCat.Npc);
                return false;
            }

            var function = item.GetOnState(0);
            if (function == 0)
            {
                Logger.LogWarning($"[ItemUse] '{item.Name}' has no on_state[0] - nothing to use.", LogCat.Npc);
                return false;
            }

            if (!CallItemFunction(npc, item, function))
                return false;

            Logger.Log($"[ItemUse] {npc.GetName(NpcNameSlot.Slot0)} used '{item.Name}' (on_state[0]={function})",
                LogCat.Npc);
            return true;
        }

        /// <summary>
        /// Runs an item function (on_state[0], on_equip, on_unequip) with self = npc and item = item, like the engine.
        /// Both globals are restored afterwards, as this can run inside another VM call (e.g. a deferred on_equip).
        /// </summary>
        public bool CallItemFunction(NpcInstance npc, ItemInstance item, int function)
        {
            if (function <= 0)
                return false;

            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            var oldItem = vm.GlobalItem;
            vm.GlobalSelf = npc;
            vm.GlobalItem = item;
            try
            {
                vm.Call(function);
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError($"[ItemUse] Item function {function} of '{item?.Name}' failed: {e.Message}",
                    LogCat.Npc);
                return false;
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalItem = oldItem;
            }
        }
    }
}
