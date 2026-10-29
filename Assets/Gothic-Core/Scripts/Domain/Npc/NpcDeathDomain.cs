using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Like Gothic, an NPC dies when its HP reach 0, whoever changed them: a hit, or a script via
    /// Npc_ChangeAttribute() (e.g. B_KillNpc()). OpenGothic: Npc::checkHealth().
    /// </summary>
    public class NpcDeathDomain
    {
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly PhysicsService _physicsService;


        public void Die(NpcContainer npc)
        {
            npc.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            Logger.Log($"[NpcDeath] {npc.Instance.GetName(NpcNameSlot.Slot0)} is dead.", LogCat.Npc);

            // An NPC out of sight (culled) has no GameObject. It only gets the dead state.
            var prefabProps = npc.PrefabProps;
            if (prefabProps?.AnimationSystem == null)
                return;

            // Death takes priority over everything: drop queued AI actions (e.g. a GoToWp/UseMob enqueued the same
            // frame), stop running animations (e.g. s_walk still looping) and play the dying animation directly.
            // Otherwise AiHandler's dead-NPC branch would play them out on the corpse before dying.
            npc.Props.AnimationQueue.Clear();
            prefabProps.AnimationSystem.StopAllAnimations();
            _physicsService.DisablePhysicsForNpc(prefabProps);

            var animName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.DeadB, npc);
            prefabProps.AnimationSystem.PlayAnimation(animName);
        }
    }
}
