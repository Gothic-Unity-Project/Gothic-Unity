using UnityEngine;

namespace GUZ.Core.Debugging.Fighting
{
    /// <summary>
    /// Added to NPC bone GameObjects by NpcColliderDebugAdapter.
    /// Tracks whether another collider is currently overlapping this trigger.
    /// </summary>
    public class ColliderHitTracker : MonoBehaviour
    {
        public bool IsBeingHit { get; private set; }

        private int _overlapCount;

        protected virtual void OnTriggerEnter(Collider other)
        {
            _overlapCount++;
            IsBeingHit = true;
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            _overlapCount = Mathf.Max(0, _overlapCount - 1);
            IsBeingHit = _overlapCount > 0;
        }

        protected virtual void OnDisable()
        {
            _overlapCount = 0;
            IsBeingHit = false;
        }
    }
}
