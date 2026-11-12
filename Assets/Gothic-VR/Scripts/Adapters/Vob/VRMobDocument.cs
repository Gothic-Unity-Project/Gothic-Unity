using Gothic.Core.Adapters.Vob;
using UnityEngine;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// The page a mob shows with Doc_Show (G2 bookstands: Use_Bookstand*_S1) - a VRDocViewer above the mob, facing the
    /// player, like a held document or map. Closes when the player walks away.
    /// </summary>
    public class VRMobDocument : MonoBehaviour
    {
        [Tooltip("The page closes when the player's head is farther away (m).")]
        [SerializeField] private float _closeDistance = 3f;
        [Tooltip("The page's height below the player's eyes (m).")]
        [SerializeField] private float _belowEyes = 0.05f;
        [Tooltip("Distance from the mob's center towards the player (m).")]
        [SerializeField] private float _towardsPlayer = 0.4f;

        // VRDocViewer lays its canvas flat for a document held in the hand (local rotation 90, 0, 0).
        private static readonly Quaternion _viewerCanvasRotation = Quaternion.Euler(90f, 0f, 0f);

        private Transform _head;

        /// <summary>
        /// Placed between the mob and the player's head. Without a head (no VR rig yet) above the mob, closed by the
        /// next touch only.
        /// </summary>
        public void Init(VobLoader mob, Transform head)
        {
            _head = head;
            PlaceInFrontOfPlayer(mob);
        }

        private void PlaceInFrontOfPlayer(VobLoader mob)
        {
            var center = mob.transform.position;
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var mobRenderer in mob.GetComponentsInChildren<Renderer>())
            {
                // VOBs nested below the mob have their own loader.
                if (mobRenderer.GetComponentInParent<VobLoader>(true) != mob)
                    continue;

                if (!hasBounds)
                    bounds = mobRenderer.bounds;
                else
                    bounds.Encapsulate(mobRenderer.bounds);
                hasBounds = true;
            }
            if (hasBounds)
                center = bounds.center;
            if (_head == null)
            {
                transform.position = center + Vector3.up;
                return;
            }

            var toPlayer = _head.position - center;
            toPlayer.y = 0f;
            var direction = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : -_head.forward;
            var position = center + direction * _towardsPlayer;
            position.y = _head.position.y - _belowEyes;
            transform.position = position;

            // The canvas faces the player upright.
            transform.rotation = Quaternion.LookRotation(-direction, Vector3.up) *
                                 Quaternion.Inverse(_viewerCanvasRotation);
        }

        private void Update()
        {
            if (_head == null)
                return;

            if (Vector3.Distance(_head.position, transform.position) > _closeDistance)
                Destroy(gameObject);
        }
    }
}
