using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Services.Npc;
using JetBrains.Annotations;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class DrawWeapon : AbstractAnimationAction
    {
        [Inject] private readonly AudioService _audioService;

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
            Logger.Log($"[DrawWeapon] {NpcGo.transform.parent?.name} weapon={(weapon == null ? "null" : $"idx={weapon.Index}")} equippedCount={Props.EquippedItems.Count} → fightMode={weaponState}", LogCat.Animation);
            Vob.FightMode = (int)weaponState;

            // Track which item is in hand so Attack.cs can use item.Range for attack range.
            Props.CurrentItem = weapon?.Index ?? -1;

            // Swap to the talent-level combat overlay (HUMANS_1HST1.MDS etc.) so attack/run animations
            // reflect the NPC's actual weapon skill. Routine overlay (Humans_Relaxed.mds) is restored on undraw.
            var combatOverlay = GetCombatOverlayName(weaponState);
            if (combatOverlay != null)
                Props.MdsNameOverlay = combatOverlay;

            MoveWeaponToHand(weapon, weaponState);
            PlayWeaponDrawSound(weaponState);

            var prefix = AnimationService.GetWeaponAnimationPrefix(weaponState);
            var walkMode = (VmGothicEnums.WalkMode)Vob.AiHuman.WalkMode;

            // Run mode: use the layer-1 animation (t_1h_2_1hRun) which naturally replaces the running
            // animation on the same layer. Walk mode: use the layer-2 walking draw (t_Move_2_1hMove).
            var animationName = walkMode == VmGothicEnums.WalkMode.Run
                ? $"t_{prefix}_2_{prefix}Run"
                : $"t_Move_2_{prefix}Move";

            Logger.Log($"[DrawWeapon] walkMode={walkMode} → playing '{animationName}'", LogCat.Animation);

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

        private void PlayWeaponDrawSound(VmGothicEnums.WeaponState weaponState)
        {
            if (weaponState == VmGothicEnums.WeaponState.Fist || weaponState == VmGothicEnums.WeaponState.NoWeapon)
                return;

            var soundName = (weaponState == VmGothicEnums.WeaponState.Bow || weaponState == VmGothicEnums.WeaponState.CBow)
                ? DaedalusConst.SoundDrawWood
                : DaedalusConst.SoundDrawMetal;

            var clip = _audioService.GetRandomSoundClip(soundName);
            if (clip != null)
                PrefabProps.NpcSound.PlayOneShot(clip);
        }

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

        [CanBeNull]
        private string GetCombatOverlayName(VmGothicEnums.WeaponState weaponState)
        {
            int talentIndex;
            string prefix;
            switch (weaponState)
            {
                case VmGothicEnums.WeaponState.W1H:   talentIndex = (int)VmGothicEnums.Talent._1H;        prefix = "HUMANS_1HS"; break;
                case VmGothicEnums.WeaponState.W2H:   talentIndex = (int)VmGothicEnums.Talent._2H;        prefix = "HUMANS_2HS"; break;
                case VmGothicEnums.WeaponState.Bow:   talentIndex = (int)VmGothicEnums.Talent.Bow;        prefix = "HUMANS_BOW"; break;
                case VmGothicEnums.WeaponState.CBow:  talentIndex = (int)VmGothicEnums.Talent.Crossbow;   prefix = "HUMANS_CBOW"; break;
                default: return null;
            }

            var skill = Vob.GetTalent(talentIndex)?.Skill ?? 0;
            return skill > 0 ? $"{prefix}T{skill}.MDS" : null;
        }
    }
}
