using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Wearing in VR: the trigger on a held armor, amulet, ring or belt puts it on or takes it off (EquipmentService),
    /// like a click in Gothic's inventory. A short buzz tells which: a success pulse when worn, a soft one when taken
    /// off, an error when it can't be worn (the scripts say why).
    /// </summary>
    public class VRWearableEquip : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly IVrHaptics _haptics;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly EquipmentService _equipmentService;

        private void Update()
        {
            TryToggle(VrHandSide.Left, _vrPlayerService.GrabbedItemLeft);
            TryToggle(VrHandSide.Right, _vrPlayerService.GrabbedItemRight);
        }

        private void TryToggle(VrHandSide side, GameObject heldItem)
        {
            if (heldItem == null || !_vrInput.GetButton(side, VrButton.Trigger).IsJustActivated)
                return;

            var item = heldItem.GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            if (item == null || item.Type != DaedalusInstanceType.Item || !EquipmentService.IsWearable(item))
                return;

            var hero = _npcService.GetHeroContainer();
            var wasWorn = _equipmentService.IsEquipped(hero, item);
            if (!_equipmentService.ToggleEquip(hero, item))
                _haptics.Vibrate(side, VrHapticPreset.Error);
            else
                _haptics.Vibrate(side, wasWorn ? VrHapticPreset.Touch : VrHapticPreset.Success);
        }
    }
}
