using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Npc;
using JetBrains.Annotations;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class DrawWeapon : AbstractAnimationAction
    {
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly AudioService _audioService;

        private bool _isRangedRequested => Action.Int0 == 1;

        public DrawWeapon(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            // An NPC that draws its weapon doesn't keep the dialog box open.
            if (GameStateService.Dialogs.IsInDialog && GameStateService.Dialogs.CurrentDialogNpc == NpcContainer)
            {
                Logger.Log($"[DrawWeapon] {NpcInstance.GetName(NpcNameSlot.Slot0)} drew a weapon during the dialog " +
                           "- closing the dialog", LogCat.Dialog);
                _dialogService.StopDialog(NpcContainer);
            }

            var weapon = GetEquippedWeapon();
            var weaponState = GetWeaponState(weapon);

            // The fight mode must be set before the animation plays: all follow-up animations
            // (s_1hRunL, t_2hRunTurnL, s_FistAttack, ...) compose their names from it.
            Vob.FightMode = (int)weaponState;

            // The weapon in the hand: AI_Attack takes its range from it.
            Props.CurrentItem = weapon?.Index ?? -1;

            // Like the engine, the NPC fights with the stance of its weapon talent (HUMANS_1HST1.MDS, ...). The
            // routine's overlay (HUMANS_RELAXED.MDS) comes back when the weapon is put away.
            var fightOverlay = GetFightOverlayName(weaponState);
            if (fightOverlay != null)
                Props.MdsNameOverlay = fightOverlay;

            MoveWeaponToHand(weapon, weaponState);
            PlayDrawSound(weaponState);

            var prefix = AnimationService.GetWeaponAnimationPrefix(weaponState);
            // Running, the draw replaces the run on its layer (t_1h_2_1hRun); walking or standing it plays over it.
            var animationName = (VmGothicEnums.WalkMode)Vob.AiHuman.WalkMode == VmGothicEnums.WalkMode.Run
                ? $"t_{prefix}_2_{prefix}Run"
                : $"t_Move_2_{prefix}Move";

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
            // Like the engine (OpenGothic MdlVisual::updateWeaponSkeleton): a bow is held in the left hand (the right
            // one draws the arrow), a crossbow like every other weapon in the right hand - its animations hold the
            // stock there.
            return weaponState == VmGothicEnums.WeaponState.Bow ? Constants.SlotLeftHand : Constants.SlotRightHand;
        }

        private void PlayDrawSound(VmGothicEnums.WeaponState weaponState)
        {
            if (weaponState is VmGothicEnums.WeaponState.NoWeapon or VmGothicEnums.WeaponState.Fist or
                VmGothicEnums.WeaponState.Mage)
                return;

            var soundName = weaponState.IsRanged() ? DaedalusConst.SoundDrawWood : DaedalusConst.SoundDrawMetal;
            var clip = _audioService.GetRandomSoundClip(soundName);
            if (clip != null && PrefabProps.NpcSound != null)
                PrefabProps.NpcSound.PlayOneShot(clip);
        }

        /// <summary>
        /// The overlay of the NPC's talent for this weapon (skill 1 or 2), null without the talent.
        /// </summary>
        [CanBeNull]
        private string GetFightOverlayName(VmGothicEnums.WeaponState weaponState)
        {
            (VmGothicEnums.Talent talent, string prefix) overlay = weaponState switch
            {
                VmGothicEnums.WeaponState.W1H => (VmGothicEnums.Talent._1H, "HUMANS_1HS"),
                VmGothicEnums.WeaponState.W2H => (VmGothicEnums.Talent._2H, "HUMANS_2HS"),
                VmGothicEnums.WeaponState.Bow => (VmGothicEnums.Talent.Bow, "HUMANS_BOW"),
                VmGothicEnums.WeaponState.CBow => (VmGothicEnums.Talent.Crossbow, "HUMANS_CBOW"),
                _ => (VmGothicEnums.Talent.Unknown, null)
            };
            // The stances exist for the human model only (orcs fight with their own).
            if (overlay.prefix == null || Props.MdsNameBase == null ||
                !Props.MdsNameBase.StartsWith("HUMANS", System.StringComparison.OrdinalIgnoreCase))
                return null;

            var skill = Vob.GetTalent((int)overlay.talent)?.Skill ?? 0;
            return skill > 0 ? $"{overlay.prefix}T{skill}.MDS" : null;
        }
    }
}
