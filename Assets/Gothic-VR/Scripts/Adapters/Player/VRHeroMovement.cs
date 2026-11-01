using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Reports how the VR hero moves to HeroPerceptionService, which derives Gothic's body state (stand, walk, run,
    /// sneak) and the footsteps NPCs hear. Sits next to VRPlayer and the backend's IVrRig.
    /// </summary>
    public class VRHeroMovement : MonoBehaviour
    {
        // A move this far (m) within one frame is a teleport, not walking.
        private const float _teleportDistance = 1.5f;

        [Inject] private readonly HeroPerceptionService _heroPerceptionService;

        private IVrRig _rig;
        private Vector3 _lastPosition;

        private void Start()
        {
            _rig = GetComponent<IVrRig>();
            if (_rig != null)
                _lastPosition = _rig.Root.position;
        }

        private void Update()
        {
            if (_rig == null)
                return;

            var position = _rig.Root.position;
            var moved = new Vector2(position.x - _lastPosition.x, position.z - _lastPosition.z).magnitude;
            _lastPosition = position;

            // A teleport or falling isn't walking.
            if (moved > _teleportDistance || !_rig.IsGrounded)
                moved = 0f;

            _heroPerceptionService.OnHeroMoved(moved, Time.deltaTime, _rig.IsCrouching);
        }
    }
}
