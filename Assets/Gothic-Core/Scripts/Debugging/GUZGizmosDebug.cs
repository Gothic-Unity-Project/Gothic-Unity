using UnityEngine;

namespace GUZ.Core.Debugging
{
    /// <summary>
    /// Add it to an object where you want to draw some information.
    /// </summary>
    public class GuzGizmosDebug : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            var pos = transform.position;
            var up = new Vector3(pos.x, 100, pos.z);
            var down = new Vector3(pos.x, -100, pos.z);

            Gizmos.DrawLine(up, down);
        }
    }
}
