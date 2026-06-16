using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Services.Npc;
using Logger = Gothic.Core.Logging.Logger;
using Reflex.Attributes;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class UndrawWeapon : AbstractAnimationAction
    {
        [Inject] private readonly AudioService _audioService;

        private VmGothicEnums.WeaponState _weaponState;
        private bool _weaponMoved;
        private float _weaponMoveTime;

        public UndrawWeapon(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            _weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;
            Logger.LogWarning($"[UndrawWeapon] {NpcGo.transform.parent?.name} fightMode={_weaponState}", LogCat.Fight);

            if (_weaponState == VmGothicEnums.WeaponState.NoWeapon)
            {
                IsFinishedFlag = true;
                return;
            }

            var prefix = AnimationService.GetWeaponAnimationPrefix(_weaponState);
            var walkMode = (VmGothicEnums.WalkMode)Vob.AiHuman.WalkMode;

            // Run mode: layer-1 sheath (t_1hRun_2_1h, DEF_UNDRAWSOUND at frame 5 of 20 = 25%).
            // Walk mode: layer-2 sheath (t_1hMove_2_Move, DEF_UNDRAWSOUND at frame 6 of 24 = 25%).
            // Both animations fire the weapon-stow event at ~25% of their duration.
            var animationName = walkMode == VmGothicEnums.WalkMode.Run
                ? $"t_{prefix}Run_2_{prefix}"
                : $"t_{prefix}Move_2_Move";

            Logger.Log($"[UndrawWeapon] walkMode={walkMode} → playing '{animationName}'", LogCat.Animation);

            if (!PrefabProps.AnimationSystem.PlayAnimation(animationName))
            {
                // No sheath animation — apply state directly.
                ApplyVisualSheath();
                ApplyStateSheath();
                IsFinishedFlag = true;
                return;
            }

            ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(animationName);
            // Weapon moves at 25% of the sheath animation — matches DEF_UNDRAWSOUND frame in HUMANS.MDS.
            _weaponMoveTime = ActionEndEventTime * 0.25f;
        }

        public override void Tick()
        {
            base.Tick();
            if (IsFinishedFlag) return;

            if (!_weaponMoved && ActionTime >= _weaponMoveTime)
            {
                _weaponMoved = true;
                Logger.Log($"[UndrawWeapon] frame-6 event — moving weapon to stow slot", LogCat.Fight);
                ApplyVisualSheath();
            }
        }

        protected override void AnimationEnd()
        {
            if (!_weaponMoved)
            {
                _weaponMoved = true;
                ApplyVisualSheath();
            }
            ApplyStateSheath();
            base.AnimationEnd();
        }

        // Reparent weapon mesh + play sound — happens at the DEF_UNDRAWSOUND frame (~25% through animation).
        private void ApplyVisualSheath()
        {
            if (_weaponState == VmGothicEnums.WeaponState.NoWeapon || _weaponState == VmGothicEnums.WeaponState.Fist)
                return;
            MoveWeaponBackToStowSlot(_weaponState);
            PlayWeaponSound(sheathing: true);
        }

        // Game-state changes — applied at end of animation so overlays and fight mode update cleanly.
        private void ApplyStateSheath()
        {
            if (_weaponState == VmGothicEnums.WeaponState.NoWeapon || _weaponState == VmGothicEnums.WeaponState.Fist)
                return;
            Vob.FightMode = (int)VmGothicEnums.WeaponState.NoWeapon;
            Props.MdsNameOverlay = Props.MdsNameRoutineOverlay;
            Props.CurrentItem = -1;
        }

        private void PlayWeaponSound(bool sheathing)
        {
            if (_weaponState == VmGothicEnums.WeaponState.Fist)
                return;

            var soundName = (_weaponState == VmGothicEnums.WeaponState.Bow || _weaponState == VmGothicEnums.WeaponState.CBow)
                ? (sheathing ? DaedalusConst.SoundUndrawWood : DaedalusConst.SoundDrawWood)
                : (sheathing ? DaedalusConst.SoundUndrawMetal : DaedalusConst.SoundDrawMetal);

            var clip = _audioService.GetRandomSoundClip(soundName);
            if (clip != null)
                PrefabProps.NpcSound.PlayOneShot(clip);
        }

        private void MoveWeaponBackToStowSlot(VmGothicEnums.WeaponState weaponState)
        {
            if (weaponState == VmGothicEnums.WeaponState.Fist)
                return;

            var handSlotName = DrawWeapon.GetHandSlotName(weaponState);
            var stowSlotName = DrawWeapon.GetStowSlotName(weaponState);
            var handGo = NpcGo.FindChildRecursively(handSlotName);
            var slotGo = NpcGo.FindChildRecursively(stowSlotName);

            if (handGo == null)
            {
                Logger.LogWarning($"[UndrawWeapon] {NpcGo.transform.parent?.name} handSlot '{handSlotName}' not found!", LogCat.Fight);
                return;
            }
            if (slotGo == null)
            {
                Logger.LogWarning($"[UndrawWeapon] {NpcGo.transform.parent?.name} stowSlot '{stowSlotName}' not found!", LogCat.Fight);
                return;
            }
            if (handGo.transform.childCount == 0)
            {
                Logger.LogWarning($"[UndrawWeapon] {NpcGo.transform.parent?.name} handSlot '{handSlotName}' has no children — weapon not in hand!", LogCat.Fight);
                return;
            }

            Logger.LogWarning($"[UndrawWeapon] {NpcGo.transform.parent?.name} moving weapon from '{handSlotName}' → '{stowSlotName}'", LogCat.Fight);
            handGo.transform.GetChild(0).gameObject.SetParent(slotGo, true, true);
        }

        /// <summary>
        /// Immediately reparent weapon mesh and update fight state without playing the sheath animation.
        /// Called when a state change discards the queue before UndrawWeapon can execute normally.
        /// </summary>
        public void SheathImmediately()
        {
            if (_weaponState == VmGothicEnums.WeaponState.NoWeapon || _weaponState == VmGothicEnums.WeaponState.Fist)
                return;

            Logger.LogWarning($"[UndrawWeapon] {NpcGo.transform.parent?.name} SheathImmediately fightMode={_weaponState}", LogCat.Fight);
            ApplyVisualSheath();
            ApplyStateSheath();
        }
    }
}
