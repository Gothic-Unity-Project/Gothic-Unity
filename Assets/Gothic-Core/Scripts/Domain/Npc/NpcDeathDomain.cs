using System.Collections;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
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
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly UnityMonoService _unityMonoService;


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

        /// <summary>
        /// After Die(): 0 hit points, then ZS_Dead and ZS_Dead_Loop with the killer as 'other' (in the next frame),
        /// then the witnesses learn about the murder (PERC_ASSESSMURDER).
        /// </summary>
        public void StartDeadState(NpcContainer dead, NpcContainer killer)
        {
            // Dead is 0 hit points like the engine (OpenGothic Npc::finishingMove / C_Kill: hit points -max) - a
            // finished off NPC kept the knockout's 1 HP.
            if (dead.Vob.GetAttribute((int)NpcAttribute.HitPoints) > 0)
            {
                _npcService.SetAttribute(dead, NpcAttribute.HitPoints, 0);
            }

            // Set at once so AiHandler.Start() on the corpse doesn't call ZS_Dead a second time.
            dead.IsZsDeadCalled = true;

            // ZS_Dead (B_CheckDeadMissionNPCs + B_GiveDeathInv etc.) takes ~280ms in G2 — too heavy
            // to run synchronously in the hit frame. Defer by one frame so the frame spike is gone.
            _unityMonoService.StartCoroutine(CallZsDeadDeferred(dead.Instance, killer));

            Logger.Log($"[FightService.OnNpcDied] {dead.Instance.GetName(NpcNameSlot.Slot0)} died — ZS_Dead deferred " +
                       $"(killer: {killer.Instance.GetName(NpcNameSlot.Slot0)})", LogCat.Npc);
        }

        private IEnumerator CallZsDeadDeferred(NpcInstance dead, NpcContainer killer)
        {
            yield return null;
            RunZsDead(dead, killer);
            _npcAiService.BroadcastDownPerception(dead.GetUserData(), killer, VmGothicEnums.PerceptionType.AssessMurder);
        }

        /// <summary>
        /// ZS_Dead and ZS_Dead_Loop of a dead NPC with the killer as 'other', the state bookkeeping like the engine.
        /// Also for an NPC killed before it spawned (AiHandler: dead on spawn, e.g. Nek) - without a killer it is its
        /// own 'other' (no experience). Not for a corpse from a save: its state is ZS_Dead already.
        /// </summary>
        public void RunZsDead(NpcInstance dead, NpcContainer killer)
        {
            if (dead.GetUserData() is { } deadNpc)
                deadNpc.IsZsDeadCalled = true;

            var vm = _gameStateService.GothicVm;
            var zsDeadSym = vm.GetSymbolByName(DaedalusConst.ZsDead);
            if (zsDeadSym == null)
            {
                Logger.LogWarning("[FightService.OnNpcDied] ZS_Dead symbol not found", LogCat.Npc);
                return;
            }

            // Like the engine's state change: the state he died in is the last one - G1's B_DeathXP pays nothing for
            // finishing off an unconscious human (Npc_WasInState(self, ZS_Unconscious)).
            var deadContainer = dead.GetUserData();
            if (deadContainer?.Vob != null)
            {
                deadContainer.Vob.LastAiState = deadContainer.Vob.CurrentStateIndex;
                deadContainer.Vob.CurrentStateIndex = zsDeadSym.Index;
                deadContainer.Vob.CurrentStateName = zsDeadSym.Name;
            }

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            vm.GlobalSelf = dead;
            // Like the engine (OpenGothic onNoHealth: setOther(lastHit)) 'other' is the real killer - ZS_Dead gives XP
            // only if he's the hero or in his party (AIV_PARTYMEMBER: Mordrag leading him; AIV_MM_PARTYMEMBER: the
            // hero's summons, passed as themselves), and G2 marks only the hero's victims as killed by him.
            vm.GlobalOther = killer?.Instance ?? dead;
            try
            {
                vm.Call(zsDeadSym.Index);

                // The engine runs ZS_Dead_Loop too - G2 counts killed dragons there (B_DragonKillCounter): the undead
                // dragon's EXTRO video, the teleport to UNDEAD_ENDTELEPORT and UndeadDragonIsDead never happened. The
                // scripts guard it themselves (AIV_TAPOSITION), once is enough.
                var zsDeadLoopSym = vm.GetSymbolByName(DaedalusConst.ZsDeadLoop);
                if (zsDeadLoopSym != null)
                {
                    if (zsDeadLoopSym.HasReturn)
                        vm.Call<int>(zsDeadLoopSym.Index);
                    else
                        vm.Call(zsDeadLoopSym.Index);
                }

                _npcService.SyncHeroInstanceToVob();
                Logger.Log($"[FightService.OnNpcDied] ZS_Dead complete: {dead.GetName(NpcNameSlot.Slot0)}" +
                           $"{(zsDeadLoopSym != null ? " (+ ZS_Dead_Loop)" : "")}", LogCat.Npc);
            }
            catch (System.Exception e)
            {
                Logger.LogError($"[FightService.OnNpcDied] ZS_Dead of {dead.GetName(NpcNameSlot.Slot0)} failed: " +
                                e.Message, LogCat.Npc);
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
            }
        }
    }
}
