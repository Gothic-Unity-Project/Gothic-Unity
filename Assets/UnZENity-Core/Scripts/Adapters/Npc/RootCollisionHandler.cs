using GUZ.Core.Const;
using UnityEngine;

namespace GUZ.Core.Adapters.Npc
{
    [RequireComponent(typeof(CapsuleCollider))]
    public class RootCollisionHandler : BasePlayerBehaviour
    {
        [SerializeField] private CapsuleCollider _walkCollider;

        private SkinnedMeshRenderer[] _meshRenderers;
        
        private const float _pushbackDistance = 0.3f;
        
        
        protected override void Awake()
        {
            base.Awake();

            // Cached object which will be used later.
            NpcData.PrefabProps.ColliderRootMotion = gameObject.transform;
        }

        /// <summary>
        /// We need to apply physics on the NPC itself.
        /// General movement and animations are handled within AnimationSystem.cs. This Collider object is to add physics on top.
        /// </summary>
        private void Update()
        {
            if (_meshRenderers == null)
                _meshRenderers = Go.GetComponentsInChildren<SkinnedMeshRenderer>();

            var bbox = new Bounds();

            foreach (var rend in _meshRenderers)
                bbox.Encapsulate(rend.localBounds);

            /*
             * NPC GO hierarchy:
             *
             * root
             *  /BIP01/ <- animation root
             *    /RootCollisionHandler <- Moved with animation as inside BIP01, but physics are applied and merged to root
             *    /... <- animation bones
             */

            // Apply physics based position change to root.
            Go.transform.localPosition += transform.localPosition;

            // Empty physics based diff. Next frame physics will be recalculated.
            transform.localPosition = Vector3.zero;
        }

        private void OnCollisionEnter(Collision collision)
        {
            // As we already use these layers for Monsters+NPCs+Hero, we will simply reuse it instead of using a Tag.
            // PERC_MOVENPC is only relevant for hero + NPCs in G1.
            if (collision.gameObject.layer.Equals(Constants.PlayerLayer))
            {
                PrefabProps.AiHandler.HeroCollisionDetected();
                
                PushMonsterAwayFromPlayer();
            }
            else
            {
                // Nothing relevant collided with.
                return;
            }
        }

        /// <summary>
        /// Pushes the monster away from the player when collision occurs in VR.
        /// Prevents the monster from glitching through and ending up below the player.
        /// Alternatives would be:
        /// 1. In G1 itself, the hit animation of the player is slightly moving him backward.
        /// 2. But in VR, this could cause nausea.
        ///
        /// TODO - This will also cause monsters to spawn away from us, when we move into them.
        ///        Let's see if this causes glitches in a normal VR gameplay session.
        /// </summary>
        private void PushMonsterAwayFromPlayer()
        {
            var playerPosition = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            var monsterPosition = Go.transform.position;

            // Calculate the pushback direction (away from player, on horizontal plane only)
            var directionAwayFromPlayer = new Vector3(
                monsterPosition.x - playerPosition.x,
                0f, // Don't push up/down
                monsterPosition.z - playerPosition.z
            ).normalized;

            // Push monster back by a small amount (30cm)
            var pushbackOffset = directionAwayFromPlayer * _pushbackDistance;

            // Apply the pushback to the root position
            Go.transform.position += pushbackOffset;
        }
    }
}
