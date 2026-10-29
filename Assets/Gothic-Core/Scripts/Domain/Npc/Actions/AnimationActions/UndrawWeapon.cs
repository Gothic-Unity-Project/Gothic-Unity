using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class UndrawWeapon : AbstractAnimationAction
    {
        // The weapon goes back at 25% of the sheath animation - its DEF_UNDRAWSOUND frame in HUMANS.MDS
        // (t_1hRun_2_1h: frame 5 of 20, t_1hMove_2_Move: frame 6 of 24).
        private const float _weaponMoveFraction = 0.25f;

        [Inject] private readonly AudioService _audioService;

        private VmGothicEnums.WeaponState _weaponState;
        private bool _isWeaponMoved;
        private float _weaponMoveTime;

        public UndrawWeapon(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            _weaponState = (VmGothicEnums.WeaponState)Vob.FightMode;

            if (_weaponState == VmGothicEnums.WeaponState.NoWeapon)
            {
                IsFinishedFlag = true;
                return;
            }

            var prefix = AnimationService.GetWeaponAnimationPrefix(_weaponState);
            // Running, the sheath replaces the run on its layer; walking or standing it plays over it.
            var animationName = (VmGothicEnums.WalkMode)Vob.AiHuman.WalkMode == VmGothicEnums.WalkMode.Run
                ? $"t_{prefix}Run_2_{prefix}"
                : $"t_{prefix}Move_2_Move";

            if (!PrefabProps.AnimationSystem.PlayAnimation(animationName))
            {
                SheathVisually();
                SheathState();
                IsFinishedFlag = true;
                return;
            }

            ActionEndEventTime = PrefabProps.AnimationSystem.GetAnimationDuration(animationName);
            _weaponMoveTime = ActionEndEventTime * _weaponMoveFraction;
        }

        public override void Tick()
        {
            base.Tick();
            if (IsFinishedFlag)
                return;

            if (!_isWeaponMoved && ActionTime >= _weaponMoveTime)
                SheathVisually();
        }

        protected override void AnimationEnd()
        {
            SheathVisually();
            // The fight mode and stance change once the animation is over, so it isn't cut by the new animation set.
            SheathState();
            base.AnimationEnd();
        }

        public override void StopImmediately()
        {
            // Cut short, the weapon still goes away - else the NPC walks on with the fight animations.
            if (_weaponState != VmGothicEnums.WeaponState.NoWeapon && !IsFinishedFlag)
            {
                SheathVisually();
                SheathState();
            }
            base.StopImmediately();
        }

        /// <summary>
        /// The weapon back on the belt/back, with its sound.
        /// </summary>
        private void SheathVisually()
        {
            if (_isWeaponMoved)
                return;

            _isWeaponMoved = true;
            if (_weaponState is VmGothicEnums.WeaponState.Fist or VmGothicEnums.WeaponState.Mage)
                return;

            MoveWeaponBackToStowSlot(_weaponState);

            var soundName = _weaponState.IsRanged() ? DaedalusConst.SoundUndrawWood : DaedalusConst.SoundUndrawMetal;
            var clip = _audioService.GetRandomSoundClip(soundName);
            if (clip != null && PrefabProps.NpcSound != null)
                PrefabProps.NpcSound.PlayOneShot(clip);
        }

        private void SheathState()
        {
            // From here on, follow-up animations are weaponless again (s_RunL, ...) and the routine's stance is back.
            Vob.FightMode = (int)VmGothicEnums.WeaponState.NoWeapon;
            Props.MdsNameOverlay = NpcContainer.GetScriptOverlay();
            Props.CurrentItem = -1;
        }

        private void MoveWeaponBackToStowSlot(VmGothicEnums.WeaponState weaponState)
        {
            var handGo = NpcGo.FindChildRecursively(DrawWeapon.GetHandSlotName(weaponState));
            var slotGo = NpcGo.FindChildRecursively(DrawWeapon.GetStowSlotName(weaponState));

            if (handGo == null || slotGo == null || handGo.transform.childCount == 0)
                return;

            handGo.transform.GetChild(0).gameObject.SetParent(slotGo, true, true);
        }
    }
}
