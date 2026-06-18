using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Npc;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class UndrawWeapon : AbstractAnimationAction
    {
        public UndrawWeapon(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;

            if (weaponState == VmGothicEnums.WeaponState.NoWeapon)
            {
                IsFinishedFlag = true;
                return;
            }

            MoveWeaponBackToStowSlot(weaponState);

            var prefix = AnimationService.GetWeaponAnimationPrefix(weaponState);

            // From here on, follow-up animations are weaponless again (s_RunL, ...).
            Vob.FightMode = (int)VmGothicEnums.WeaponState.NoWeapon;

            var animationName = $"t_{prefix}Move_2_Move";

            if (!PrefabProps.AnimationSystem.PlayAnimation(animationName))
            {
                IsFinishedFlag = true;
                return;
            }

            ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(animationName);
        }

        private void MoveWeaponBackToStowSlot(VmGothicEnums.WeaponState weaponState)
        {
            // Fists carry no mesh.
            if (weaponState == VmGothicEnums.WeaponState.Fist)
                return;

            var handGo = NpcGo.FindChildRecursively(DrawWeapon.GetHandSlotName(weaponState));
            var slotGo = NpcGo.FindChildRecursively(DrawWeapon.GetStowSlotName(weaponState));

            if (handGo == null || slotGo == null || handGo.transform.childCount == 0)
                return;

            handGo.transform.GetChild(0).gameObject.SetParent(slotGo, true, true);
        }
    }
}
