using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Ports;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The VR hero falling: from the highest point in the air down to where he lands, the same fall damage as every NPC
    /// (NpcFallService - FALLDOWN_HEIGHT and FALLDOWN_DAMAGE of the human guild). Landing in water doesn't hurt, climbing
    /// and teleports don't count as falls.
    /// </summary>
    public class VrHeroFall : MonoBehaviour
    {
        // Shorter drops (steps, slopes, jumps) are never deep enough to hurt.
        private const float _minFallHeight = 1f;
        // Moving farther in one frame is a teleport.
        private const float _teleportDistance = 2f;

        private IVrRig _rig;
        private NpcService _npcService;
        private NpcFallService _npcFallService;
        private float? _highestY;
        private Vector3 _lastPosition;

        private void Start()
        {
            _rig = GetComponent<IVrRig>();
            _npcService = ReflexProjectInstaller.DIContainer.Resolve<NpcService>();
            _npcFallService = ReflexProjectInstaller.DIContainer.Resolve<NpcFallService>();
        }

        private void Update()
        {
            if (_rig?.Root == null)
                return;

            var position = _rig.Root.position;
            var isTeleport = (position - _lastPosition).sqrMagnitude > _teleportDistance * _teleportDistance;
            _lastPosition = position;

            var hero = _npcService.GetHeroContainer();
            if (isTeleport || _rig.IsClimbing || hero?.Vob?.AiHuman == null ||
                hero.Vob.AiHuman.WaterLevel != (int)ZenGineConst.WaterLevel.Normal)
            {
                _highestY = null;
                return;
            }

            if (!_rig.IsGrounded)
            {
                _highestY = Mathf.Max(_highestY ?? position.y, position.y);
                return;
            }

            if (_highestY == null)
                return;

            var height = _highestY.Value - position.y;
            _highestY = null;
            if (height >= _minFallHeight)
                _npcFallService.LandHero(hero, height);
        }
    }
}
