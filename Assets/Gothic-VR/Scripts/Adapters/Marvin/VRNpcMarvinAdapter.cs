using Gothic.Core;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Player;
using Gothic.Core.Extensions;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
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
        private VrInteractable _interactable;

        private void Awake()
        {
            _npcData = GetComponentInParent<NpcLoader>().Npc.GetUserData();
        }

        /// <summary>
        /// Subscribed in Start (not Awake), so that VRNpc (dialog/loot) always handles a grab first.
        /// </summary>
        private void Start()
        {
            _interactable = VrInteractable.Find(this);
            if (_interactable != null)
                _interactable.Grabbed += OnGrabbed;
        }

        private void OnDestroy()
        {
            if (_interactable != null)
                _interactable.Grabbed -= OnGrabbed;
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            // Safety return, until we have proper Marvin feature handling implemented (active = true/false)
            return;

            var attacker = _playerService.HeroContainer;
            GlobalEventDispatcher.FightHit.Invoke(attacker, _npcData, default);
        }
    }
}
