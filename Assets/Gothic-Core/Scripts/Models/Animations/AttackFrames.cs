using System.Collections.Generic;

namespace Gothic.Core.Models.Animations
{
    /// <summary>
    /// The frames of an attack animation's events (MDS): one hit frame (DEF_OPT_FRAME) and one end (DEF_HIT_END) per
    /// part of a combo, and a start/end pair (DEF_WINDOW) per follow-up hit.
    /// </summary>
    public readonly struct AttackFrames
    {
        public readonly IReadOnlyList<int> OptimalFrames;
        public readonly IReadOnlyList<int> HitEnds;
        public readonly IReadOnlyList<int> Windows;

        public AttackFrames(IReadOnlyList<int> optimalFrames, IReadOnlyList<int> hitEnds, IReadOnlyList<int> windows)
        {
            OptimalFrames = optimalFrames;
            HitEnds = hitEnds;
            Windows = windows;
        }
    }
}
