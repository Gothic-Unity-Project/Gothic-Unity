using Gothic.Core.Const;
using UnityEngine;

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
        /// <summary>
        /// TODO - Need to be updated to support fist collider from monsters and player as well
        /// Is the other who's hitting me?:
        /// 1. A VobItem (aka weapon)
        /// 2. Is the attacker in attack window state
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer != Constants.VobHitbox)
                return;

            Debug.Log("OnTriggerEnter - VobHitbox");
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

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer != Constants.VobHitbox)
                return;

            Debug.Log("OnTriggerExit - VobHitbox");
        }
    }
}
