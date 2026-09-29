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
using Gothic.Core.Services.Vobs;
using Gothic.VR.Services;
using HurricaneVR.Framework.ControllerInput;
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

        // Handled elsewhere: equipping (weapons/armor/amulets), VRRuneCaster, VRDocViewer, VRMouth.
        private const int _notUsableCategories = (int)(VmGothicEnums.ItemFlags.ItemKatNf | VmGothicEnums.ItemFlags.ItemKatFf |
                                                       VmGothicEnums.ItemFlags.ItemKatMun | VmGothicEnums.ItemFlags.ItemKatArmor |
                                                       VmGothicEnums.ItemFlags.ItemKatFood | VmGothicEnums.ItemFlags.ItemKatDocs |
                                                       VmGothicEnums.ItemFlags.ItemKatPotions | VmGothicEnums.ItemFlags.ItemKatRune |
                                                       VmGothicEnums.ItemFlags.ItemKatMagic);

        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly VobService _vobService;

        private VobContainer _container;
        private ItemInstance _item;
        private bool _usedThisGrab;


        public static bool IsUsable(ItemInstance item)
        {
            return item != null && item.GetOnState(0) != 0 && (item.MainFlag & _notUsableCategories) == 0;
        }

        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            _container = GetComponentInParent<VobLoader>()?.Container;
            _item = _container?.PropsAs<VobItemProperties2>()?.Instance;

            if (!IsUsable(_item))
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

        private bool IsConsumedOnUse()
        {
            if (string.IsNullOrEmpty(_item.SchemeName))
                return false;

            var animationName = string.Format(_useEndAnimationScheme, _item.SchemeName);
            var anim = _resourceCacheService.TryGetModelScript("Humans")?.Animations
                .FirstOrDefault(i => i.Name.EqualsIgnoreCase(animationName));

            return anim != null && anim.EventTags.Any(i => i.Type == EventType.ItemDestroy);
        }

        private void Consume()
        {
            if (_container.Vob is IItem vobItem && vobItem.Amount > 1)
            {
                vobItem.Amount--;
                Logger.Log($"[VRItemUser] {_item.Name} used, remaining={vobItem.Amount}", LogCat.VR);
                return;
            }

            Logger.Log($"[VRItemUser] {_item.Name} used up", LogCat.VR);
            _vobService.RemoveWorldItem(_container);
        }
    }
}
#endif
