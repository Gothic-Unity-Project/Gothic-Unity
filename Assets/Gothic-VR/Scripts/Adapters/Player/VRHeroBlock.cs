using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The hero's parry in VR: both hands on one melee weapon (like Gothic's parry animation holds it at grip and blade)
    /// and both triggers held. FightService then stops melee hits from the front like an NPC's parry. Found on
    /// BoroBongo's branch.
    /// </summary>
    public class VRHeroBlock : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly FightService _fightService;

        private void Update()
        {
            _fightService.IsHeroBlocking = _vrPlayerService.IsDualGrabbed &&
                                           IsMeleeWeapon(_vrPlayerService.GrabbedItemLeft) &&
                                           _vrInput.GetButton(VrHandSide.Left, VrButton.Trigger).IsActive &&
                                           _vrInput.GetButton(VrHandSide.Right, VrButton.Trigger).IsActive;
        }

        private void OnDisable()
        {
            _fightService.IsHeroBlocking = false;
        }

        private static bool IsMeleeWeapon(GameObject heldItem)
        {
            var item = heldItem?.GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            return item != null && ((VmGothicEnums.ItemFlags)item.MainFlag).HasFlag(VmGothicEnums.ItemFlags.ItemKatNf);
        }
    }
}
