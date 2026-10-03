#if GOTHIC_HVR_INSTALLED
using System;
using System.Linq;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Services;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using EventType = ZenKit.EventType;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Gothic's "use item from inventory" for items with an on_state[0] function which no other VR interaction handles,
    /// e.g. G2 pouches (ItSe_*) or mod items. Lives while the item is grabbed with both hands; trigger (or R) uses it once.
    ///
    /// Consumption follows the engine: an item is used up if its scheme's end animation (t_[Scheme]_S0_2_Stand)
    /// fires DEF_DESTROY_ITEM (e.g. MAPSEALED for pouches/sealed letters). Items like maps (MAP -> DEF_REMOVE_ITEM) stay.
    /// </summary>
    public class VRItemUser : MonoBehaviour
    {
        private const string _useEndAnimationScheme = "t_{0}_S0_2_Stand";

        // Handled elsewhere: weapons, VRRuneCaster, VRDocViewer, VRMouth. Wearables only via Equip() (flag).
        private const int _notUsableCategories = (int)(VmGothicEnums.ItemFlags.ItemKatNf | VmGothicEnums.ItemFlags.ItemKatFf |
                                                       VmGothicEnums.ItemFlags.ItemKatMun | VmGothicEnums.ItemFlags.ItemKatArmor |
                                                       VmGothicEnums.ItemFlags.ItemKatFood | VmGothicEnums.ItemFlags.ItemKatDocs |
                                                       VmGothicEnums.ItemFlags.ItemKatPotions | VmGothicEnums.ItemFlags.ItemKatRune |
                                                       VmGothicEnums.ItemFlags.ItemKatMagic);

        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly SaveGameService _saveGameService;

        private VobContainer _container;
        private ItemInstance _item;
        private bool _usedThisGrab;


        /// <param name="allowEquip">DeveloperConfig.EnableEquipItems - wearables (armor, amulets, rings, belts) get equipped.</param>
        public static bool IsUsable(ItemInstance item, bool allowEquip)
        {
            if (item == null)
                return false;

            if (allowEquip && NpcInventoryService.IsWearable(item))
                return true;

            return item.GetOnState(0) != 0 && (item.MainFlag & _notUsableCategories) == 0;
        }

        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            _container = GetComponentInParent<VobLoader>()?.Container;
            _item = _container?.PropsAs<VobItemProperties2>()?.Instance;

            if (!IsUsable(_item, _configService.Dev.EnableEquipItems))
            {
                Destroy(this);
                return;
            }

            Logger.Log($"[VRItemUser] {_item.Name} ready to use - press trigger (R).", LogCat.VR);
        }

        private void Update()
        {
            if (_item == null || _usedThisGrab)
                return;

            bool triggered;
            if (_vrPlayerService.VRPlayerInputs.UseWASD)
                triggered = Keyboard.current[Key.R].wasPressedThisFrame;
            else
                triggered = HVRController.GetButtonState(HVRHandSide.Right, HVRButtons.Trigger).JustActivated;

            if (!triggered)
                return;

            // One use per dual-grab, like the rune cast. Re-grab to use the next one of a stack.
            _usedThisGrab = true;
            Use();
        }

        private void Use()
        {
            if (_configService.Dev.EnableEquipItems && NpcInventoryService.IsWearable(_item))
            {
                Equip();
                return;
            }

            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            vm.GlobalItem = _item;
            try
            {
                vm.Call(_item.GetOnState(0));
                Logger.Log($"[VRItemUser] Called on_state[0] for {_item.Name}", LogCat.VR);
            }
            catch (Exception e)
            {
                Logger.LogError($"[VRItemUser] on_state[0] failed for {_item.Name}: {e.Message}", LogCat.VR);
                return;
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }

            if (IsConsumedOnUse())
                Consume();
        }

        /// <summary>
        /// The worn item moves from the world (our hands) into the hero's inventory - like putting it into the backpack -
        /// and gets equipped. Taking it out of the backpack again unequips it (VRBackpack).
        /// </summary>
        private void Equip()
        {
            var hero = _playerService.HeroContainer;
            var vobItem = _container.VobAs<IItem>();
            var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;

            // Items held in a hand already count as inventory (VRPlayerService.SetGrab/UnsetGrab). Release it first,
            // which takes it out of the inventory again - otherwise AddItem() below would duplicate it (x1 -> x2 -> x4).
            ReleaseFromHands();

            _saveGameService.UntrackLooseItem(_container);
            _playerService.AddItem(instanceName, Mathf.Max(1, vobItem.Amount));
            _npcInventoryService.EquipItemWithEffects(hero.Instance, _item);

            // Destroys our GameObject (and this component).
            _vobService.RemoveWorldItem(_container);
        }

        private void ReleaseFromHands()
        {
            var grabbable = GetComponent<HVRGrabbable>();
            if (grabbable != null && grabbable.IsBeingHeld)
                grabbable.ForceRelease();
        }

        private bool IsConsumedOnUse() => IsConsumedOnUse(_item, _resourceCacheService);

        /// <summary>
        /// Used up like in the engine: the end animation of the item's scheme (t_[Scheme]_S0_2_Stand) fires
        /// DEF_DESTROY_ITEM - e.g. MAPSEALED (sealed letters, G2 pouches). Also used by documents (VRVobItem).
        /// </summary>
        public static bool IsConsumedOnUse(ItemInstance item, ResourceCacheService resourceCacheService)
        {
            if (string.IsNullOrEmpty(item?.SchemeName))
                return false;

            var animationName = string.Format(_useEndAnimationScheme, item.SchemeName);
            var anim = resourceCacheService.TryGetModelScript("Humans")?.Animations
                .FirstOrDefault(i => i.Name.EqualsIgnoreCase(animationName));

            return anim != null && anim.EventTags.Any(i => i.Type == EventType.ItemDestroy);
        }

        private void Consume()
        {
            var item = _container.VobAs<IItem>();
            var instanceName = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;

            if (item.Amount > 1)
            {
                item.Amount--;
                // The held stack counts as inventory (added with its full amount on grab, removed with its then-current
                // amount on release) - take the used one out now, or it stays in the inventory as a ghost.
                var grabbable = GetComponent<HVRGrabbable>();
                if (grabbable != null && grabbable.IsBeingHeld)
                    _playerService.RemoveItem(instanceName, 1);

                Logger.Log($"[VRItemUser] {_item.Name} used, remaining={item.Amount}", LogCat.VR);
                return;
            }

            Logger.Log($"[VRItemUser] {_item.Name} used up", LogCat.VR);
            // Release first (removes it from the inventory like any dropped item), then destroy.
            ReleaseFromHands();
            _vobService.RemoveWorldItem(_container);
        }
    }
}
#endif
