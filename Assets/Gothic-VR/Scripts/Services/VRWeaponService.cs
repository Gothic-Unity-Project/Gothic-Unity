using Gothic.Core;
using ZenKit.Daedalus;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Services;
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
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Models.Vob;
using Gothic.Core.Extensions;
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
        [Inject] private NpcHelperService _npcHelperService;
        [Inject] private ConfigService _configService;
        [Inject] private IVrSfxPlayer _vrSfxPlayer;
        [Inject] private VRPlayerService _vrPlayerService;

        private readonly VrWeaponAttackDomain _firstAttackDomain = new VrWeaponAttackDomain().Inject();
        private readonly VrWeaponAttackDomain _secondAttackDomain = new VrWeaponAttackDomain().Inject();

        // The hero's stab finish (one stuck blade at a time).
        private VrStabFinishDomain _stabFinish;

        // Fists. Created on first use, like the stab finish (its services live in the VR scene scope).
        private VrFistDomain _fistDomain;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(OnHit);
            GlobalEventDispatcher.FightWindowAttack.AddListener(OnAttackWindowStart);
            GlobalEventDispatcher.FightWindowInitial.AddListener(OnAttackWindowEnd);
            GlobalEventDispatcher.HeroKnockedOut.AddListener(OnHeroKnockedOut);
        }

        /// <summary>
        /// MVP: vanilla drops the hero's readied weapon when knocked out.
        /// In VR we let the hands drop every melee weapon they hold. Release events handle the rest (attack domain, flags).
        /// </summary>
        private void OnHeroKnockedOut(NpcContainer hero)
        {
            foreach (var hand in _vrPlayerService.GetHandGrabbers())
            {
                var grabbed = hand.Held;
                if (grabbed == null)
                    continue;

                var container = grabbed.GetComponentInParent<VobLoader>()?.Container;
                if (container == null || !container.IsMeleeWeapon())
                    continue;
                var item = container.GetItemInstance()!;

                Logger.Log($"[VRWeaponService] Hero knocked out - dropping '{item.Name}' from {hand.HandSide} hand.", LogCat.VR);
                hand.ForceRelease();
            }
        }

        public void FixedUpdate()
        {
            _firstAttackDomain.FixedUpdate();
            _secondAttackDomain.FixedUpdate();
            _fistDomain ??= new VrFistDomain().Inject();
            if (_fistDomain.Tick())
                UpdateHeroWeaponState();
            if (_stabFinish?.IsStuck == true)
                _stabFinish.Tick();
        }

        /// <summary>
        /// The "stabFinish" option (Immersion menu): an unconscious NPC is finished off by a stab
        /// kept in the body only (VrStabFinishDomain).
        /// </summary>
        public bool IsStabFinishOn()
        {
            return _configService.Gothic.GetInt(VRConstants.IniNames.StabFinish, 1) == 1;
        }

        /// <summary>
        /// The hero's held weapon entered an unconscious NPC: a stab along the blade sticks it in.
        /// </summary>
        public void TryStabFinish(VobContainer weapon, NpcContainer target, VrInteractable interactable)
        {
            if (_stabFinish?.IsStuck == true)
                return;

            var attacker = GetWeaponOwner(weapon);
            if (attacker?.PrefabProps == null || !attacker.PrefabProps.IsHero())
                return;

            var playerVelocity = _vrPlayerService.Rig?.CharacterController?.velocity ?? Vector3.zero;
            // Created on first use, like the scene's other VR domains (its services live in the VR scene scope).
            _stabFinish ??= new VrStabFinishDomain().Inject();
            _stabFinish.TryStab(attacker, target, interactable, playerVelocity);
        }

        public void OnGrabbed(VrHandSide handSide, VobContainer vobContainer, WeaponPhysicsConfig weaponConfig)
        {
            var heroContainer = _playerService.HeroContainer;

            if (!_firstAttackDomain.TryHandle(vobContainer, weaponConfig, handSide, heroContainer))
            {
                // If we can't handle with the first handler, then it's a second weapon grabbed with another hand.
                _secondAttackDomain.TryHandle(vobContainer, weaponConfig, handSide, heroContainer);
            }

            UpdateHeroWeaponState();
        }

        public void OnReleased(VrHandSide handSide, WeaponPhysicsConfig weaponConfig)
        {
            if (!_firstAttackDomain.TryUnHandle(weaponConfig, handSide))
            {
                // If we can't handle with the first handler, then it's a second weapon released from another hand.
                _secondAttackDomain.TryUnHandle(weaponConfig, handSide);
            }

            UpdateHeroWeaponState();
        }

        /// <summary>
        /// Gothic knows only one readied weapon at a time. VR hands can hold several, so we pick:
        /// melee weapon in any hand > fist > nothing (bows and runes join with their PRs).
        /// NPCs react to the result via PERC_DRAWWEAPON/PERC_ASSESSFIGHTER/PERC_ASSESSREMOVEWEAPON.
        /// </summary>
        public void UpdateHeroWeaponState()
        {
            var state = _firstAttackDomain.GetWeaponState();
            if (state == VmGothicEnums.WeaponState.NoWeapon)
                state = _secondAttackDomain.GetWeaponState();
            if (state == VmGothicEnums.WeaponState.NoWeapon && _fistDomain?.IsReadied == true)
                state = VmGothicEnums.WeaponState.Fist;

            // Which melee weapon is readied - Npc_GetReadiedWeapon (orcs tolerate the Ulu-Mulu in the hero's hand).
            var meleeWeapon = _firstAttackDomain.WeaponVobContainer ?? _secondAttackDomain.WeaponVobContainer;
            _npcHelperService.HeroReadiedMeleeWeapon = meleeWeapon?.GetItemInstance();

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
                     _vrSfxPlayer.Play(clipMetal, weapon.Go.transform.position);
                     break;
                 case VmGothicEnums.ItemMaterial.Wood:
                     var clipWood = _audioService.CreateAudioClip(DaedalusConst.SoundDrawWood);
                     _vrSfxPlayer.Play(clipWood, weapon.Go.transform.position);
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
                    _vrSfxPlayer.Play(clipMetal, weapon.Go.transform.position);
                    break;
                case VmGothicEnums.ItemMaterial.Wood:
                    var clipWood = _audioService.CreateAudioClip(DaedalusConst.SoundUndrawWood);
                    _vrSfxPlayer.Play(clipWood, weapon.Go.transform.position);
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
