using Gothic.Core.Models.Animations;
using Gothic.Core.Models.Container;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// Plays a mob's own animations (the MobAnimator adapter on the mob). MobAnimationService decides what to play and
    /// gets it from IMobAnimatorFactory. Docs/mobs.md.
    /// </summary>
    public interface IMobAnimator
    {
        /// <summary>
        /// The mob state the animation is in or plays towards. 0 while the mob rests.
        /// </summary>
        int State { get; }

        /// <summary>
        /// NPCs (or the hero) using the mob right now - several on a mob with several slots.
        /// </summary>
        int UserCount { get; }

        bool HasUser(NpcContainer user);

        void AddUser(NpcContainer user);

        void RemoveUser(NpcContainer user);

        /// <summary>
        /// Plays the transition into the state (e.g. T_S0_2_S1), then its loop (S_S1). Either may be null: without a
        /// loop the mob rests in the transition's last pose (a lever stays down).
        /// </summary>
        void Play(int state, AnimationTrack transition, AnimationTrack loop);

        /// <summary>
        /// Lets a running transition end, plays the transition back (e.g. T_S1_2_S0, may be null) and rests. Its
        /// particles fade out.
        /// </summary>
        void PlayBackAndStop(AnimationTrack backTransition);
    }
}
