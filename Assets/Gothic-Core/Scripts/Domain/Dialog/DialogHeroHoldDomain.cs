using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Dialog
{
    /// <summary>
    /// Holds the hero while a dialog is open, owned by DialogService. Pinned (default, ADR 2026-10-30-dialog-pins-hero):
    /// at the spot where he stood when it opened - like the engine, the player can't walk away. Immersion option
    /// "dialogFreeMovement" (Gothic.ini, off by default): inside a bubble around the NPC, and walking out of it while
    /// choosing picks END.
    /// </summary>
    public class DialogHeroHoldDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        // An NPC farther away doesn't open its dialog. The engine allows 20 m (OpenGothic AI_ProcessInfos) - in VR that
        // was far too wide, Bloodwyn talked to the hero from across the yard.
        private const float _startMaxDistance = 8f;
        // Pinned: steps (stick or room-scale) around the spot where the hero stood when the dialog opened - free up to
        // the soft radius, slowed down to a stop at the radius.
        private const float _pinSoftRadius = 0.3f;
        private const float _pinRadius = 0.75f;
        // Held back a little inside the edge, so the hero isn't held right at the stop distance.
        private const float _holdEdgeMargin = 0.2f;
        // The hero can't be held inside (falling, climbing, teleported, blocked by a wall) - the dialog ends after
        // being outside this long, or at once when this far beyond the edge.
        private const float _holdGiveUpSeconds = 1f;
        private const float _holdGiveUpOvershoot = 2f;

        private Coroutine _heroHold;

        /// <summary>
        /// Our Immersion option "dialogFreeMovement" (Gothic.ini, off by default): the hero walks around during a
        /// dialog inside a bubble around the NPC, and walking out of it chooses END. Off: the dialog pins him.
        /// </summary>
        public bool IsFreeMovementOn()
        {
            return _configService.IsGothicOptionOn(Constants.IniNames.DialogFreeMovement);
        }

        /// <summary>
        /// Pinned (default): 8 m. Free movement: the hero has to be inside the bubble to read the dialog box.
        /// </summary>
        public float GetStartMaxDistance(bool isFreeMovement)
        {
            return isFreeMovement ? Constants.DialogFreeMovementRadius : _startMaxDistance;
        }

        /// <summary>
        /// Pinned (default): within 8 m (the engine: 20 m, OpenGothic AI_ProcessInfos). Free movement: inside the
        /// bubble.
        /// </summary>
        public bool IsHeroCloseEnoughToStart(NpcContainer npcContainer, bool isFreeMovement)
        {
            if (npcContainer.Go == null || !TryGetHeroPosition(out var heroPosition))
                return true;

            var npcPosition = npcContainer.Go.transform.position;
            return isFreeMovement
                ? IsHeroWithin(npcPosition, Constants.DialogFreeMovementRadius, out _)
                : Vector3.Distance(heroPosition, npcPosition) <= _startMaxDistance;
        }

        /// <summary>
        /// Called on every (re)open of the dialog. Pinned: locks teleport each time, as something else may have
        /// unlocked the player in between - the hero walks (stick or room-scale) inside the pin radius, the hold slows
        /// him down at its edge. The hold itself starts with the initial open only; mob dialogs have no bubble.
        /// </summary>
        /// <param name="chooseExit">Picks the END choice when the hero walks out of the bubble.</param>
        /// <param name="closeDialog">Ends the dialog when the hero can't be held or the NPC is gone.</param>
        public void Hold(NpcContainer npcContainer, bool isFreeMovement, bool initialDialogStarting, bool isMobDialog,
            Action<NpcContainer, InfoInstance> chooseExit, Action<NpcContainer> closeDialog)
        {
            if (!isFreeMovement)
                _contextInteractionService.LockPlayer(PlayerLocks.Dialog, PlayerControls.Teleport);

            if (!initialDialogStarting || (isFreeMovement && isMobDialog))
                return;

            StopHold();
            _heroHold = _unityMonoService.StartCoroutine(
                HoldHero(npcContainer, isFreeMovement, chooseExit, closeDialog));
        }

        /// <summary>
        /// The dialog ended: no more hold, the player moves freely again.
        /// </summary>
        public void Release()
        {
            StopHold();
            _contextInteractionService.UnlockPlayer(PlayerLocks.Dialog);
        }

        /// <summary>
        /// The END choice among the shown ones - the one walking out of the bubble picks. Vanilla G1/G2 END entries:
        /// nr = 999, description = DIALOG_ENDE.
        /// </summary>
        [CanBeNull]
        public InfoInstance FindExitInfo(List<InfoInstance> infos)
        {
            var exitDescription = _gameStateService.GothicVm
                .GetSymbolByName(DaedalusConst.DialogExitDescription)?.GetString(0);
            return infos.FirstOrDefault(i => i.Nr == DaedalusConst.DialogExitInfoNr ||
                                             (exitDescription != null && i.Description == exitDescription));
        }

        /// <summary>
        /// Walking out of the bubble chooses END if the shown choices offer it, otherwise (somebody talks,
        /// sub-choices, no END) the bubble's edge holds the hero back. If the hero can't be held (falling, climbing,
        /// teleported, blocked), the dialog ends.
        /// </summary>
        private IEnumerator HoldHero(NpcContainer npcContainer, bool isFreeMovement,
            Action<NpcContainer, InfoInstance> chooseExit, Action<NpcContainer> closeDialog)
        {
            if (!TryGetHeroPosition(out var pinPoint))
            {
                Logger.LogWarning($"[Dialog] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)}: no hero position " +
                                  "- the hero isn't held during the dialog", LogCat.Dialog);
                _heroHold = null;
                yield break;
            }

            var dialogs = _gameStateService.Dialogs;
            var softRadius = isFreeMovement ? Constants.DialogSlowDownDistance : _pinSoftRadius;
            var radius = isFreeMovement ? Constants.DialogFreeMovementRadius : _pinRadius;
            var outsideSeconds = 0f;
            while (dialogs.IsInDialog && dialogs.CurrentDialogNpc == npcContainer)
            {
                // The bubble follows the NPC: if it's gone (world change, removed), there's nobody to talk to.
                if (isFreeMovement && npcContainer.Go == null)
                {
                    Logger.LogWarning($"[Dialog] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)}: NPC is gone - " +
                                      "closing the dialog", LogCat.Dialog);
                    closeDialog(npcContainer);
                    yield break;
                }

                var center = isFreeMovement ? npcContainer.Go.transform.position : pinPoint;
                var canWalkAway = isFreeMovement && dialogs.IsChoosing && dialogs.ExitInfo != null;
                if (IsHeroWithin(center, radius, out var horizontalDistance))
                {
                    outsideSeconds = 0f;

                    // Slows the hero down before the edge, so the hold doesn't kick in as a hard stop.
                    if (!canWalkAway)
                        KeepHeroWithin(center, softRadius, radius);
                }
                else if (canWalkAway)
                {
                    Logger.Log($"[Dialog] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)}: hero walked away - " +
                               "choosing END", LogCat.Dialog);
                    chooseExit(npcContainer, dialogs.ExitInfo);
                }
                else
                {
                    outsideSeconds += Time.deltaTime;
                    var overshoot = horizontalDistance - radius;
                    if (outsideSeconds > _holdGiveUpSeconds || overshoot > _holdGiveUpOvershoot)
                    {
                        Logger.Log($"[Dialog] {npcContainer.Instance.GetName(NpcNameSlot.Slot0)}: hero can't be " +
                                   "held - closing the dialog", LogCat.Dialog);
                        closeDialog(npcContainer);
                        yield break;
                    }

                    KeepHeroWithin(center, softRadius, radius);
                }
                yield return null;
            }
            _heroHold = null;
        }

        /// <summary>
        /// The ground point under the hero's head (the camera): a physical step moves it, turning doesn't. The same
        /// point KeepPlayerWithin holds.
        /// </summary>
        private bool TryGetHeroPosition(out Vector3 position)
        {
            position = Vector3.zero;
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
                return false;

            position = hero.Go.transform.position;
            var head = hero.PrefabProps?.Head;
            if (head != null)
            {
                position.x = head.position.x;
                position.z = head.position.z;
            }
            return true;
        }

        /// <summary>
        /// The hold is a cylinder: horizontal distance like KeepPlayerWithin (a ramp mustn't count), plus a height cap
        /// (upper floor, cliff, ladder).
        /// </summary>
        private bool IsHeroWithin(Vector3 center, float radius, out float horizontalDistance)
        {
            horizontalDistance = 0f;
            if (!TryGetHeroPosition(out var heroPosition))
                return true;

            var offset = heroPosition - center;
            var heightDifference = Mathf.Abs(offset.y);
            offset.y = 0f;
            horizontalDistance = offset.magnitude;

            return horizontalDistance <= radius && heightDifference <= Constants.DialogMaxHeightDifference;
        }

        private void KeepHeroWithin(Vector3 center, float softRadius, float radius)
        {
            _contextInteractionService.KeepPlayerWithin(center, softRadius, radius - _holdEdgeMargin);
        }

        private void StopHold()
        {
            if (_heroHold == null)
                return;

            _unityMonoService.StopCoroutine(_heroHold);
            _heroHold = null;
        }
    }
}
