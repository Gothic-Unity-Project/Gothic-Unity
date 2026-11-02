using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_WaitTillEnd(self, other): self waits until other has done everything it was given up to now - its running
    /// action and the ones queued behind it. What other gets later doesn't count.
    /// </summary>
    public class WaitTillEnd : AbstractAnimationAction
    {
        // Other's AI can stand still (out of reach, culled) - the waiting NPC must not hang in it forever.
        private const float _maxWaitSeconds = 30f;

        private readonly NpcContainer _other;
        private AbstractAnimationAction _lastOfOther;
        private float _waitedSeconds;

        public WaitTillEnd(AnimationAction action, NpcContainer npcContainer, NpcContainer other)
            : base(action, npcContainer)
        {
            _other = other;
        }

        public override void Start()
        {
            var otherProps = _other.Props;
            _lastOfOther = otherProps?.AnimationQueue.LastOrDefault() ?? otherProps?.CurrentAction;
        }

        public override bool IsFinished()
        {
            // Nothing runs on other (the VR hero - the player moves him, his queue isn't played; an NPC whose AI
            // hasn't started): nothing to wait for. DoM's Diego waited for the hero forever.
            var otherProps = _other.Props;
            if (otherProps?.CurrentAction == null || _lastOfOther == null)
                return true;

            _waitedSeconds += Time.deltaTime;
            if (_waitedSeconds > _maxWaitSeconds)
            {
                Logger.LogWarning($"[NpcAi] AI_WaitTillEnd: {NpcInstance.GetName(NpcNameSlot.Slot0)} waited " +
                                  $"{_maxWaitSeconds} s for {_other.Instance.GetName(NpcNameSlot.Slot0)} - goes on.",
                    LogCat.Ai);
                IsFinishedFlag = true;
                return true;
            }

            if (otherProps.AnimationQueue.Contains(_lastOfOther))
                return false;

            // Kept, so an NPC waiting for this one (trialogues chain them) sees the end too.
            IsFinishedFlag = otherProps.CurrentAction != _lastOfOther || _lastOfOther.HasEnded;
            return IsFinishedFlag;
        }
    }
}
