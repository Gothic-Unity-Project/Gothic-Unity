using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.UI.StatusBars;
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
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Services;
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
        private VrInteractable _interactable;
        private StatusBarAdapter _healthBar;
        private Coroutine _holdToLoot;

        // DeveloperConfig.EnableCorpseHoldToLoot: seconds the grip is held on a corpse until its loot opens/closes.
        private const float _lootHoldSeconds = 1f;

        private void Awake()
        {
            _npcData = GetComponentInParent<NpcLoader>().Npc.GetUserData();
            _npcLoot = GetComponent<VRNpcLoot>();
            _healthBar = GetComponentInChildren<StatusBarAdapter>(true);

            _interactable = VrInteractable.Find(this);
            _interactable.Grabbed += OnGrabbed;
            _interactable.HoverEntered += OnHoverEnter;
            _interactable.HoverExited += OnHoverExit;
        }

        private void OnDestroy()
        {
            if (_interactable == null)
                return;

            _interactable.Grabbed -= OnGrabbed;
            _interactable.HoverEntered -= OnHoverEnter;
            _interactable.HoverExited -= OnHoverExit;
        }

        /// <summary>
        /// The NPC's health bar is only visible while a hand hovers over the NPC.
        /// </summary>
        private void OnHoverEnter(VrGrabArgs _)
        {
            if (_healthBar != null)
                _healthBar.EnableBar();
        }

        private void OnHoverExit(VrGrabArgs _)
        {
            if (_healthBar != null)
                _healthBar.DisableBar();
        }

        private System.Collections.IEnumerator ToggleLootWhenHeld(VrInteractable interactable)
        {
            var heldTime = 0f;
            while (heldTime < _lootHoldSeconds)
            {
                yield return null;
                if (interactable == null || !interactable.IsHeld)
                {
                    _holdToLoot = null;
                    yield break;
                }
                heldTime += Time.deltaTime;
            }

            _holdToLoot = null;
            Logger.Log($"[VRNpc] Grip held on '{_npcData.Instance.GetName(NpcNameSlot.Slot0)}' - loot toggled",
                LogCat.VR);
            _npcLoot.Toggle(_npcData);
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            if (_vrPlayerService.IsSpellActive)
            {
                Logger.Log($"[VRNpc] Grab on '{_npcData.Instance.GetName(NpcNameSlot.Slot0)}' ignored - a spell is active " +
                           "(targeting or telekinesis)", LogCat.VR);
                args.Interactable.ForceRelease();
                return;
            }

            // DeveloperConfig.EnableVrPickpocket: the grip was for the purse on his hip (or his pockets are open) - a talk
            // made the pockets vanish from the hand.
            if (_pickpocketService.IsTalkBlocked(_npcData))
            {
                Logger.Log($"[VRNpc] Grab on '{_npcData.Instance.GetName(NpcNameSlot.Slot0)}' ignored - pickpocketing",
                    LogCat.VR);
                args.Interactable.ForceRelease();
                return;
            }

            var bodyState = _npcData.Props.BodyState;
            var isDead = bodyState == VmGothicEnums.BodyState.BsDead;
            var isUnconscious = bodyState == VmGothicEnums.BodyState.BsUnconscious;

            if ((isDead || isUnconscious) && _configService.Dev.EnableNpcLooting && _npcLoot != null)
            {
                if (_npcLoot.TryMakeEmptyCorpseInert(_npcData))
                    return;

                // DeveloperConfig.EnableCorpseHoldToLoot: the loot opens/closes only after the grip is held on the
                // body for a moment - a grab in passing (fighting, picking up an item next to it) opened the loot.
                // Select-only (XRI) only: the grab lasts as long as the grip is held.
                if (_configService.Dev.EnableCorpseHoldToLoot && args.Interactable.IsSelectOnly)
                {
                    if (_holdToLoot != null)
                        StopCoroutine(_holdToLoot);
                    _holdToLoot = StartCoroutine(ToggleLootWhenHeld(args.Interactable));
                    return;
                }

                _npcLoot.Toggle(_npcData);
                // Grabbing turns physics on (VRPlayerItemAdapter); release immediately and freeze the corpse.
                // Select-only interactables (XRI) neither move the NPC nor turn its physics on.
                if (!args.Interactable.IsSelectOnly)
                {
                    args.Interactable.ForceRelease();
                    _physicsService.DisablePhysicsForNpc(_npcData.PrefabProps);
                }

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

            // Talking is a click, not a grab: release (grabbing turns physics on) and keep a seated NPC kinematic.
            // Select-only interactables (XRI) don't move the NPC, so there's nothing to undo.
            if (args.Interactable.IsSelectOnly)
                return;

            args.Interactable.ForceRelease();
            if (_npcData.IsUsingMob())
                _physicsService.DisablePhysicsForNpc(_npcData.PrefabProps);
        }
    }
}
