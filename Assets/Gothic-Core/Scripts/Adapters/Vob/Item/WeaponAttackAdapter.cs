using System;
using Gothic.Core.Const;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Models.Container;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Adapters.Vob.Item
{
    /// <summary>
    /// The validity check for a hit requires answering: "Is this attack currently active?"
    /// That state lives on the attacker (DEF_OPT_FRAME window, DEF_HIT_LIMB, "already connected" flag).
    /// If we put the logic on the receiver, we must reach across to the attacker's component to get that state
    /// every time any contact happens. If we put it on the attacker, all required state is already local.
    /// </summary>
    public class WeaponAttackAdapter : MonoBehaviour
    {
        private VobContainer _weaponVobContainer;
        private NpcContainer _targetNpcContainer;

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
            if (other.gameObject.layer != Constants.VobHitbox)
            {
                Debug.LogWarning($"[WeaponAttackAdapter] Wrong layer: {LayerMask.LayerToName(other.gameObject.layer)}");
                return;
            }

            Debug.Log($"[WeaponAttackAdapter] Collision with VobHitbox: {other.gameObject.name}");

            // Try to get the target NPC/Monster from the hitbox
            var targetNpcLoader = other.GetComponentInParent<NpcLoader>();
            if (targetNpcLoader == null)
            {
                Debug.LogWarning($"[WeaponAttackAdapter] No NpcLoader found");
                return;
            }

            var targetNpcContainer = targetNpcLoader.Container;
            if (targetNpcContainer == null)
            {
                Debug.LogWarning($"[WeaponAttackAdapter] No NpcContainer found");
                return;
            }

            Debug.Log($"[WeaponAttackAdapter] Target: {targetNpcContainer.Instance.GetName(NpcNameSlot.Slot0)}");

            // Try to fire the hit through VR weapon service if available
            if (TryFireHitViaVRWeaponService(targetNpcContainer))
            {
                Debug.Log($"[WeaponAttackAdapter] *** HIT FIRED (VR)");
                return;
            }

            Debug.LogWarning($"[WeaponAttackAdapter] Failed to fire hit (not in attack window or VR unavailable)");
            // TODO - Add support for flat-screen weapon hits and NPC-to-NPC hits here
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer != Constants.VobHitbox)
                return;
        }

        /// <summary>
        /// Attempts to validate and fire a hit using VRWeaponService (VR mode only).
        /// Returns true if the hit was processed, false otherwise.
        /// </summary>
        private bool TryFireHitViaVRWeaponService(NpcContainer targetNpcContainer)
        {
            try
            {
                // Try to dynamically get the VRWeaponService through the Reflex DI container
                // Using reflection to avoid direct assembly dependency on Gothic.VR
                var vrWeaponServiceType = Type.GetType("Gothic.VR.Services.VRWeaponService, Gothic.VR");
                if (vrWeaponServiceType == null)
                {
                    Debug.LogWarning($"[WeaponAttackAdapter] VRWeaponService not found (flat-screen mode?)");
                    return false;
                }

                var vrWeaponService = ReflexProjectInstaller.DIContainer.Resolve(vrWeaponServiceType);
                if (vrWeaponService == null)
                {
                    Debug.LogWarning($"[WeaponAttackAdapter] VRWeaponService could not be resolved");
                    return false;
                }

                // Use reflection to call the methods
                var isInAttackWindowMethod = vrWeaponServiceType.GetMethod("IsWeaponInAttackWindow");
                var getWeaponOwnerMethod = vrWeaponServiceType.GetMethod("GetWeaponOwner");

                if (isInAttackWindowMethod == null || getWeaponOwnerMethod == null)
                {
                    Debug.LogWarning($"[WeaponAttackAdapter] Required methods not found on VRWeaponService");
                    return false;
                }

                // Check if this weapon is currently in an active attack window
                var isInAttackWindow = (bool)isInAttackWindowMethod.Invoke(vrWeaponService, new object[] { _weaponVobContainer });
                Debug.Log($"[WeaponAttackAdapter] IsInAttackWindow: {isInAttackWindow}");
                if (!isInAttackWindow)
                {
                    Debug.LogWarning($"[WeaponAttackAdapter] Weapon not in attack window");
                    return false;
                }

                // Get who is attacking with this weapon
                var attacker = (NpcContainer)getWeaponOwnerMethod.Invoke(vrWeaponService, new object[] { _weaponVobContainer });
                if (attacker == null)
                {
                    Debug.LogWarning($"[WeaponAttackAdapter] No attacker found for weapon");
                    return false;
                }

                // Get the hit position for effects/knockback later
                var hitPosition = transform.position;

                // Fire the combat event
                Debug.Log($"[WeaponAttackAdapter] FightHit event: {attacker.Instance.GetName(NpcNameSlot.Slot0)} → {targetNpcContainer.Instance.GetName(NpcNameSlot.Slot0)}");
                GlobalEventDispatcher.FightHit.Invoke(attacker, targetNpcContainer, hitPosition);
                return true;
            }
            catch (Exception ex)
            {
                // VRWeaponService not available or DI resolution failed
                Debug.LogError($"[WeaponAttackAdapter] Exception in TryFireHitViaVRWeaponService: {ex.Message}");
                return false;
            }
        }
    }
}
