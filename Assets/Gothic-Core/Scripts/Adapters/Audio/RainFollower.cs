using UnityEngine;

namespace Gothic.Core.Adapters.Audio
{
    /// <summary>
    /// Keeps the rain (particles and sound) around the player's head - it sat at the world's origin below the player
    /// rig's root, which doesn't move. On Rain.prefab.
    /// </summary>
    public class RainFollower : MonoBehaviour
    {
        private void LateUpdate()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head != null)
                transform.position = head.position;
        }
    }
}
