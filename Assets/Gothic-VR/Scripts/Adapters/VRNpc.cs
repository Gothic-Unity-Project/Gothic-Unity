#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.VR.Adapters
{
    public class VRNpc : MonoBehaviour
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly PhysicsService _physicsService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private NpcContainer _npcData;
        private VRNpcLoot _npcLoot;

        private void Awake()
        {
            _npcData = GetComponentInParent<NpcLoader>().Npc.GetUserData();
            _npcLoot = GetComponent<VRNpcLoot>();
        }

        public void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_vrPlayerService.IsSpellActive)
            {
                grabber.ForceRelease();
                return;
            }

            var bodyState = _npcData.Props.BodyState;
            var isDead = bodyState == VmGothicEnums.BodyState.BsDead;
            var isUnconscious = bodyState == VmGothicEnums.BodyState.BsUnconscious;

            if ((isDead || isUnconscious) && _configService.Dev.EnableNpcLooting && _npcLoot != null)
            {
                _npcLoot.Toggle(_npcData);
                grabber.ForceRelease();
                _physicsService.DisablePhysicsForNpc(_npcData.PrefabProps);
                return;
            }

            if (_gameStateService.Dialogs.IsInDialog)
            {
                _dialogService.SkipCurrentDialogLine(_npcData.Props);
            }
            else
            {
                _gameStateService.Dialogs.WasPlayerInitiated = true;
                _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessTalk, _npcData.Props, _npcData.Instance, null, (NpcInstance)_gameStateService.GothicVm.GlobalHero);
            }
        }
    }
}
#endif
