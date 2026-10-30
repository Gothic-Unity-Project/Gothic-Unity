using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Vector3 = UnityEngine.Vector3;

namespace Gothic.Core.Services.Npc
{
    public class NpcAiService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        // Reused by SendPerceptionToWitnesses() on every hit.
        private readonly List<NpcContainer> _witnessBuffer = new();


        public void ExtNpcPerceptionEnable(NpcInstance npc, VmGothicEnums.PerceptionType perception, int function)
        {
            npc.GetUserData().Props.Perceptions[perception] = function;
        }

        public void ExtNpcPerceptionDisable(NpcInstance npc, VmGothicEnums.PerceptionType perception)
        {
            npc.GetUserData().Props.Perceptions[perception] = -1;
        }

        /// <summary>
        /// Call an NPC Perception (active like Assess_Player or passive like Assess_Talk are possible).
        /// </summary>
        public void ExecutePerception(VmGothicEnums.PerceptionType type, NpcProperties properties, NpcInstance self, NpcInstance victim, NpcInstance other)
        {
            // Perception isn't set
            if (!properties.Perceptions.TryGetValue(type, out var perceptionFunction))
            {
                return;
            }
            // Perception is disabled
            else if (perceptionFunction < 0)
            {
                return;
            }

            var oldSelf = _gameStateService.GothicVm.GlobalSelf;
            var oldVictim = _gameStateService.GothicVm.GlobalVictim;
            var oldOther = _gameStateService.GothicVm.GlobalOther;

            _gameStateService.GothicVm.GlobalSelf = self;

            if(other != null)
            {
                _gameStateService.GothicVm.GlobalOther = other;
            }

            if(victim != null)
            {
                _gameStateService.GothicVm.GlobalVictim = victim;
            }

            // The finally block ensures a throwing perception function doesn't leave the globals
            // polluted for every subsequent script call of all NPCs.
            //
            // We also catch here: an uncaught exception thrown from inside a Daedalus external
            // (invoked by the native VM while executing this perception function) unwinds back
            // through the native VM's own call frames instead of returning normally. Repeated
            // occurrences (e.g. one broken external firing every perception tick for every NPC)
            // have been observed to corrupt the VM's internal stack ("Internal Exception: stack
            // overflow" / "illegal access ... OTHER" / "tried to pop_instance but frame does not
            // contain an instance"), eventually crashing the whole process natively. Catching here
            // won't undo damage already done to the VM by an external further down the call chain,
            // but it stops this specific call from cascading further and gives a clear, attributable
            // log instead of an anonymous NullReferenceException.
            try
            {
                _gameStateService.GothicVm.Call(perceptionFunction);
            }
            catch (Exception e)
            {
                Logger.LogError($"Perception '{type}' (self={self?.GetName(NpcNameSlot.Slot0)}) threw: {e.Message}",
                    LogCat.Ai);
            }
            finally
            {
                _gameStateService.GothicVm.GlobalSelf = oldSelf;
                _gameStateService.GothicVm.GlobalVictim = oldVictim;
                _gameStateService.GothicVm.GlobalOther = oldOther;
            }
        }

        public void ExtNpcSetPerceptionTime(NpcInstance npc, float time)
        {
            npc.GetUserData().Props.PerceptionTime = time;
        }

        public void ExtAiSetWalkMode(NpcInstance npc, VmGothicEnums.WalkMode walkMode)
        {
            npc.GetUserData()!.Vob.AiHuman.WalkMode = (int)walkMode;
        }

        public void ExtAiGoToWp(NpcInstance npc, string wayPointName)
        {
            npc.GetUserData()!.Props.AnimationQueue.Enqueue(new GoToWp(
                new AnimationAction(wayPointName),
                npc.GetUserData()));
        }

        public void ExtAiAlignToWp(NpcInstance npc)
        {
            npc.GetUserData()!.Props.AnimationQueue.Enqueue(new AlignToWp(new AnimationAction(), npc.GetUserData()));
        }

