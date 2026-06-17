using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Npc;
using JetBrains.Annotations;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class DrawWeapon : AbstractAnimationAction
    {
        private bool _isRangedRequested => Action.Int0 == 1;

        public DrawWeapon(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            var weapon = GetEquippedWeapon();
            var weaponState = GetWeaponState(weapon);

            // The fight mode must be set before the animation plays: all follow-up animations
            // (s_1hRunL, t_2hRunTurnL, s_FistAttack, ...) compose their names from it.
            Vob.FightMode = (int)weaponState;

            MoveWeaponToHand(weapon, weaponState);

            var prefix = AnimationService.GetWeaponAnimationPrefix(weaponState);
            var animationName = $"t_Move_2_{prefix}Move";

            if (!PrefabProps.AnimationSystem.PlayAnimation(animationName))
            {
                IsFinishedFlag = true;
                return;
            }

            ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(animationName);
        }

        [CanBeNull]
        private ItemInstance GetEquippedWeapon()
        {
            var mainFlag = _isRangedRequested ? VmGothicEnums.ItemFlags.ItemKatFf : VmGothicEnums.ItemFlags.ItemKatNf;

            return Props.EquippedItems.FirstOrDefault(i => i.MainFlag == (int)mainFlag);
        }

        private VmGothicEnums.WeaponState GetWeaponState([CanBeNull] ItemInstance weapon)
        {
            // No weapon equipped: fight with fists (the default for monsters and brawling humans).
            if (weapon == null)
                return VmGothicEnums.WeaponState.Fist;

            var flags = (VmGothicEnums.ItemFlags)weapon.Flags;

            if (_isRangedRequested)
                return flags.HasFlag(VmGothicEnums.ItemFlags.ItemCrossbow)
                    ? VmGothicEnums.WeaponState.CBow
                    : VmGothicEnums.WeaponState.Bow;

            return flags.HasFlag(VmGothicEnums.ItemFlags.Item2HdAxe) || flags.HasFlag(VmGothicEnums.ItemFlags.Item2HdSwd)
                ? VmGothicEnums.WeaponState.W2H
                : VmGothicEnums.WeaponState.W1H;
        }

        /// <summary>
        /// Reparent the weapon mesh from its stow slot (back/hip) into the hand.
        /// FIXME - Ideally this happens on the animation's DEF_DRAWSOUND event instead of immediately.
        /// </summary>
        private void MoveWeaponToHand([CanBeNull] ItemInstance weapon, VmGothicEnums.WeaponState weaponState)
        {
            if (weapon == null)
                return;

            var slotGo = NpcGo.FindChildRecursively(GetStowSlotName(weaponState));
            var handGo = NpcGo.FindChildRecursively(GetHandSlotName(weaponState));

            if (slotGo == null || handGo == null || slotGo.transform.childCount == 0)
                return;

            slotGo.transform.GetChild(0).gameObject.SetParent(handGo, true, true);
        }

        public static string GetStowSlotName(VmGothicEnums.WeaponState weaponState)
        {
            return weaponState switch
            {
                VmGothicEnums.WeaponState.W2H => Constants.SlotLongsword,
                VmGothicEnums.WeaponState.Bow => Constants.SlotBow,
                VmGothicEnums.WeaponState.CBow => Constants.SlotCrossbow,
                _ => Constants.SlotSword
            };
        }

        public static string GetHandSlotName(VmGothicEnums.WeaponState weaponState)
        {
            // Bows are held in the left hand (the right hand draws the arrow), everything else in the right.
            return weaponState == VmGothicEnums.WeaponState.Bow ? Constants.SlotLeftHand : Constants.SlotRightHand;
        }
    }
}
