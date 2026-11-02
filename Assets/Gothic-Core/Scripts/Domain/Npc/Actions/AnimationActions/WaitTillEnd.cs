using System.Linq;
using Gothic.Core.Models.Container;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    /// <summary>
    /// AI_WaitTillEnd(self, other): self waits until other has done everything it was given up to now - its running
    /// action and the ones queued behind it. What other gets later doesn't count.
    /// </summary>
    public class WaitTillEnd : AbstractAnimationAction
    {
        private readonly NpcContainer _other;
        private AbstractAnimationAction _lastOfOther;

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
            var otherProps = _other.Props;
            if (otherProps == null || _lastOfOther == null)
                return true;

            if (otherProps.AnimationQueue.Contains(_lastOfOther))
                return false;

            return otherProps.CurrentAction != _lastOfOther || _lastOfOther.HasEnded;
        }
    }
}
