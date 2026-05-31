using Gothic.Core;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Player;
using Gothic.Core.Extensions;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.VR.Adapters.Marvin
{
    // FIXME - Can be used for proper Marvin mode feature: "kill"
    public class VRNpcMarvinAdapter : MonoBehaviour
    {
        [Inject] private PlayerService _playerService;

        private NpcContainer _npcData;

        private void Awake()
        {
            _npcData = GetComponentInParent<NpcLoader>().Npc.GetUserData();
        }

        public void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            // Safety return, until we have proper Marvin feature handling implemented (active = true/false)
            return;

            var attacker = _playerService.HeroContainer;
            GlobalEventDispatcher.FightHit.Invoke(attacker, _npcData, default);
        }
    }
}
