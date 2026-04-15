using UnityEngine;

namespace GUZ.Core.Debugging
{
    /// <summary>
    /// Added to NPC bone GameObjects by NpcColliderDebugAdapter.
    /// Tracks whether another collider is currently overlapping this trigger.
    /// </summary>
    public class ColliderHitTracker : MonoBehaviour
    {
        public bool IsBeingHit { get; private set; }

        private int _overlapCount;

        private void OnTriggerEnter(Collider other)
        {
            _overlapCount++;
            IsBeingHit = true;
        }

        private void OnTriggerExit(Collider other)
        {
            _overlapCount = Mathf.Max(0, _overlapCount - 1);
            IsBeingHit = _overlapCount > 0;
        }

        private void OnDisable()
        {
            _overlapCount = 0;
            IsBeingHit = false;
        }
    }
}
