#if GOTHIC_HVR_INSTALLED
using Gothic.Core;
using Gothic.Core.Adapters.Vob.Item;
using Gothic.Core.Const;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.VR.Domain.Player;
using Gothic.VR.Models.Vob;
using Gothic.Core.Extensions;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Utils;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
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
        [Inject] private NpcAiService _npcAiService;
        [Inject] private ConfigService _configService;

        private readonly VrWeaponAttackDomain _firstAttackDomain = new VrWeaponAttackDomain().Inject();
        private readonly VrWeaponAttackDomain _secondAttackDomain = new VrWeaponAttackDomain().Inject();

        private bool _isRuneReadied;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(OnHit);
            GlobalEventDispatcher.FightWindowAttack.AddListener(OnAttackWindowStart);
            GlobalEventDispatcher.FightWindowInitial.AddListener(OnAttackWindowEnd);
            GlobalEventDispatcher.HeroKnockedOut.AddListener(OnHeroKnockedOut);
        }

        /// <summary>
        /// MVP (DeveloperConfig.EnableHeroDropsWeaponsOnKnockout): vanilla drops the hero's readied weapon when knocked out.
        /// In VR we let the hands drop every melee weapon they hold. Release events handle the rest (attack domain, flags).
        /// </summary>
        private void OnHeroKnockedOut(NpcContainer hero)
        {
            if (!_configService.Dev.EnableHeroDropsWeaponsOnKnockout)
                return;

            foreach (var hand in Object.FindObjectsByType<HVRHandGrabber>(FindObjectsSortMode.None))
            {
                var grabbed = hand.GrabbedTarget;
                if (grabbed == null)
                    continue;

                var item = grabbed.GetComponentInParent<VobLoader>()?.Container?.GetItemInstance();
                if (item == null || item.MainFlag != (int)VmGothicEnums.ItemFlags.ItemKatNf)
                    continue;

                Logger.Log($"[VRWeaponService] Hero knocked out - dropping '{item.Name}' from {hand.HandSide} hand.", LogCat.VR);
                hand.ForceRelease();
            }
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

            UpdateHeroWeaponState();
        }

        public void OnReleased(HVRHandSide handSide, WeaponPhysicsConfig weaponConfig)
        {
            if (!_firstAttackDomain.TryUnHandle(weaponConfig, handSide))
            {
                // If we can't handle with the first handler, then it's a second weapon released from another hand.
                _secondAttackDomain.TryUnHandle(weaponConfig, handSide);
            }

            UpdateHeroWeaponState();
        }

        /// <summary>
        /// Called by VRRuneCaster (rune dual-grabbed / released). Can't be derived from hero.ActiveSpell,
        /// as spell ID 0 (SPL_LIGHT) is the same as "no spell".
        /// </summary>
        public void SetRuneReadied(bool isReadied)
        {
            _isRuneReadied = isReadied;
            UpdateHeroWeaponState();
        }

        /// <summary>
        /// Gothic knows only one readied weapon at a time. VR hands can hold several, so we pick:
        /// melee weapon in any hand > readied rune (VRRuneCaster sets hero.ActiveSpell) > nothing.
        /// NPCs react to the result via PERC_DRAWWEAPON/PERC_ASSESSFIGHTER/PERC_ASSESSREMOVEWEAPON.
        /// </summary>
        public void UpdateHeroWeaponState()
        {
            var state = _firstAttackDomain.GetWeaponState();
            if (state == VmGothicEnums.WeaponState.NoWeapon)
                state = _secondAttackDomain.GetWeaponState();
            if (state == VmGothicEnums.WeaponState.NoWeapon && _isRuneReadied)
                state = VmGothicEnums.WeaponState.Mage;
            // TODO - Bows/crossbows (WeaponState.Bow/CBow) once VR ranged combat exists.

            _npcAiService.ExtSetHeroWeaponState(state);
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

            // Go can already be destroyed (Unity null). Throwing here would abort HVR's release chain.
            if (weapon?.Go == null)
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
