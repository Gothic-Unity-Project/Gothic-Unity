using Gothic.Core.Extensions;
using Gothic.Core.Models.Animations;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vobs;

namespace Gothic.Core.Adapters.Vob
{
    public class MobAnimatorFactory : IMobAnimatorFactory
    {
        public IMobAnimator GetOrCreate(VobContainer mob, AnimationSkeleton skeleton)
        {
            if (mob?.Go == null || skeleton == null)
                return null;

            if (mob.Go.TryGetComponent<MobAnimator>(out var animator))
                return animator;

            // The nodes are mapped once: a lazily loaded mob without its mesh yet gets its animator later.
            if (mob.Go.TryGetComponent<VobLoader>(out var loader) && !loader.IsLoaded)
                return null;

            animator = mob.Go.AddComponent<MobAnimator>().Inject();
            animator.Init(mob, skeleton);
            return animator;
        }

        public IMobAnimator TryGet(VobContainer mob)
        {
            if (mob?.Go == null)
                return null;

            return mob.Go.TryGetComponent<MobAnimator>(out var animator) ? animator : null;
        }
    }
}
