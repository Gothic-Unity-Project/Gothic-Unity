using UnityEngine;

namespace GUZ.Core.Debugging
{
    public class VobCullingGizmo : MonoBehaviour
    {
        [Tooltip("Hint: Only works if DeveloperConfig.DrawVobMeshCullingGizmos is also activated.")]
        public bool ActivateGizmo = true;
    }
}
