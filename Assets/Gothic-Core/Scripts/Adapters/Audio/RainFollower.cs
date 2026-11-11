using UnityEngine;

namespace Gothic.Core.Adapters.Audio
{
    /// <summary>
    /// Keeps the rain (particles and sound) around the player's head - it sat at the world's origin below the player
    /// rig's root, which doesn't move. On Rain.prefab.
    /// </summary>
    public class RainFollower : MonoBehaviour
    {
        private Transform _head;

        private void LateUpdate()
        {
            // A new player rig (world change) has a new camera.
            if (_head == null)
                _head = Camera.main != null ? Camera.main.transform : null;
            if (_head != null)
                transform.position = _head.position;
        }
    }
}
