using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
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
    /// The scripts' part of the death (ZS_Dead: experience, loot, quest variables) follows via
    /// GlobalEventDispatcher.NpcDied (FightService).
    /// </summary>
    public class NpcDeathDomain
    {
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly PhysicsService _physicsService;
        [Inject] private readonly AudioService _audioService;


        /// <param name="killer">Who killed the NPC, null if nobody did (a script, a fall).</param>
        public void Die(NpcContainer npc, NpcContainer killer = null)
        {
            npc.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            Logger.Log($"[NpcDeath] {npc.Instance.GetName(NpcNameSlot.Slot0)} is dead.", LogCat.Npc);

            // Dead is 0 hit points like in the engine (C_Kill, finishing move) - a finished off NPC kept the
            // knockout's 1 HP on its bar.
            if (npc.Vob.GetAttribute((int)NpcAttribute.HitPoints) > 0)
            {
                npc.Vob.SetAttribute((int)NpcAttribute.HitPoints, 0);
                npc.Instance.SetAttribute(NpcAttribute.HitPoints, 0);
                GlobalEventDispatcher.NpcAttributeChanged.Invoke(npc, NpcAttribute.HitPoints);
            }

            // An NPC out of sight (culled) has no GameObject. It only gets the dead state.
            var prefabProps = npc.PrefabProps;
            if (prefabProps?.AnimationSystem != null)
            {
                // Death takes priority over everything: drop queued AI actions (e.g. a GoToWp/UseMob enqueued the same
                // frame), stop running animations (e.g. s_walk still looping) and play the dying animation directly.
                // Otherwise AiHandler's dead-NPC branch would play them out on the corpse before dying.
                npc.Props.AnimationQueue.Clear();
                // The running action (a walk) would go on moving the corpse.
                npc.Props.CurrentAction = new None(new AnimationAction(), npc);
                prefabProps.AnimationSystem.StopAllAnimations();
                _physicsService.DisablePhysicsForNpc(prefabProps);

                var animName = _animationService.GetAnimationName(VmGothicEnums.AnimationType.DeadB, npc);
                prefabProps.AnimationSystem.PlayAnimation(animName);
                PlayDeathScream(npc);
            }

            GlobalEventDispatcher.NpcDied.Invoke(npc, killer);
        }

        /// <summary>
        /// Like the engine (OpenGothic: emitSoundSVM("SVM_%d_DEAD")), humans scream when they die. Monsters have their
        /// death sound in their animation.
        /// </summary>
        private void PlayDeathScream(NpcContainer npc)
        {
            if (npc.Instance.Voice <= 0 || npc.PrefabProps.NpcSound == null)
                return;

            var clip = _audioService.GetRandomSoundClip($"SVM_{npc.Instance.Voice}_DEAD");
            if (clip != null)
                npc.PrefabProps.NpcSound.PlayOneShot(clip);
        }
    }
}
