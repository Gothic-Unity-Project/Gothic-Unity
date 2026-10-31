using System.Collections.Generic;
using Gothic.Core.Models.Animations;

namespace Gothic.Core.Models.Npc
{
    /// <summary>
    /// An NPC's combo like the engine's (OpenGothic Pose::continueCombo, Sequence::isFinished): a swing hits at its
    /// part's DEF_OPT_FRAME, a swing asked for in its DEF_WINDOW continues the same animation from the part's
    /// DEF_HIT_END, else the attack is over there. A combo has at most as many follow-ups as the animation has
    /// windows (lurker s_FistAttack 10,20 = one, human s_1hAttack 12,36,47,71,82,106 = three).
    /// Times are seconds into the animation's track.
    /// </summary>
    public class AttackCombo
    {
        // Swings chained in combo windows since the last one that played to its end.
        public int Length;
        // The attack animation that keeps playing for the next swing, and the part that swing continues with.
        public string ContinueAnimation;
        public int ContinuePart;

        public static int GetPartCount(AttackFrames frames)
        {
            return frames.OptimalFrames?.Count ?? 0;
        }

        public static int GetWindowCount(AttackFrames frames)
        {
            return (frames.Windows?.Count ?? 0) / 2;
        }

        public static bool IsHitFrameReached(AttackFrames frames, int part, float time, float fps)
        {
            return TryGetFrame(frames.OptimalFrames, part, out var frame) && time >= frame / fps;
        }

        public static bool IsWindowOpen(AttackFrames frames, int part, float time, float fps)
        {
            return TryGetFrame(frames.Windows, part * 2, out var start) &&
                   TryGetFrame(frames.Windows, part * 2 + 1, out var end) &&
                   time >= start / fps && time <= end / fps;
        }

        /// <summary>
        /// False if the animation has no hit end for the part (it plays to its end then).
        /// </summary>
        public static bool IsHitEndReached(AttackFrames frames, int part, float time, float fps)
        {
            return TryGetFrame(frames.HitEnds, part, out var frame) && time >= frame / fps;
        }

        /// <summary>
        /// Where the part continues: the end of the part before it (DEF_HIT_END[part - 1]). False if the animation has
        /// no such part.
        /// </summary>
        public static bool TryGetContinueTime(AttackFrames frames, int part, float fps, out float time)
        {
            time = 0f;
            if (!TryGetFrame(frames.OptimalFrames, part, out _) || !TryGetFrame(frames.HitEnds, part - 1, out var end))
                return false;

            time = end / fps;
            return true;
        }

        private static bool TryGetFrame(IReadOnlyList<int> frames, int index, out int frame)
        {
            frame = 0;
            if (frames == null || index < 0 || index >= frames.Count)
                return false;

            frame = frames[index];
            return true;
        }
    }
}
