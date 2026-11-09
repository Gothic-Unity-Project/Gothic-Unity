using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Inventory;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Gothic's "use item from inventory" in VR (ItemUseService decides what is usable and runs it), e.g. G2 pouches
    /// (ItSe_*) or mod items. Lives while the item is grabbed with both hands; trigger (or R) uses it once.
    /// Weapons, runes, documents and food are handled by VRRuneCaster, VRDocViewer and VRMouth.
    /// </summary>
    public class VRItemUser : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly ItemUseService _itemUseService;

        private VobContainer _container;
        private ItemInstance _item;
        private bool _usedThisGrab;


        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            _container = GetComponentInParent<VobLoader>()?.Container;
            _item = _container?.PropsAs<VobItemProperties2>()?.Instance;

            if (!_itemUseService.IsUsable(_item))
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
            if (_vrInput.IsSimulator)
                triggered = Keyboard.current != null && Keyboard.current[Key.R].wasPressedThisFrame;
            else
                triggered = _vrInput.GetButton(VrHandSide.Right, VrButton.Trigger).IsJustActivated;

            if (!triggered)
                return;

            // One use per dual-grab, like the rune cast. Re-grab to use the next one of a stack.
            _usedThisGrab = true;
            Use();
        }

        private void Use()
        {
            if (_itemUseService.IsEquippedOnUse(_item))
            {
                Equip();
                return;
            }

            if (!_itemUseService.UseItem(_gameStateService.GothicVm.GlobalHero as NpcInstance, _item))
                return;

            if (_itemUseService.IsConsumedOnUse(_item))
                Consume();
        }

        /// <summary>
        /// The worn item moves from the world (our hands) into the hero's inventory - like putting it into the backpack -
        /// and gets equipped. Taking it out of the backpack again unequips it (VRBackpack).
        /// </summary>
        private void Equip()
        {
            // Destroys our GameObject (and this component) at the end of the frame.
            if (!_vrPlayerService.StoreHeldItem(_container))
                return;

            _npcInventoryService.EquipItemWithEffects(_playerService.HeroContainer.Instance, _item);
        }

        private void ReleaseFromHands()
        {
            var interactable = VrInteractable.Find(this);
            if (interactable != null && interactable.IsHeld)
                interactable.ForceRelease();
        }

        private void Consume()
        {
            var item = _container.VobAs<IItem>();
            var instanceName = item.GetSymbolName();

            if (item.Amount > 1)
            {
                item.Amount--;
                // The held stack counts as inventory (added with its full amount on grab, removed with its then-current
                // amount on release) - take the used one out now, or it stays in the inventory as a ghost.
                var interactable = VrInteractable.Find(this);
                if (interactable != null && interactable.IsHeld)
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
