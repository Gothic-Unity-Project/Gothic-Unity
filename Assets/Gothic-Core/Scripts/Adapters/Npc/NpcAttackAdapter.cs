using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using MyBox;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// Attached to NPC/Monster root collider to allow them to deal melee damage via unarmed attacks.
    /// Fires FightHit events when the NPC's collider hits an opponent during attack animations.
    /// </summary>
    public class NpcAttackAdapter : MonoBehaviour
    {
        private NpcContainer _npcContainer;
        private BoxCollider _collider;
        private float _attackHitCooldown;

        private void Start()
        {
            var npcLoader = GetComponentInParent<NpcLoader>();
            if (npcLoader != null)
            {
                _npcContainer = npcLoader.Container;
            }

            _collider = GetComponent<BoxCollider>();
        }

        private void Update()
        {
            // Reduce cooldown timer each frame
            if (_attackHitCooldown > 0)
            {
                _attackHitCooldown -= Time.deltaTime;
            }
        }

        /// <summary>
        /// Called when the NPC's collider hits something during combat.
        /// Checks if the NPC is actively attacking and if the target is valid.
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            // Can't hit without a valid container
            if (_npcContainer == null)
            {
                Debug.LogWarning($"[NpcAttackAdapter] No NPC container");
                return;
            }

            // Check if this is within attack hit cooldown to prevent multiple hits per attack
            if (_attackHitCooldown > 0)
            {
                Debug.LogWarning($"[NpcAttackAdapter] Hit in cooldown (remaining: {_attackHitCooldown:F2}s)");
                return;
            }

            // The target must have a hitbox layer
            if (other.gameObject.layer != Constants.VobHitbox)
            {
                Debug.LogWarning($"[NpcAttackAdapter] Wrong layer: {LayerMask.LayerToName(other.gameObject.layer)}");
                return;
            }

            Debug.Log($"[NpcAttackAdapter] Collision with hitbox layer: {other.gameObject.name}");

            // Try to get the target NPC/Player
            var targetNpcLoader = other.GetComponentInParent<NpcLoader>();
            if (targetNpcLoader == null)
            {
                Debug.LogWarning($"[NpcAttackAdapter] No target NpcLoader found");
                return;
            }

            var targetNpcContainer = targetNpcLoader.Container;
            if (targetNpcContainer == null)
            {
                Debug.LogWarning($"[NpcAttackAdapter] No target NpcContainer found");
                return;
            }

            Debug.Log($"[NpcAttackAdapter] Target NPC: {targetNpcContainer.Instance.GetName(NpcNameSlot.Slot0)}");

            // Don't let NPCs hit themselves
            if (targetNpcContainer == _npcContainer)
            {
                Debug.LogWarning($"[NpcAttackAdapter] Target is self, ignoring");
                return;
            }

            Debug.Log($"[NpcAttackAdapter] Checking if {_npcContainer.Instance.GetName(NpcNameSlot.Slot0)} is attacking...");

            // Check if the NPC is currently attacking
            if (!IsNpcCurrentlyAttacking())
            {
                Debug.LogWarning($"[NpcAttackAdapter] Not in attack state");
                return;
            }

            // Fire the hit event
            var hitPosition = transform.position;
            Debug.Log($"[NpcAttackAdapter] *** HIT FIRED! {_npcContainer.Instance.GetName(NpcNameSlot.Slot0)} → {targetNpcContainer.Instance.GetName(NpcNameSlot.Slot0)} at {hitPosition}");
            GlobalEventDispatcher.FightHit.Invoke(_npcContainer, targetNpcContainer, hitPosition);

            // Set cooldown to prevent multiple hits in rapid succession (0.5 seconds)
            _attackHitCooldown = 0.5f;
        }

        /// <summary>
        /// Determines if the NPC is currently in an attack animation.
        /// Checks if the current animation action is an attack-related action.
        /// </summary>
        private bool IsNpcCurrentlyAttacking()
        {
            if (_npcContainer?.Props?.CurrentAction == null)
            {
                Debug.LogWarning($"[NpcAttackAdapter] No CurrentAction");
                return false;
            }

            // Check if the current action is an attack-based action
            // AttackPlayAni is used for all NPC melee attacks
            var currentAction = _npcContainer.Props.CurrentAction;
            var actionTypeName = currentAction.GetType().Name;

            Debug.Log($"[NpcAttackAdapter] CurrentAction type: {actionTypeName}");

            // Direct check for AttackPlayAni type (preferred, most reliable)
            if (actionTypeName == "AttackPlayAni")
            {
                Debug.Log($"[NpcAttackAdapter] IsAttacking=TRUE (AttackPlayAni)");
                return true;
            }

            // Also support PlayAni with attack animations by checking the animation name
            if (actionTypeName == "PlayAni")
            {
                // PlayAni stores animation name in Action.String0
                // Animation names for attacks typically contain "attack" or follow pattern like "s_1hAttack", "s_2hAttack"
                if (currentAction.Action?.String0 != null)
                {
                    var aniName = currentAction.Action.String0.ToLower();
                    Debug.Log($"[NpcAttackAdapter] PlayAni animation: {aniName}");
                    var isAttack = aniName.Contains("attack");
                    Debug.Log($"[NpcAttackAdapter] IsAttacking={isAttack}");
                    return isAttack;
                }
                else
                {
                    Debug.LogWarning($"[NpcAttackAdapter] PlayAni but no animation name");
                }
            }

            Debug.LogWarning($"[NpcAttackAdapter] IsAttacking=FALSE (type: {actionTypeName})");
            return false;
        }
    }
}
