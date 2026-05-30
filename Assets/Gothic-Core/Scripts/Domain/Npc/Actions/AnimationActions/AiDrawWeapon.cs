using GUZ.Core.Models.Container;
using GUZ.Core.Extensions;
using EventType = ZenKit.EventType;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class DrawWeapon : AbstractAnimationAction
    {
        public DrawWeapon(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            // FIXME - We need to handle both mds and mdh options! (base vs overlay)
            // "t_1hRun_2_1h" --> undraw animation!
            // "t_Move_2_1hMove" --> drawing
            // "t_1h_2_1hRun"
            PrefabProps.AnimationSystem.PlayAnimation("t_Move_2_1hMove");
        }

        // FIXME - Hardcoded. We need to set it dynamically and not copying the ZS, but an object below. Otherwise it's hard to find previous parent when undrawing.
        private void SyncZSlots()
        {
            var rightHand = NpcGo.FindChildRecursively("ZS_RIGHTHAND");
            var weapon1HSlot = NpcGo.FindChildRecursively("ZS_SWORD");

            // No weapon equipped in slot.
            if (weapon1HSlot.transform.childCount == 0)
            {
                return;
            }

            var weaponGo = weapon1HSlot.transform.GetChild(0).gameObject;

            weaponGo.SetParent(rightHand, true, true);
        }
    }
}
