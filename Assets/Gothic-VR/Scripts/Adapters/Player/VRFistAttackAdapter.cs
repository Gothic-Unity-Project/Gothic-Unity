#if GUZ_HVR_INSTALLED
using GUZ.Core.Const;
using UnityEngine;

namespace GUZ.Core.Adapters.Vob.Item
{
    /// <summary>
    /// Basically a WeaponAttackAdapter for HVR Hands. But some tweaks are needed to fake the object into being an oCVobItem to fight.
    /// </summary>
    public class VRFistAttackAdapter : WeaponAttackAdapter
    {
        private void Awake()
        {
            // TODO - Implement before enable...
            enabled = false;
        }

        /// <summary>
        /// HVR resets all hand sub-GOs to layer Hands. But we want to have our specific HandAttack detection to be another layer.
        /// </summary>
        private void Start()
        {
            gameObject.layer = Constants.VobItemLayer;
        }
    }
}
#endif
