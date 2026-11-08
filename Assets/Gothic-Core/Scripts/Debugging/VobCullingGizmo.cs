using UnityEngine;

namespace Gothic.Core.Debugging
{
    public class VobCullingGizmo : MonoBehaviour
    {
        [Tooltip("Hint: Only works if DeveloperConfig.ShowVOBMeshCullingGizmos is also activated.")]
        public bool ActivateGizmo = true;
    }
}