        public void ExtAiGoToFp(NpcInstance npc, string freePointName)
        {
            npc.GetUserData()!.Props.AnimationQueue.Enqueue(new GoToFp(
                new AnimationAction(freePointName),
                npc.GetUserData()));
        }

        /// <summary>
        /// freeLOS - Free Line Of Sight == ignoreFOV
        /// fov = 50 - OpenGothic assumes 100 fov for NPCs
        /// fov = 30 - We reuse this for Focus angle during AI_Attack()
        /// </summary>
        public bool ExtNpcCanSeeNpc(NpcInstance self, NpcInstance other, bool freeLOS, float fov = 50f)
        {
            return _npcHelperService.CanSeeNpc(self, other, freeLOS, fov);
        }

        public void ExtNpcClearAiQueue(NpcInstance npc)
        {
            npc.GetUserData().Props.AnimationQueue.Clear();
        }

        public void ExtAttack(NpcInstance npc)
        {
            var npcContainer = npc.GetUserData()!;
            npcContainer.Props.AnimationQueue.Enqueue(new Attack(
                new AnimationAction(),
                npcContainer));
        }

        public void ExtAiGoToNextFp(NpcInstance npc, string fpNamePart)
        {
            var npcContainer = npc.GetUserData();
            npcContainer.Props.AnimationQueue.Enqueue(new GoToNextFp(
                new AnimationAction(fpNamePart),
                npcContainer));
        }

        public void ExtAiWait(NpcInstance npc, float seconds)
        {
            var npcContainer = npc.GetUserData();
            npcContainer.Props.AnimationQueue.Enqueue(new Wait(
                new AnimationAction(float0: seconds),
                npcContainer));
        }

        public void ExtAiGoToNpc(NpcInstance self, NpcInstance other)
        {
            if (other == null)
            {
                return;
            }

            self.GetUserData().Props.AnimationQueue.Enqueue(new GoToNpc(
                new AnimationAction(instance0: other),
                self.GetUserData()));
        }

