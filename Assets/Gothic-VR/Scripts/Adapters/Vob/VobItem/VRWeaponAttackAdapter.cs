using Gothic.Core;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.VR.Domain.Player;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Components;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// The validity check for a hit requires answering: "Is this attack currently active?"
    /// That state lives on the attacker (DEF_OPT_FRAME window, DEF_HIT_LIMB, "already connected" flag).
    /// If we put the logic on the receiver, we must reach across to the attacker's component to get that state
    /// every time any contact happens. If we put it on the attacker, all required state is already local.
    /// </summary>
    public class VRWeaponAttackAdapter : MonoBehaviour
    {
        [Inject] private readonly VRWeaponService _vrWeaponService; 
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VmCacheService _vmCacheService;

        private VobContainer _weaponVobContainer;
        private NpcContainer _targetNpcContainer;
        private VrStabFinishDomain _stabFinish;

        private void Start()
        {
            // Get reference to this weapon's VobContainer if it exists
            var vobLoader = GetComponentInParent<VobLoader>();
            if (vobLoader != null)
            {
                _weaponVobContainer = vobLoader.Container;
            }
        }

        /// <summary>
        /// TODO - We need to handle multiple hitboxes on the same target (e.g. head vs body) and ensure we don't apply multiple hits from one attack.
        /// TODO - figure out how to do fists
        /// TODO - figure out how to do NPC-to-NPC hits (e.g. monster attacking hero or monster attacking monster)
        /// Handles collision between weapon and potential targets (NPCs/Monsters).
        /// Validates the hit and fires FightHit event if conditions are met.
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            // Hands, grabbables, the world - touching them is normal. A warning per touch flooded the log (lag).
            if (other.gameObject.layer != Constants.VobHitbox)
                return;

            Logger.Log($"[WeaponAttackAdapter] Collision with VobHitbox: {other.gameObject.name}", LogCat.Fight);

            // Try to get the target NPC/Monster from the hitbox
            var targetNpcLoader = other.GetComponentInParent<NpcLoader>();
            if (targetNpcLoader == null)
            {
                Logger.LogWarning($"[WeaponAttackAdapter] No NpcLoader found", LogCat.Fight);
                return;
            }

            var targetNpcContainer = targetNpcLoader.Container;
            if (targetNpcContainer == null)
            {
                Logger.LogWarning("[WeaponAttackAdapter] No NpcContainer found", LogCat.Fight);
                return;
            }

            Logger.Log($"[WeaponAttackAdapter] Target: {targetNpcContainer.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);

            // DeveloperConfig.EnableVrStabFinish: an unconscious NPC is finished off by a stab and a twist only.
            if (IsStabFinishOn() && NpcHelperService.IsKnockedOut(targetNpcContainer))
            {
                TryStabFinish(targetNpcContainer);
                return;
            }

            // Try to fire the hit through VR weapon service if available
            if (TryFireHitViaVRWeaponService(targetNpcContainer))
            {
                Logger.Log($"[WeaponAttackAdapter] *** HIT FIRED (VR)", LogCat.Fight);
                return;
            }

            Logger.LogWarning("[WeaponAttackAdapter] Failed to fire hit (not in attack window or VR unavailable)", LogCat.Fight);
            // TODO - Add support for flat-screen weapon hits and NPC-to-NPC hits here
        }

        /// <summary>
        /// DeveloperConfig.EnableVrStabFinish: the blade can already be inside the padded hitbox when the thrust comes.
        /// </summary>
        private void OnTriggerStay(Collider other)
        {
            if (other.gameObject.layer != Constants.VobHitbox || !IsStabFinishOn())
                return;

            var target = other.GetComponentInParent<NpcLoader>()?.Container;
            if (target != null && NpcHelperService.IsKnockedOut(target))
                TryStabFinish(target);
        }

        private void Update()
        {
            if (_stabFinish?.IsStuck == true)
                _stabFinish.Tick(GetPlayerVelocity());
        }

        private Vector3 GetPlayerVelocity()
        {
            return _vrPlayerService.VRContextInteractionService.Rig.CharacterController.velocity;
        }

        private bool IsStabFinishOn()
        {
            return _configService != null && _configService.Dev.EnableVrStabFinish &&
                   _configService.Gothic.GetInt(VRConstants.IniNames.StabFinish, 1) == 1;
        }

        private void TryStabFinish(NpcContainer target)
        {
            if (_vrWeaponService == null || _stabFinish?.IsStuck == true)
                return;

            var attacker = _vrWeaponService.GetWeaponOwner(_weaponVobContainer);
            if (attacker?.PrefabProps == null || !attacker.PrefabProps.IsHero())
                return;

            _stabFinish ??= new VrStabFinishDomain().Inject();
            _stabFinish.TryStab(attacker, target, GetComponent<VrInteractable>(), GetPlayerVelocity());
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer != Constants.VobHitbox)
                return;
        }

        private bool TryFireHitViaVRWeaponService(NpcContainer targetNpcContainer)
        {
            if (_vrWeaponService == null)
            {
                Logger.LogWarning("[WeaponAttackAdapter] VRWeaponService not injected (flat-screen mode?)", LogCat.Fight);
                return false;
            }

            var isInAttackWindow = _vrWeaponService.IsWeaponInAttackWindow(_weaponVobContainer);
            Logger.Log($"[WeaponAttackAdapter] IsInAttackWindow: {isInAttackWindow}", LogCat.Fight);
            if (!isInAttackWindow)
            {
                Logger.LogWarning("[WeaponAttackAdapter] Weapon not in attack window", LogCat.Fight);
                return false;
            }

            var attacker = _vrWeaponService.GetWeaponOwner(_weaponVobContainer);
            if (attacker == null)
            {
                Logger.LogWarning("[WeaponAttackAdapter] No attacker found for weapon", LogCat.Fight);
                return false;
            }

            Logger.Log($"[WeaponAttackAdapter] FightHit event: {attacker.Instance.GetName(NpcNameSlot.Slot0)} → {targetNpcContainer.Instance.GetName(NpcNameSlot.Slot0)}", LogCat.Fight);

            // DeveloperConfig.EnableEngineMeleeDamage: the weapon in the hand isn't equipped in VR - FightService takes
            // its damage from here (only during the hit).
            var weaponName = _weaponVobContainer?.Vob?.Name;
            attacker.Props.MeleeHitWeapon = weaponName != null ? _vmCacheService.TryGetItemData(weaponName) : null;
            try
            {
                GlobalEventDispatcher.FightHit.Invoke(attacker, targetNpcContainer, transform.position);
            }
            finally
            {
                attacker.Props.MeleeHitWeapon = null;
            }
            return true;
        }
    }
}
