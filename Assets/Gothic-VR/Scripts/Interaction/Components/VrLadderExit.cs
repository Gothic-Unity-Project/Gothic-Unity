using Gothic.Core.Logging;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.Context;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Components
{
    /// <summary>
    /// Gets the player off a ladder at its top. Climbing itself is done by the backend (XRI: ClimbInteractable and
    /// ClimbProvider), which lets the player hang at the top edge without ever reaching the floor.
    ///
    /// While a hand holds the ladder and the head rose above its top edge, the rig is put onto the floor in front of
    /// the ladder (the way the player looks). The spot needs a floor near the top edge and room for the player's
    /// CharacterController, otherwise the player stays on the ladder. Both hands let go of the ladder meanwhile.
    ///
    /// HVR has no such mantle (the player lets go and walks off by thumbstick), so this is inactive there.
    /// </summary>
    [RequireComponent(typeof(VrInteractable))]
    public class VrLadderExit : MonoBehaviour
    {
        private const int MaxOverlaps = 16;

        [Inject] private readonly IVrBackend _vrBackend;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        [Tooltip("The head needs to be this far (m) above the ladder's top edge to step off.")]
        [SerializeField] private float _headAboveTop = 0.15f;

        [Tooltip("Distances (m) in front of the head, where a floor is searched. The first free one wins.")]
        [SerializeField] private float[] _exitDistances = { 0.5f, 0.8f };

        [Tooltip("The floor may be this far (m) above or below the ladder's top edge.")]
        [SerializeField] private float _floorTolerance = 0.5f;

        [Tooltip("Seconds until the next step off is tried after a failed one.")]
        [SerializeField] private float _retryInterval = 0.5f;

        private readonly Collider[] _overlaps = new Collider[MaxOverlaps];
        private VrInteractable _interactable;
        private IVrRig _rig;
        private int _holdCount;
        private float _nextTryTime;
        private bool _isFailureLogged;

        private bool IsActive => _vrBackend != null && _vrBackend.Kind == VrInteractionBackend.Xri;

        private void Awake()
        {
            if (!IsActive)
            {
                enabled = false;
                return;
            }

            _interactable = GetComponent<VrInteractable>();
            _interactable.Grabbed += OnGrabbed;
            _interactable.Released += OnReleased;
        }

        private void OnDestroy()
        {
            if (_interactable == null)
                return;

            _interactable.Grabbed -= OnGrabbed;
            _interactable.Released -= OnReleased;
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            if (args.IsHandOrDistance)
                _holdCount++;
        }

        private void OnReleased(VrGrabArgs args)
        {
            if (args.IsHandOrDistance)
                _holdCount = Mathf.Max(0, _holdCount - 1);

            if (_holdCount == 0)
                _isFailureLogged = false;
        }

        private void Update()
        {
            if (_holdCount == 0 || Time.time < _nextTryTime)
                return;

            _rig ??= _contextInteractionService.GetImpl<VRContextInteractionService>().Rig;
            if (_rig == null || !_rig.IsClimbing || !TryGetTopY(out var topY))
                return;

            if (_rig.Head.position.y < topY + _headAboveTop)
                return;

            if (TryFindExit(topY, out var exitPosition))
            {
                var yaw = Quaternion.Euler(0f, _rig.Head.eulerAngles.y, 0f);
                _interactable.ForceRelease();
                _rig.Teleport(exitPosition, yaw);
                _holdCount = 0;
                return;
            }

            _nextTryTime = Time.time + _retryInterval;
            if (!_isFailureLogged)
            {
                _isFailureLogged = true;
                Logger.LogWarning($"[Ladder] {name}: no free floor in front of the ladder's top edge found.",
                    LogCat.VR);
            }
        }

        private bool TryGetTopY(out float topY)
        {
            topY = float.NegativeInfinity;
            foreach (var ladderCollider in _interactable.Colliders)
            {
                if (ladderCollider != null)
                    topY = Mathf.Max(topY, ladderCollider.bounds.max.y);
            }

            return !float.IsNegativeInfinity(topY);
        }

        private bool TryFindExit(float topY, out Vector3 position)
        {
            position = default;

            var forward = _rig.Head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                return false;
            forward.Normalize();

            var head = _rig.Head.position;
            foreach (var distance in _exitDistances)
            {
                var rayStart = new Vector3(head.x, topY + _floorTolerance, head.z) + forward * distance;
                if (!TryFindFloor(rayStart, topY, out var floor) || !HasRoom(floor))
                    continue;

                position = floor;
                return true;
            }

            return false;
        }

        private bool TryFindFloor(Vector3 rayStart, float topY, out Vector3 floor)
        {
            floor = default;

            var hits = Physics.RaycastAll(rayStart, Vector3.down, _floorTolerance * 2f, ~0,
                QueryTriggerInteraction.Ignore);
            var closest = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.distance >= closest || IsOwnCollider(hit.collider) || hit.normal.y < 0.7f)
                    continue;

                closest = hit.distance;
                floor = hit.point;
            }

            return closest < float.MaxValue && Mathf.Abs(floor.y - topY) <= _floorTolerance;
        }

        /// <summary>
        /// The player's CharacterController fits at the spot (a bit above the floor, so that the floor isn't hit).
        /// </summary>
        private bool HasRoom(Vector3 floor)
        {
            var controller = _rig.CharacterController;
            var radius = controller.radius;
            var bottom = floor + Vector3.up * (radius + controller.skinWidth + 0.05f);
            var top = floor + Vector3.up * Mathf.Max(radius + 0.1f, controller.height - radius);

            var mask = GetCollidingLayers(_rig.Root.gameObject.layer);
            var count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, _overlaps, mask,
                QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (!IsOwnCollider(_overlaps[i]) && !_overlaps[i].transform.IsChildOf(_rig.Root))
                    return false;
            }

            return true;
        }

        private static int GetCollidingLayers(int layer)
        {
            var mask = 0;
            for (var i = 0; i < 32; i++)
            {
                if (!Physics.GetIgnoreLayerCollision(layer, i))
                    mask |= 1 << i;
            }

            return mask;
        }

        private bool IsOwnCollider(Collider other)
        {
            return other.transform.IsChildOf(transform);
        }
    }
}
