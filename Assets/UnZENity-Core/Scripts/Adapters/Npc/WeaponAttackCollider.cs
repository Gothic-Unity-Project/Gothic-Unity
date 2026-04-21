using GUZ.Core.Adapters.Npc;
using GUZ.Core.Models.Container;
using GUZ.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.VR.Adapters.Npc
{
    [RequireComponent(typeof(BoxCollider))]
    public class WeaponAttackCollider : MonoBehaviour
    {
        // [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly AnimationService _animationService;
        [SerializeField] BoxCollider HitCollider;

        private NpcContainer _npcContainer;

        private void Start()
        {
            _npcContainer = GetComponentInParent<NpcLoader>().Container;
        }

        public void SetDimension(Bounds unityBounds)
        {
            HitCollider.center = unityBounds.center;
            HitCollider.size = unityBounds.size;
        }

        /// <summary>
        /// TODO - Need to be updated to support fist collider from monsters and player as well
        /// Is the other who's hitting me?:
        /// 1. A VobItem (aka weapon)
        /// 2. Is the attacker in attack window state
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            // if (other.gameObject.layer != Constants.VobItemLayer)
            //     return;
            //
            // var vobContainer = other.GetComponentInParent<VobLoader>()?.Container;
            //
            // if (!_vrWeaponService.IsWeaponInAttackWindow(vobContainer))
            //     return;
            //
            // var attacker = _vrWeaponService.GetWeaponOwner(vobContainer);
            // if (attacker == null)
            //     return;
            //
            // var hitPosition = other.ClosestPoint(transform.position);
            // GlobalEventDispatcher.FightHit.Invoke(attacker, _npcContainer, hitPosition);
        }
    }
}