        public void ExtAiPlayAni(NpcInstance npc, string name)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(name), npc.GetUserData()));
        }

        public void PlayAttackAni(NpcInstance npc, string name, FightAiMove move, NpcInstance moveTarget)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new AttackPlayAni(
                new AnimationAction(name, int0: (int)move, instance0: moveTarget),
                npc.GetUserData()));
        }

        /// <summary>
        /// AI_StartState: the new state gets the scripts' current other and victim.
        /// </summary>
        public void ExtAiStartState(NpcInstance npc, int action, bool stopCurrentState, string wayPointName)
        {
            var other = (NpcInstance)_gameStateService.GothicVm.GlobalOther;
            var victim = (NpcInstance)_gameStateService.GothicVm.GlobalVictim;
            StartState(npc, action, stopCurrentState, wayPointName, other, victim, true);
        }

        /// <param name="isScriptCall">False for state changes of the engine itself (knockout): like the engine, the old
        /// state's _End isn't called and the queue doesn't wait for dialog lines - ZS_Attack_End queued putting the
        /// weapon away and a new state, which replaced ZS_Unconscious.</param>
        public void StartState(NpcInstance npc, int action, bool stopCurrentState, string wayPointName,
            NpcInstance other, NpcInstance victim, bool isScriptCall)
        {
            var container = npc.GetUserData();

            // Like the engine, AI_StartState(..., 1, ...) ends the current state with its _End function first -
            // StartState calls it when the queue reaches it. Zeroing it here meant it never ran: a G1 dialog that ends
            // in a fight (AI_StartState(self, ZS_Attack, 1, "")) skipped ZS_Talk_End, the hero kept the dialog's
            // AIV_INVINCIBLE and nobody fought him. 0 starts the new state without the old one's _End.
            var endFunction = 0;
            if (stopCurrentState)
            {
                // Called from the state's own _End (ZS_Attack_End: AI_StartState after "never try that again") or after
                // it: the state already ended, its _End would run a second time.
                var hasStateEnded = container.Props.CurrentLoopState is NpcProperties.LoopState.End or
                    NpcProperties.LoopState.AfterEnd;

                // Abandon the current state's actions so the new one starts next frame - but like the engine
                // (AI_StartState is an AI message) dialog lines still queued are said first, and a queued sheath or
                // stand up plays out.
                var isSheathOrStandUpPending = container.Props.CurrentAction is UndrawWeapon or StandUp ||
                                               container.Props.AnimationQueue.Any(i => i is UndrawWeapon or StandUp);
                var isQueueCleared = !isScriptCall || (!IsTalking(container) && !isSheathOrStandUpPending);

                // A second AI_StartState in the same frame: the first one, cleared below, still carried the old end.
                // Left in the queue, the first one runs it - taking it here too ran it twice.
                var pendingEnd = isQueueCleared
                    ? container.Props.AnimationQueue.OfType<StartState>().Select(i => i.EndFunction)
                        .LastOrDefault(i => i != 0)
                    : 0;
                var stateEnd = container.Props.StateEnd != 0 ? container.Props.StateEnd : pendingEnd;
                endFunction = hasStateEnded || !isScriptCall ? 0 : stateEnd;
                container.Props.StateEnd = 0;
                container.Props.CurrentWayPoint = null; // forces GoToWp to use nearest WP, not stale pre-interrupt WP

                if (isQueueCleared)
                    container.PrefabProps?.AiHandler?.ClearState(false);
            }

            container.Props.AnimationQueue.Enqueue(new StartState(
                new AnimationAction(int0: action, int1: endFunction, bool0: stopCurrentState, string0: wayPointName,
                    instance0: other, instance1: victim),
                container));
        }

        /// <summary>
        /// A line being said, or lines of a dialog (or its start/stop) still waiting in the NPC's queue.
        /// </summary>
        private static bool IsTalking(NpcContainer container)
        {
            return container.Props.CurrentAction is Output or OutputSvm ||
                   container.Props.AnimationQueue.Any(i => i is Output or OutputSvm or StartProcessInfos or
                       StopProcessInfos);
        }

        public void ExtAiLookAt(NpcInstance npc, string wayPointName)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new LookAt(new AnimationAction(wayPointName), npc.GetUserData()));
        }

        public void ExtAiAlignToFp(NpcInstance npc)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new AlignToFp(new AnimationAction(), npc.GetUserData()));
        }

        public void ExtAiLookAtNpc(NpcInstance npc, NpcInstance other)
        {
            if (other == null)
            {
                return;
            }

            npc.GetUserData().Props.AnimationQueue.Enqueue(new LookAtNpc(
                new AnimationAction(instance0: other),
                npc.GetUserData()));
        }

        public void ExtAiStopLookAt(NpcInstance npc)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new StopLookAtNpc(
                new AnimationAction(),
                npc.GetUserData()));
        }

        public void ExtAiContinueRoutine(NpcInstance npc)
        {
            if (npc == null || npc.GetUserData() == null)
                return;
            npc.GetUserData().Props.AnimationQueue.Enqueue(new ContinueRoutine(new AnimationAction(), npc.GetUserData()));
        }

        public void ExtAiUseMob(NpcInstance npc, string target, int state)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new UseMob(
                new AnimationAction(target, state),
                npc.GetUserData()));
        }

        public void ExtAiStandUp(NpcInstance npc)
        {
            // FIXME - Implement remaining task from G1 documentation:
            // * Ist der Nsc in einem Animationsstate, wird die passende Rücktransition abgespielt (e.g. item states).
            var container = npc.GetUserData();
            // Reset immediately (not via queue) so Daedalus C_BodyStateContains checks in the same ZS_*_Loop tick see BsStand.
            // A seated NPC still sits during the back transition below.
            container.Props.BodyState = VmGothicEnums.BodyState.BsStand;

            // G1 docs: AI_StandUp plays the back transitions of a mob (S1 -> S0 -> Stand) - unless one is already
            // running or queued (e.g. ZS_SitAround_End: AI_UseMob(BENCH, -1)).
            var props = container.Props;
            var isBackTransitionPending = props.CurrentAction is UseMob { IsStopUsingMob: true } ||
                                          props.AnimationQueue.OfType<UseMob>().Any(u => u.IsStopUsingMob);
            if (container.IsUsingMob() && !isBackTransitionPending)
            {
                var scheme = container.PrefabProps.CurrentInteractable.Props.GetVisualScheme();
                props.AnimationQueue.Enqueue(new UseMob(new AnimationAction(scheme, -1), container));
            }

            props.AnimationQueue.Enqueue(new StandUp(new AnimationAction(), container));
        }

        /// <summary>
        /// G1 docs: like AI_StandUp, but pops out of a mob without its back transitions.
        /// </summary>
        public void ExtAiStandUpQuick(NpcInstance npc)
        {
            var container = npc.GetUserData();
            container.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            container.Props.AnimationQueue.Enqueue(new StandUp(new AnimationAction(), container));
        }

        /// <summary>
        /// The fight's turn move (FA tables MOVE_TURN): it ends once the enemy is within toleranceDegrees (the focus
        /// check is 30) or after 2 s - not at 1 degree on a moving target.
        /// </summary>
        public void ExtAiTurnToNpcInFight(NpcInstance npc, NpcInstance other, float toleranceDegrees)
        {
            if (other == null)
                return;

            npc.GetUserData().Props.AnimationQueue.Enqueue(new TurnToNpc(
                new AnimationAction(instance0: other, float0: toleranceDegrees),
                npc.GetUserData()));
        }

        public void ExtAiTurnToNpc(NpcInstance npc, NpcInstance other)
        {
            if (other == null)
            {
                return;
            }

            npc.GetUserData().Props.AnimationQueue.Enqueue(new TurnToNpc(
                new AnimationAction(instance0: other),
                npc.GetUserData()));
        }

        public void ExtAiPlayAniBs(NpcInstance npc, string name, int bodyState)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new PlayAniBs(new AnimationAction(name, bodyState), npc.GetUserData()));
        }

        public void ExtAiUnequipArmor(NpcInstance npc)
        {
            npc.GetUserData().Props.BodyData.Armor = 0;
        }

        /// <summary>
        /// Daedalus needs an int value.
        /// </summary>
        public int ExtNpcGetStateTime(NpcInstance npc)
        {
            // If there is no active running state, we immediately assume the current routine is running since the start of all beings.
            if (!npc.GetUserData().Props.IsStateTimeActive)
            {
                return int.MaxValue;
            }

            return (int)npc.GetUserData().Props.StateTime;
        }

        public void ExtNpcSetStateTime(NpcInstance npc, int seconds)
        {
            npc.GetUserData().Props.StateTime = seconds;
        }

        /// <summary>
        /// State means the final state where the animation shall go to.
        /// example:
        /// * itemId=xyz (ItFoBeer)
        /// * animationState = 0
        /// * ItFoBeer is of visual_scheme = Potion
        /// * expected state is t_Potion_Stand_2_S0 --> s_Potion_S0
        /// </summary>
        public void ExtAiUseItemToState(NpcInstance npc, int itemId, int animationState)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new UseItemToState(
                new AnimationAction(int0: itemId, int1: animationState),
                npc.GetUserData()));
        }

        public bool ExtNpcWasInState(NpcInstance npc, uint action)
        {
            return npc.GetUserData().Vob.LastAiState == action;
        }

        public VmGothicEnums.BodyState ExtGetBodyState(NpcInstance npc)
        {
            return npc.GetUserData().Props.BodyState;
        }

        /// <summary>
        /// Return position distance in cm.
        /// </summary>
        public int ExtNpcGetDistToNpc(NpcInstance npc1, NpcInstance npc2)
        {
            if (npc1 == null || npc2 == null)
            {
                return int.MaxValue;
            }

            var npc1Pos = npc1.GetUserData().Go.transform.position;

            Vector3 npc2Pos;
            // If hero: use camera position (VR head position is most accurate)
            if (npc2.Index == _gameStateService.GothicVm.GlobalHero?.Index)
            {
                npc2Pos = Camera.main!.transform.position;
            }
            else
            {
                var go = npc2.GetUserData().Go;

                // e.g. Triggered at Grd_214_Torwache_NODUSTY_Condition as Dusty is not yet spawned.
                // Hint: Could be optimized/overcome if we copy pos+rot between GO and ZenKitVob each frame.
                if (go == null)
                    return int.MaxValue;
                else
                    npc2Pos = go.transform.position;
            }

            return (int)(Vector3.Distance(npc1Pos, npc2Pos) * 100);
        }

        /// <summary>
        /// Return height difference in cm.
        /// </summary>
        public int ExtNpcGetHeightToNpc(NpcInstance npc1, NpcInstance npc2)
        {
            if (npc1 == null || npc2 == null)
                return 0;

            var npc1Pos = npc1.GetUserData().Go.transform.position;

            Vector3 npc2Pos;
            // If hero
            if (npc2.Id == 0)
                npc2Pos = Camera.main!.transform.position;
            else
                npc2Pos = npc2.GetUserData().Go.transform.position;

            return (int)((npc2Pos.y - npc1Pos.y) * 100);
        }

        public void ExtAiDrawWeapon(NpcInstance npc)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new DrawWeapon(new AnimationAction(), npc.GetUserData()));
        }

        public void ExtAiReadyRangedWeapon(NpcInstance npc)
        {
            // int0 == 1 --> DrawWeapon picks the equipped ranged weapon instead of the melee one.
            npc.GetUserData().Props.AnimationQueue.Enqueue(new DrawWeapon(new AnimationAction(int0: 1), npc.GetUserData()));
        }

        public void ExtAiUndrawWeapon(NpcInstance npc)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new UndrawWeapon(new AnimationAction(), npc.GetUserData()));
        }

        public bool ExtNpcIsDead(NpcInstance npcInstance)
        {
            // FIXME - BodyState is runtime-only and lost on NPC reload (e.g. world reload respawns the NPC alive).
            // A permanent death flag needs to be persisted in SaveGame state and checked here instead.
            return npcInstance.GetUserData()?.Props.BodyState == VmGothicEnums.BodyState.BsDead;
        }

        public bool ExtNpcIsInState(NpcInstance npc, int state)
        {
            var container = npc.GetUserData();
            if (container == null)
                return false;

            // The hero runs no Daedalus states. Knocked out, he is in ZS_Unconscious for the scripts - their
            // C_NpcIsDown(other) ends the fight against him.
            if (container.IsHero() && container.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return _gameStateService.GothicVm.GetSymbolByIndex(state)?.Name is "ZS_UNCONSCIOUS" or "ZS_MAGICSLEEP";

            return container.Vob.CurrentStateIndex == state;
        }

        public bool ExtNpcIsPlayer(NpcInstance npc)
        {
            // NpcService.CacheHero() calls Vm.InitInstance(heroInstance) BEFORE assigning Vm.GlobalHero.
            // A (mod) script calling Npc_IsPlayer during that window finds no hero yet - nobody is the player then.
            var globalHero = _gameStateService.GothicVm.GlobalHero;
            return globalHero != null && npc.Index == globalHero.Index;
        }

        public ItemInstance ExtGetEquippedArmor(NpcInstance npc)
        {
            var armor = npc.GetUserData().Props.EquippedItems
                .FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatArmor);

            return armor;
        }

        public bool ExtNpcHasEquippedArmor(NpcInstance npc)
        {
            return ExtGetEquippedArmor(npc) != null;
        }

        /// <summary>
        /// The scripts ask for a category (FMODE_MELEE = 1h or 2h, FMODE_FAR = bow or crossbow), our fight mode is the
        /// weapon itself. Compared directly, only 1h and crossbow matched: a guard with a two-hander or a bow counted
        /// as unarmed and drew his weapon again and again (OpenGothic npc_isinfightmode).
        /// </summary>
        public bool ExtNpcIsInFightMode(NpcInstance npc, VmGothicEnums.FightMode fightMode)
        {
            return ((VmGothicEnums.WeaponState)npc.GetUserData().Vob.FightMode).ToFightMode() == fightMode;
        }

        public bool ExtNpcOwnedByNpc(ItemInstance item, NpcInstance npc)
        {
            if (item == null)
            {
                return false;
            }

            return item.Owner == npc.Index;
        }

        public VmGothicEnums.Attitude ExtGetAttitude(NpcInstance self, NpcInstance other)
        {
            var npc1 = self.GetUserData();
            var npc2 = other.GetUserData();
            if (npc1 == null || npc2 == null)
            {
                return VmGothicEnums.Attitude.Neutral;
            }

            return _npcHelperService.GetPersonAttitude(npc1, npc2);
        }

        public VmGothicEnums.Attitude ExtGetPermAttitude(NpcInstance self, NpcInstance other)
        {
            var npc1 = self.GetUserData();
            var npc2 = other.GetUserData();
            if (npc1 == null || npc2 == null)
                return VmGothicEnums.Attitude.Neutral;

            return _npcHelperService.GetPermanentAttitude(npc1, npc2);
        }

        /// <summary>
        /// HINT: These values are only used when checking the attitude towards the player
        /// HINT: for attitudes between NPC we directly use the guild attitude
        /// </summary>
        public void ExtSetAttitude(NpcInstance npc, VmGothicEnums.Attitude value)
        {
            // Like the engine, the permanent attitude replaces the temporary one as well.
            var vob = npc.GetUserData().Vob;
            vob.Attitude = (int)value;
            vob.AttitudeTemp = (int)value;
        }
        
        /// <summary>
        /// HINT: These values are only used when checking the attitude towards the player
        /// HINT: for attitudes between NPC we directly use the guild attitude
        /// </summary>
        public void ExtSetTempAttitude(NpcInstance npc, VmGothicEnums.Attitude value)
        {
            npc.GetUserData().Vob.AttitudeTemp = (int)value;
        }

        public bool ExtGetTarget(NpcInstance npc)
        {
            var target = npc.GetUserData().Props.TargetNpc;

            if (target == null)
            {
                // Like the engine (OpenGothic npc_gettarget), no target means no "other" either - scripts mustn't act
                // on the one from before.
                _gameStateService.GothicVm.GlobalOther = null;
                return false;
            }

            // Npc_GetTarget() also fills >other< with the target - scripts use it immediately afterwards.
            _gameStateService.GothicVm.GlobalOther = target;
            // ...and AI_Attack after it fights this target (engine: the target is the enemy), not a stale enemy.
            npc.GetUserData().Props.EnemyNpc = target;
            return true;
        }

        public void ExtSetTarget(NpcInstance npc, NpcInstance target)
        {
            var props = npc.GetUserData().Props;
            props.TargetNpc = target;
            if (target != null)
                props.EnemyNpc = target;
        }

        /// <summary>
        /// Npc_GetNextTarget: the nearest NPC the NPC senses and is hostile to becomes target and enemy (and "other").
        /// ZS_Attack_Loop looks for the next one this way once its enemy is down. Without one the target is cleared,
        /// like the engine - the next Npc_GetTarget doesn't return the downed one again.
        /// </summary>
        public bool ExtGetNextTarget(NpcInstance npc)
        {
            var self = npc.GetUserData();
            if (self?.Go == null)
                return false;

            var closest = FindNearestHostile(self);
            if (closest == null)
            {
                self.Props.TargetNpc = null;
                _gameStateService.GothicVm.GlobalOther = null;
                return false;
            }

            self.Props.TargetNpc = closest.Instance;
            self.Props.EnemyNpc = closest.Instance;
            _gameStateService.GothicVm.GlobalOther = closest.Instance;
            return true;
        }

        /// <summary>
        /// The nearest NPC within the senses range that the NPC is hostile to and senses (PERC_ASSESSENEMY and
        /// Npc_GetNextTarget). Not the downed (corpses, knocked out) and not fellow guild members, whatever the
        /// attitude table says - the scripts never fight them this way.
        /// </summary>
        private NpcContainer FindNearestHostile(NpcContainer self)
        {
            var npc = self.Instance;
            var selfPosition = self.Go.transform.position;
            var sensesRange = npc.SensesRange / 100f;
            var closestSqrDistance = sensesRange * sensesRange;
            NpcContainer closest = null;

            // FIXME - Performance - Can we clean this up to support only spawned and visible NPCs/Monsters?
            //         A spatial lookup (e.g. the culling system's distance buckets) would avoid the full scan.
            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                // Fast-fail checks in order of cheapest first
                if (candidate.Props == null || candidate.Go == null || candidate.Instance.Index == npc.Index)
                    continue;
                if (candidate.IsDown() || candidate.Instance.Guild == npc.Guild)
                    continue;

                // Range and closest-so-far gates before the expensive attitude and senses checks
                // (senses may include a line-of-sight raycast for see-only monsters).
                var sqrDistance = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDistance >= closestSqrDistance)
                    continue;

                if (ExtGetAttitude(npc, candidate.Instance) != VmGothicEnums.Attitude.Hostile)
                    continue;

                if (!_npcHelperService.CanSenseNpc(npc, candidate.Instance, true))
                    continue;

                closestSqrDistance = sqrDistance;
                closest = candidate;
            }

            return closest;
        }

        /// <summary>
        /// The perceptions for every NPC who registered them and sees the attacker within their range (Perc_SetRange,
        /// capped by the senses range): victim = the victim, other = the attacker. Not the two themselves, not the
        /// hero, not downed or culled NPCs. One pass for all perceptions, over a reused copy of the NPC list (a
        /// perception function can insert or remove NPCs).
        /// </summary>
        public void SendPerceptionToWitnesses(NpcContainer victim, NpcContainer attacker,
            params VmGothicEnums.PerceptionType[] perceptions)
        {
            if (attacker?.Go == null)
                return;

            var attackerPosition = attacker.Go.transform.position;
            _witnessBuffer.Clear();
            _witnessBuffer.AddRange(_multiTypeCacheService.NpcCache);

            foreach (var witness in _witnessBuffer)
            {
                if (witness == victim || witness == attacker || witness.Props == null || witness.Go == null ||
                    !witness.Go.activeInHierarchy || witness.IsDown() || witness.IsHero())
                    continue;

                var distance = (witness.Go.transform.position - attackerPosition).magnitude;
                var sensesRange = witness.Instance.SensesRange / 100f;
                bool? isSeen = null;
                foreach (var perception in perceptions)
                {
                    if (!witness.Props.Perceptions.TryGetValue(perception, out var function) || function < 0)
                        continue;
                    if (distance > Mathf.Min(_npcHelperService.GetPerceptionRange(perception), sensesRange))
                        continue;

                    // NPCs behind walls don't come running.
                    isSeen ??= ExtNpcCanSeeNpc(witness.Instance, attacker.Instance, true);
                    if (isSeen == false)
                        break;

                    ExecutePerception(perception, witness.Props, witness.Instance, victim.Instance, attacker.Instance);
                }
            }

            _witnessBuffer.Clear();
        }

        public void Npc_SendPassivePerc(NpcInstance npc,VmGothicEnums.PerceptionType perc, NpcInstance victim, NpcInstance other)
        {
            ExecutePerception(perc, npc.GetUserData().Props, npc, victim, other);
        }

        public void ExtSetTrueGuild(NpcInstance npc, int guild)
        {
            npc.GetUserData().Props.TrueGuild = (VmGothicEnums.Guild) guild;
        }

        public int ExtGetTrueGuild(NpcInstance npc)
        {
            var npcUserData = npc.GetUserData();
            var npcGuild  = npcUserData.Props.TrueGuild;

            return npcGuild == 0 ? // No True Guild
                npc.Guild : (int)npcGuild;
        }
        

        public void UpdateEnemyNpc(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            selfNpc.Props.EnemyNpc = FindNearestHostile(selfNpc)?.Instance;
        }

        public void ExtSetRefuseTalk(NpcInstance self, int refuseSeconds)
        {
            self.GetUserData().Props.RefuseTalkTimer = refuseSeconds;
        }

        public bool ExtRefuseTalk(NpcInstance self)
        {
            return self.GetUserData().Props.RefuseTalkTimer > 0f;
        }
    }
}
