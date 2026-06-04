#if GOTHIC_HVR_INSTALLED
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR
{
    /// <summary>
    /// Each Service can be added to leverage Unity lifecycle events.
    /// This ensures a central overview of usage.
    /// </summary>
    public class VRUnityLifecycleEvents : MonoBehaviour
    {
        [Inject] private readonly VRWeaponService _vrWeaponService;

        private void FixedUpdate()
        {
            _vrWeaponService.FixedUpdate();
        }
    }
}
#endif
