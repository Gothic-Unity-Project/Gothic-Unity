using System;
using System.Collections;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Like Gothic, an NPC dies when its HP reach 0, whoever changed them: a hit, or a script via
    /// Npc_ChangeAttribute() (e.g. B_KillNpc()). OpenGothic: Npc::checkHealth(). In order:
    /// 1. it dies (body state, 0 HP, dying animation, scream) and GlobalEventDispatcher.NpcDied tells it,
    /// 2. one frame later ZS_Dead runs like the engine's state: experience, loot, quest variables,
    /// 3. the NPCs around who see it learn about the murder (PERC_ASSESSMURDER).
    /// Owned by NpcService.
    /// </summary>
    public class NpcDeathDomain
    {
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly PhysicsService _physicsService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        // NpcAiService (through NpcHelperService and VobService) leads back to NpcService, which creates this domain -
        // resolved on first use, not while NpcService is built.
        private NpcAiService _npcAiService;
        private NpcAiService NpcAi => _npcAiService ??= ReflexProjectInstaller.DIContainer.Resolve<NpcAiService>();

        private readonly Action _syncHeroInstanceToVob;

        /// <param name="syncHeroInstanceToVob">NpcService.SyncHeroInstanceToVob - ZS_Dead gives the hero experience on
        /// his instance.</param>
        public NpcDeathDomain(Action syncHeroInstanceToVob)
        {
            _syncHeroInstanceToVob = syncHeroInstanceToVob;
        }

        /// <param name="killer">Who killed the NPC, null if nobody did (a script, a fall).</param>
        public void Die(NpcContainer npc, NpcContainer killer = null)
        {
            npc.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            Logger.Log($"[NpcDeath] {npc.Instance.GetName(NpcNameSlot.Slot0)} is dead.", LogCat.Npc);

            // The engine drops a drawn weapon next to the body; it goes back on the belt or back here, like a knockout's
            // (FightService.KnockOut). It stays in the inventory, so it is looted from the body.
            UndrawWeapon.SheathAtOnce(npc);

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
            _unityMonoService.StartCoroutine(RunDeadState(npc, killer));
        }

        /// <summary>
        /// Like the engine, ZS_Dead runs for every death with the killer as other, so the hero gets experience for his
        /// own kills only. It becomes the NPC's state first: G1's B_DeathXP gives none for finishing off a knocked out
        /// human (Npc_WasInState(self, ZS_Unconscious)), the knockout paid already. Deferred by one frame, out of the
        /// frame of the hit. The engine runs its loop once too: G2 counts killed dragons there (B_DragonKillCounter).
        /// </summary>
        private IEnumerator RunDeadState(NpcContainer dead, NpcContainer killer)
        {
            yield return null;

            var vm = _gameStateService.GothicVm;
            var zsDead = vm.GetSymbolByName("ZS_Dead");
            if (zsDead == null)
            {
                Logger.LogWarning("[NpcDeath] ZS_Dead not found - nothing happens after a death.", LogCat.Npc);
                yield break;
            }

            dead.Vob.LastAiState = dead.Vob.CurrentStateIndex;
            dead.Vob.CurrentStateIndex = zsDead.Index;
            dead.Vob.CurrentStateName = zsDead.Name;

            var other = killer?.Instance ?? dead.Instance;
            _vmService.CallAsNpc(zsDead.Index, dead.Instance, other, null, LogCat.Npc);
            var zsDeadLoop = vm.GetSymbolByName("ZS_Dead_Loop");
            if (zsDeadLoop != null)
                _vmService.CallAsNpc(zsDeadLoop.Index, dead.Instance, other, null, LogCat.Npc);

            _syncHeroInstanceToVob();

            if (killer != null && killer != dead)
                NpcAi.SendPerceptionToWitnesses(dead, killer, VmGothicEnums.PerceptionType.AssessMurder);
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
