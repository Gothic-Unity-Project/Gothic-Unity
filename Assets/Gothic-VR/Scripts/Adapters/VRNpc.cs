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
using Gothic.Core.Logging;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

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
        [Inject] private readonly PickpocketService _pickpocketService;

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
                Logger.Log($"[VRNpc] Grab on '{_npcData.Instance.GetName(NpcNameSlot.Slot0)}' ignored - a spell is active " +
                           "(targeting or telekinesis)", LogCat.VR);
                grabber.ForceRelease();
                return;
            }

            // DeveloperConfig.EnableVrPickpocket: the grip was for the purse on his hip (or his pockets are open) - a talk
            // made the pockets vanish from the hand.
            if (_pickpocketService.IsTalkBlocked(_npcData))
            {
                Logger.Log($"[VRNpc] Grab on '{_npcData.Instance.GetName(NpcNameSlot.Slot0)}' ignored - pickpocketing",
                    LogCat.VR);
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

            // Talking is a click, not a grab: release (HVR's grab turns physics on) and keep a seated NPC kinematic.
            grabber.ForceRelease();
            if (_npcData.IsUsingMob())
                _physicsService.DisablePhysicsForNpc(_npcData.PrefabProps);
        }
    }
}
#endif
