#if GUZ_HVR_INSTALLED
using GUZ.Core;
using GUZ.Core.Adapters.Vob.Item;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Manager;
using GUZ.Core.Models.Container;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services.Player;
using GUZ.VR.Domain.Player;
using GUZ.VR.Models.Vob;
using HurricaneVR.Framework.Core.Utils;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.VR.Services
{
    /// <summary>
    /// Logic goes like this:
    /// * At first, our logic is executed in _firstAttackDomain. Either one handed or two handed.
    /// * If we grab another weapon which is not handled by the first Domain already, then let's handle it by the _second one.
    /// </summary>
    public class VRWeaponService
    {
        /// Disable sounds when Backpack is currently being refilled.
        public bool DrawSoundsActive = true;

        [Inject] private AudioService _audioService;
        [Inject] private PlayerService _playerService;

        private readonly VrWeaponAttackDomain _firstAttackDomain = new VrWeaponAttackDomain().Inject();
        private readonly VrWeaponAttackDomain _secondAttackDomain = new VrWeaponAttackDomain().Inject();

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(OnHit);
            GlobalEventDispatcher.FightWindowAttack.AddListener(OnAttackWindowStart);
            GlobalEventDispatcher.FightWindowInitial.AddListener(OnAttackWindowEnd);
        }

        public void FixedUpdate()
        {
            _firstAttackDomain.FixedUpdate();
            _secondAttackDomain.FixedUpdate();
        }

        public void OnGrabbed(HVRHandSide handSide, VobContainer vobContainer, WeaponPhysicsConfig weaponConfig)
        {
            var heroContainer = _playerService.HeroContainer;

            if (!_firstAttackDomain.TryHandle(vobContainer, weaponConfig, handSide, heroContainer))
            {
                // If we can't handle with the first handler, then it's a second weapon grabbed with another hand.
                _secondAttackDomain.TryHandle(vobContainer, weaponConfig, handSide, heroContainer);
            }
        }

        public void OnReleased(HVRHandSide handSide, WeaponPhysicsConfig weaponConfig)
        {
            if (!_firstAttackDomain.TryUnHandle(weaponConfig, handSide))
            {
                // If we can't handle with the first handler, then it's a second weapon released from another hand.
                _secondAttackDomain.TryUnHandle(weaponConfig, handSide);
            }
        }

        public void PlayDrawSound(VobContainer weapon)
        {
            if (!DrawSoundsActive)
                return;

            switch ((VmGothicEnums.ItemMaterial)weapon.GetItemInstance()!.Material)
            {
                 case VmGothicEnums.ItemMaterial.Metal:
                     var clipMetal = _audioService.CreateAudioClip(DaedalusConst.SoundDrawMetal);
                     SFXPlayer.Instance.PlaySFX(clipMetal, weapon.Go.transform.position);
                     break;
                 case VmGothicEnums.ItemMaterial.Wood:
                     var clipWood = _audioService.CreateAudioClip(DaedalusConst.SoundDrawWood);
                     SFXPlayer.Instance.PlaySFX(clipWood, weapon.Go.transform.position);
                     break;
                 // All others will be ignored as they're e.g., a bow.
                 default:
                     break;
            }
        }

        public void PlayUndrawSound(VobContainer weapon)
        {
            if (!DrawSoundsActive)
                return;

            switch ((VmGothicEnums.ItemMaterial)weapon.GetItemInstance()!.Material)
            {
                case VmGothicEnums.ItemMaterial.Metal:
                    var clipMetal = _audioService.CreateAudioClip(DaedalusConst.SoundUndrawMetal);
                    SFXPlayer.Instance.PlaySFX(clipMetal, weapon.Go.transform.position);
                    break;
                case VmGothicEnums.ItemMaterial.Wood:
                    var clipWood = _audioService.CreateAudioClip(DaedalusConst.SoundUndrawWood);
                    SFXPlayer.Instance.PlaySFX(clipWood, weapon.Go.transform.position);
                    break;
                // All others will be ignored as they're e.g., a bow.
                default:
                    break;
            }
        }

        public bool IsWeaponInAttackWindow(VobContainer vobContainer)
        {
            if (vobContainer == _firstAttackDomain.WeaponVobContainer)
                return _firstAttackDomain.IsInAttackState();
            else if (vobContainer == _secondAttackDomain.WeaponVobContainer)
                return _secondAttackDomain.IsInAttackState();
            else
                return false;
        }

        /// <summary>
        /// Returns the NpcContainer of the player who owns the weapon, or null if not found.
        /// </summary>
        public NpcContainer GetWeaponOwner(VobContainer vobContainer)
        {
            if (vobContainer == _firstAttackDomain.WeaponVobContainer)
                return _firstAttackDomain.GetOwner();
            else if (vobContainer == _secondAttackDomain.WeaponVobContainer)
                return _secondAttackDomain.GetOwner();
            else
                return null;
        }

        /// <summary>
        /// Returns the HandSide for a combatant's weapon, for haptics feedback.
        /// Returns None if the combatant is not the owner of any active weapon domain.
        /// </summary>
        public GlobalEventDispatcher.HandSide GetHandSideForCombatant(NpcContainer combatant)
        {
            if (_firstAttackDomain.GetOwner() == combatant)
                return _firstAttackDomain.GetHandSide();
            else if (_secondAttackDomain.GetOwner() == combatant)
                return _secondAttackDomain.GetHandSide();
            else
                return GlobalEventDispatcher.HandSide.None;
        }

        private void OnAttackWindowStart(NpcContainer combatant)
        {
            ChangeWeaponTrail(combatant, true);
        }

        private void OnAttackWindowEnd(NpcContainer combatant)
        {
            ChangeWeaponTrail(combatant, false);
        }

        private void ChangeWeaponTrail(NpcContainer combatant, bool enable)
        {
            VobContainer weapon = null;
            if (_firstAttackDomain.GetOwner() == combatant)
                weapon = _firstAttackDomain.WeaponVobContainer;
            else if (_secondAttackDomain.GetOwner() == combatant)
                weapon = _secondAttackDomain.WeaponVobContainer;

            if (weapon == null)
                return;

            var weaponAdapter = weapon.Go.GetComponentInChildren<WeaponAdapter>();
            if (weaponAdapter == null)
                return;

            if (enable)
                weaponAdapter.StartTrail();
            else
                weaponAdapter.EndTrail();
        }

        private void OnHit(NpcContainer _, NpcContainer __, Vector3 ___)
        {
            // Find which domain's weapon caused this hit and advance its state.
            // We check based on the attacker being the owner of one of our domains.
            if (_firstAttackDomain.GetOwner() != null && _firstAttackDomain.IsInAttackState())
                _firstAttackDomain.AdvanceStateAfterAttack();
            else if (_secondAttackDomain.GetOwner() != null && _secondAttackDomain.IsInAttackState())
                _secondAttackDomain.AdvanceStateAfterAttack();
        }
    }
}
#endif
