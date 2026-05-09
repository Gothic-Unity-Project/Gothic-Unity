using GUZ.Core;
using GUZ.Core.Adapters.Npc;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Container;
using GUZ.Core.Services.Player;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.VR.Adapters.Marvin
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
