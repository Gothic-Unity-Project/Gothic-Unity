using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Fades the screen to black when the head moves into walls, furniture or other world geometry, so that players
    /// can't look through them by leaning in. Sits on the VR camera next to ScreenFade.
    ///
    /// - The distance from the eyes to the nearest wall collider is measured every frame (shrinking sphere checks,
    ///   which work for the non-convex world mesh as well). Below _fadeStartDistance the screen gets darker, at
    ///   _fullFadeDistance it's black.
    /// - Once the head is fully through a (one-sided) wall mesh, nothing is near anymore. Therefore, the line from the
    ///   last free head position to the current one is checked as well: blocked = black, until the head is back on the
    ///   free side.
    /// - Jumps (teleports, loading) reset the free position, as the line would cross the whole world.
    /// </summary>
    [RequireComponent(typeof(ScreenFade))]
    public class WallFade : MonoBehaviour
    {
        [Tooltip("Layers which fade the screen when the head is inside them (world mesh, VOBs).")]
        [SerializeField] private LayerMask _wallLayers = 1;

        [Tooltip("Distance in m between eyes and wall at which the screen starts fading.")]
        [SerializeField] private float _fadeStartDistance = 0.1f;

        [Tooltip("Distance in m between eyes and wall at which the screen is black (near clip plane plus a margin).")]
        [SerializeField] private float _fullFadeDistance = 0.03f;

        [Tooltip("Fade speed in alpha per second (towards black and back).")]
        [SerializeField] private float _fadeSpeed = 6f;

        [Tooltip("A head movement longer than this in one frame (m) counts as teleport: no wall check in between.")]
        [SerializeField] private float _teleportDistance = 1f;

        [Tooltip("Sphere checks per frame to measure the distance while a wall is near. More = finer steps.")]
        [SerializeField] private int _distanceIterations = 5;

        private ScreenFade _screenFade;
        private Vector3 _lastFreePosition;
        private Vector3 _lastPosition;
        private bool _isFreePositionKnown;

        private void Awake()
        {
            _screenFade = GetComponent<ScreenFade>();
        }

        private void OnDisable()
        {
            _isFreePositionKnown = false;
            _screenFade.SetWallFade(0f, float.MaxValue);
        }

        private void LateUpdate()
        {
            var position = transform.position;

            var isTeleported = (position - _lastPosition).sqrMagnitude > _teleportDistance * _teleportDistance;
            if (!_isFreePositionKnown || isTeleported)
            {
                _lastFreePosition = position;
                _isFreePositionKnown = true;
            }

            _lastPosition = position;

            var distance = GetWallDistance(position);
            var alpha = Mathf.InverseLerp(_fadeStartDistance, _fullFadeDistance, distance);

            if (IsBehindWall(position))
                alpha = 1f;
            else if (distance >= _fadeStartDistance)
                _lastFreePosition = position;

            _screenFade.SetWallFade(alpha, _fadeSpeed);
        }

        /// <summary>
        /// Approximate distance from the position to the nearest wall collider, at most _fadeStartDistance.
        /// Binary search with overlap checks, as Collider.ClosestPoint doesn't support non-convex mesh colliders.
        /// </summary>
        private float GetWallDistance(Vector3 position)
        {
            if (!IsWallWithin(position, _fadeStartDistance))
                return _fadeStartDistance;

            var free = 0f;
            var blocked = _fadeStartDistance;
            for (var i = 0; i < _distanceIterations; i++)
            {
                var radius = (free + blocked) * 0.5f;
                if (IsWallWithin(position, radius))
                    blocked = radius;
                else
                    free = radius;
            }

            return free;
        }

        private bool IsWallWithin(Vector3 position, float radius)
        {
            return Physics.CheckSphere(position, radius, _wallLayers, QueryTriggerInteraction.Ignore);
        }

        private bool IsBehindWall(Vector3 position)
        {
            var offset = position - _lastFreePosition;
            var distance = offset.magnitude;
            if (distance < 0.001f)
                return false;

            // Both directions: world meshes are one-sided, a ray only hits a triangle from its front.
            var direction = offset / distance;
            return Physics.Raycast(_lastFreePosition, direction, distance, _wallLayers, QueryTriggerInteraction.Ignore)
                   || Physics.Raycast(position, -direction, distance, _wallLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
