using Gothic.Core.Models.Animations;
using Gothic.Core.Models.Container;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// Gives a mob's animator. Implemented in Adapters/ (MobAnimatorFactory), registered in ReflexProjectInstaller.
    /// </summary>
    public interface IMobAnimatorFactory
    {
        /// <summary>
        /// The mob's animator, created on first use with the nodes of the skeleton. Null without a GameObject.
        /// </summary>
        IMobAnimator GetOrCreate(VobContainer mob, AnimationSkeleton skeleton);

        /// <summary>
        /// The mob's animator if it has one, else null (nothing is created).
        /// </summary>
        IMobAnimator TryGet(VobContainer mob);
    }
}
