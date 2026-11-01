using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Creator;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Culling;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Gothic.Core.Services.Caches;
using Logger = Gothic.Core.Logging.Logger;
using Vector3 = UnityEngine.Vector3;

namespace Gothic.Core.Services.Npc
{
    public class NpcAiService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly HeroSpellStateService _heroSpellStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly AnimationService _animationService;

        // Reused by FindNearestHostile() on every perception tick.
        private readonly List<NpcContainer> _awakeNpcs = new();
        // Reused by SendPerceptionToWitnesses() on every hit.
        private readonly List<NpcContainer> _witnessBuffer = new();
        // Npc_SendPassivePerc() of this frame, delivered to the NPCs around the sender in their next AI update.
        private readonly List<(VmGothicEnums.PerceptionType Type, Vector3 Position, NpcInstance Sender,
            NpcInstance Victim, NpcInstance Other, ItemInstance Item)> _sentPassivePerceptions = new();
        private readonly List<(VmGothicEnums.PerceptionType Type, Vector3 Position, NpcInstance Sender,
            NpcInstance Victim, NpcInstance Other, ItemInstance Item)> _deliveredPassivePerceptions = new();
        private int _deliveryFrame = -1;
        // NPCs that talked to the hero (Npc_SetKnowsPlayer in G1's ZS_Talk).
        private readonly HashSet<int> _npcsKnowingPlayer = new();
        private bool _isReadySpellWithoutManaLogged;


        public void Init()
        {
            // Perceptions not delivered yet belong to the NPCs of the world they were sent in.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(ClearPassivePerceptions);
        }

        private void ClearPassivePerceptions()
        {
            _sentPassivePerceptions.Clear();
            _deliveredPassivePerceptions.Clear();
        }

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

            // Like the engine (OpenGothic invokeState), other and victim are this perception's, null included - a
            // left-over victim made B_AssessWarn or B_AssessUseMob see a crime that wasn't there.
            _gameStateService.GothicVm.GlobalSelf = self;
            _gameStateService.GothicVm.GlobalOther = other;
            _gameStateService.GothicVm.GlobalVictim = victim;

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
            var props = npc.GetUserData().Props;
            props.PerceptionTime = time;

            // NPCs that wake up together (a whole camp after loading) would sense in the same frame, forever: a
            // random point in the new interval spreads them. The next perception still comes within one interval.
            props.CurrentPerceptionTime = UnityEngine.Random.Range(0f, time);
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
            var props = npc.GetUserData().Props;
            props.AnimationQueue.Clear();

            // Like the engine (OpenGothic Npc::clearAiQueue), a cleared queue ends a flight: the run stops right away.
            if (props.CurrentAction is Flee)
                props.CurrentAction.StopImmediately();
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
            // G2's Spell_Logic_Sleep puts its victim to sleep itself (AI_StartState(other, ZS_MagicSleep)) - the hero
            // runs no states, it was queued and never happened.
            var container = npc.GetUserData();
            if (container != null && container.IsHero() &&
                _heroSpellStateService.TryApply(container, _gameStateService.GothicVm.GetSymbolByIndex(action)?.Name))
                return;

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

        public void ExtAiPointAt(NpcInstance npc, string pointName)
        {
            var container = npc?.GetUserData();
            if (container?.Props == null)
            {
                Logger.LogWarning($"AI_PointAt: NPC without container - '{pointName}' ignored.", LogCat.Ai);
                return;
            }

            var point = _wayNetService.GetWayNetPoint(pointName);
            if (point == null)
            {
                Logger.LogWarning($"AI_PointAt: {npc.GetName(NpcNameSlot.Slot0)} - no way point '{pointName}'.",
                    LogCat.Ai);
                return;
            }

            container.Props.AnimationQueue.Enqueue(new PointAt(new AnimationAction(), container, () => point.Position));
        }

        public void ExtAiPointAtNpc(NpcInstance npc, NpcInstance other)
        {
            var container = npc?.GetUserData();
            var target = other?.GetUserData();
            if (target == null || container?.Props == null)
            {
                Logger.LogWarning("AI_PointAtNpc: NPC or target without container - ignored.", LogCat.Ai);
                return;
            }

            container.Props.AnimationQueue.Enqueue(new PointAt(new AnimationAction(), container,
                () => target.Go != null ? (Vector3?)target.Go.transform.position : null));
        }

        public void ExtAiStopPointAt(NpcInstance npc)
        {
            var container = npc?.GetUserData();
            container?.Props?.AnimationQueue.Enqueue(new StopPointAt(new AnimationAction(), container));
        }

        /// <summary>
        /// Npc_HasBodyFlag: the body state carries the flag (BS_FLAG_INTERRUPTABLE, BS_FLAG_FREEHANDS).
        /// </summary>
        public bool ExtNpcHasBodyFlag(NpcInstance npc, int flag)
        {
            var props = npc?.GetUserData()?.Props;
            return props != null && ((int)props.BodyState & flag) != 0;
        }

        /// <summary>
        /// Npc_SendSinglePerc, like the engine: the target perceives it, with the sender as 'other' (G1's charm,
        /// shrink and berserk spells).
        /// </summary>
        public void ExtNpcSendSinglePerc(NpcInstance npc, NpcInstance target, int perception)
        {
            var targetProps = target?.GetUserData()?.Props;
            if (npc == null || targetProps == null)
                return;

            ExecutePerception((VmGothicEnums.PerceptionType)perception, targetProps, target, null, npc);
        }

        /// <summary>
        /// Npc_PlayAni: the animation right away, not queued.
        /// </summary>
        public void ExtNpcPlayAni(NpcInstance npc, string animationName)
        {
            npc?.GetUserData()?.PrefabProps?.AnimationSystem?.PlayAnimation(animationName);
        }

        /// <summary>
        /// Npc_GetLookAtTarget: the NPC (or hero) the scripts made it look at.
        /// </summary>
        public NpcInstance ExtNpcGetLookAtTarget(NpcInstance npc)
        {
            var target = npc?.GetUserData()?.PrefabProps?.AnimationHeadHandler?.ScriptTarget;
            if (target == null)
                return null;

            var hero = _gameStateService.GothicVm.GlobalHero as NpcInstance;
            var heroHead = hero?.GetUserData()?.PrefabProps?.Head;
            if (heroHead != null && target == heroHead)
                return hero;

            foreach (var other in _multiTypeCacheService.NpcCache)
            {
                if (other.PrefabProps != null && other.PrefabProps.Head == target)
                    return other.Instance;
            }
            return null;
        }

        public void ExtNpcSetKnowsPlayer(NpcInstance npc)
        {
            if (npc != null)
                _npcsKnowingPlayer.Add(npc.Index);
        }

        public bool ExtNpcKnowsPlayer(NpcInstance npc)
        {
            return npc != null && _npcsKnowingPlayer.Contains(npc.Index);
        }

        /// <returns>cm to the hero, measured like Npc_GetDistToNpc(npc, hero).</returns>
        public int ExtNpcGetDistToPlayer(NpcInstance npc)
        {
            return ExtNpcGetDistToNpc(npc, _gameStateService.GothicVm.GlobalHero as NpcInstance);
        }

        /// <summary>
        /// Npc_IsInCutscene, like the reference engine: in a dialog (there are no cutscenes) - nobody attacks then.
        /// </summary>
        public bool ExtNpcIsInCutscene(NpcInstance npc)
        {
            var dialogs = _gameStateService.Dialogs;
            if (npc == null || !dialogs.IsInDialog)
                return false;

            return ExtNpcIsPlayer(npc) || dialogs.CurrentDialogNpc?.Instance?.Index == npc.Index;
        }

        /// <summary>
        /// Npc_CanSeeSource: the NPC sees who made the noise it perceives ('other').
        /// </summary>
        public bool ExtNpcCanSeeSource(NpcInstance npc)
        {
            var source = _gameStateService.GothicVm.GlobalOther as NpcInstance;
            return npc != null && source != null && _npcHelperService.CanSeeNpc(npc, source, false);
        }

        public void ExtAiStopLookAt(NpcInstance npc)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new StopLookAtNpc(
                new AnimationAction(),
                npc.GetUserData()));
        }

        /// <summary>
        /// AI_Dodge: a jump back, like the engine (OpenGothic: AI_Dodge plays Anim::MoveBack) - the parade jump of the
        /// drawn melee weapon or fists, otherwise the plain jump back. G1's orcs dodge when hit or watching a fight.
        /// </summary>
        public void ExtAiDodge(NpcInstance npc)
        {
            var npcContainer = npc.GetUserData();
            if (npcContainer == null)
            {
                Logger.LogWarning($"AI_Dodge: NPC {npc?.Id} has no Unity data (not spawned?) - skipping.", LogCat.Ai);
                return;
            }

            var weaponState = (VmGothicEnums.WeaponState)npcContainer.Vob.FightMode;
            var animationName = weaponState is VmGothicEnums.WeaponState.Fist or VmGothicEnums.WeaponState.W1H or
                VmGothicEnums.WeaponState.W2H
                ? $"T_{AnimationService.GetWeaponAnimationPrefix(weaponState)}PARADEJUMPB"
                : "T_JUMPB";

            npcContainer.Props.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(animationName), npcContainer));
        }

        /// <summary>
        /// Npc_StopAni: stops an animation right away, not queued. A queued AI_PlayAni of it then ends (PlayAni). G2's
        /// praying NPCs stop their random prayer with it before they stand up.
        /// </summary>
        public void ExtNpcStopAni(NpcInstance npc, string name)
        {
            var animationSystem = npc.GetUserData()?.PrefabProps?.AnimationSystem;
            if (animationSystem == null)
            {
                Logger.LogWarning($"Npc_StopAni: NPC {npc?.Id} has no animations (not spawned?) - {name} not stopped.",
                    LogCat.Ai);
                return;
            }

            animationSystem.StopAnimation(name);
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
        /// AI_Flee: one short run step away from the NPC's enemy along the waynet (ZS_Flee's loop repeats it).
        /// </summary>
        public void ExtAiFlee(NpcInstance npc)
        {
            var container = npc.GetUserData();
            container?.Props.AnimationQueue.Enqueue(new Flee(new AnimationAction(), container));
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

        /// <summary>
        /// AI_FinishingMove: walk to the knocked out enemy, face it, kill it with a stab (FinishingMove). Scripts call it
        /// from ZS_Attack_End for hostile enemies - also for the hero.
        /// </summary>
        public void ExtAiFinishingMove(NpcInstance self, NpcInstance other)
        {
            var container = self.GetUserData();
            if (other == null || container == null)
                return;

            container.Props.AnimationQueue.Enqueue(new GoToNpc(new AnimationAction(instance0: other), container));
            container.Props.AnimationQueue.Enqueue(new TurnToNpc(new AnimationAction(instance0: other), container));
            // The stab needs a melee weapon in the hand, but a knockout of the hero puts the attackers' weapons away
            // (FightService.KnockOutHero) - nobody finished him off (found on BoroBongo's branch).
            if (!((VmGothicEnums.WeaponState)container.Vob.FightMode).IsMelee())
                ExtAiDrawWeapon(self);
            container.Props.AnimationQueue.Enqueue(new FinishingMove(new AnimationAction(instance0: other), container));
        }

        public void ExtAiTurnAway(NpcInstance npc, NpcInstance other)
        {
            var container = npc.GetUserData();
            if (other == null || container == null)
                return;

            container.Props.AnimationQueue.Enqueue(new TurnAwayFromNpc(new AnimationAction(instance0: other),
                container));
        }

        public void ExtAiUseItem(NpcInstance npc, int itemId)
        {
            var container = npc.GetUserData();
            if (container == null)
                return;

            // Like the engine, only an item the NPC carries is used.
            var item = _vmCacheService.TryGetItemData(itemId);
            if (item == null || _npcInventoryService.ExtNpcHasItems(npc, item.Index) <= 0)
            {
                Logger.LogWarning($"AI_UseItem: {npc.GetName(NpcNameSlot.Slot0)} doesn't carry item {itemId}",
                    LogCat.Ai);
                return;
            }

            // The item's animation plays (t_Potion_Stand_2_S0: the potion to the mouth) before it works and the NPC
            // puts it away after (t_Potion_S0_2_Stand). Items without both work at once: a missing way back would
            // stall the NPC's queue.
            var hasAnimation = HasItemTransition(item, "STAND", "S0", container) &&
                               HasItemTransition(item, "S0", "STAND", container);
            if (hasAnimation)
                ExtAiUseItemToState(npc, itemId, 0);
            container.Props.AnimationQueue.Enqueue(new UseItem(new AnimationAction(int0: itemId), container));
            if (hasAnimation)
                ExtAiUseItemToState(npc, itemId, -1);
        }

        private bool HasItemTransition(ItemInstance item, string from, string to, NpcContainer container)
        {
            return !string.IsNullOrEmpty(item.SchemeName) && _animationService.GetTrack(
                $"T_{item.SchemeName}_{from}_2_{to}", container.Props.MdsNameBase,
                container.Props.MdsNameOverlay) != null;
        }

        public void ExtAiWhirlAround(NpcInstance npc, NpcInstance other)
        {
            var container = npc?.GetUserData();
            if (other == null || container == null)
            {
                Logger.LogWarning($"AI_WhirlAround: {(container == null ? "the NPC" : "the other NPC")} isn't " +
                                  "spawned - not turning", LogCat.Ai);
                return;
            }

            container.Props.AnimationQueue.Enqueue(new WhirlAround(new AnimationAction(instance0: other), container));
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

            // Not spawned yet (Npc_GetDistToPlayer of an NPC out of range).
            var npc1Go = npc1.GetUserData()?.Go;
            if (npc1Go == null)
                return int.MaxValue;

            var npc1Pos = npc1Go.transform.position;

            Vector3 npc2Pos;
            // If hero: use camera position (VR head position is most accurate)
            if (npc2.IsHero())
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
            if (npc2.IsHero())
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

        /// <summary>
        /// AI_ReadySpell. Like the engine, without mana to invest nothing is drawn.
        /// </summary>
        public void ExtAiReadySpell(NpcInstance npc, int spellId, int investMana)
        {
            var container = npc.GetUserData();
            if (container == null)
                return;

            if (investMana <= 0)
            {
                // Fight loops ask again and again - once is enough to see it.
                if (!_isReadySpellWithoutManaLogged)
                {
                    _isReadySpellWithoutManaLogged = true;
                    Logger.LogWarning($"[AI] AI_ReadySpell({npc.GetName(NpcNameSlot.Slot0)}, {spellId}, " +
                                      $"{investMana}) without mana to invest - no spell drawn. Logged once.",
                        LogCat.Fight);
                }
                return;
            }

            container.Props.AnimationQueue.Enqueue(new ReadySpell(new AnimationAction(int0: spellId, int1: investMana),
                container));
        }

        public void ExtAiUndrawWeapon(NpcInstance npc)
        {
            npc.GetUserData().Props.AnimationQueue.Enqueue(new UndrawWeapon(new AnimationAction(), npc.GetUserData()));
        }

        public bool ExtNpcIsDead(NpcInstance npcInstance)
        {
            // FIXME - Npc_ChangeAttribute() to HP <= 0 doesn't run the death transition yet (only FightService does),
            // e.g. B_KillNpc() doesn't kill then. OpenGothic: Npc::checkHealth().
            return npcInstance.GetUserData()?.Props.IsDead == true;
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
            var sensesRangeSqr = sensesRange * sensesRange;
            var closestSqrDist = float.MaxValue;
            NpcContainer closest = null;

            // Only the hero and the NPCs that are awake: asleep NPCs are far away and frozen. Scanning every NPC of
            // the world on each perception made crowded places stutter (Old Camp: 52 awake of 992).
            var hero = _npcService.GetHeroContainer();
            if (hero != null && IsCloserEnemy(npc, selfPosition, hero, sensesRangeSqr, ref closestSqrDist))
                closest = hero;

            _npcMeshCullingService.CollectAwakeNpcs(_awakeNpcs);
            foreach (var candidate in _awakeNpcs)
            {
                if (IsCloserEnemy(npc, selfPosition, candidate, sensesRangeSqr, ref closestSqrDist))
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

        /// <summary>
        /// Like Gothic (OpenGothic: WorldObjects::sendPassivePerc), a passive perception goes to the NPCs around the
        /// sender, not to the sender itself, and not right away: it's delivered in their next AI update
        /// (DeliverPassivePerceptions()). Running it on the sender, inside its own script call, made guards never
        /// warn each other and re-ran the sender's own reaction (ZS_ObservePlayer -> PERC_ASSESSWARN ->
        /// B_AssessWarn -> ZS_ObservePlayer).
        /// </summary>
        public void Npc_SendPassivePerc(NpcInstance npc,VmGothicEnums.PerceptionType perc, NpcInstance victim, NpcInstance other)
        {
            var sender = npc.GetUserData();
            if (sender?.Go == null)
                return;

            // Dropped without other: the perception scripts (B_AssessWarn, ...) read it and would access null.
            if (other == null)
            {
                Logger.LogWarning($"Npc_SendPassivePerc: {perc} of {npc.GetName(NpcNameSlot.Slot0)} has no other - " +
                    "not sent.", LogCat.Ai);
                return;
            }

            _sentPassivePerceptions.Add((perc, sender.Go.transform.position, npc, victim, other, null));
        }

        /// <summary>
        /// A passive perception about an item (PERC_ASSESSTHEFT): like the engine, the scripts find it in their
        /// global "item" while they react.
        /// </summary>
        public void SendPassivePerception(NpcInstance sender, VmGothicEnums.PerceptionType perc, NpcInstance victim,
            NpcInstance other, ItemInstance item)
        {
            var senderContainer = sender.GetUserData();
            if (senderContainer?.Go == null || other == null)
                return;

            _sentPassivePerceptions.Add((perc, senderContainer.Go.transform.position, sender, victim, other, item));
        }

        /// <summary>
        /// Called by every awake NPC once per frame: runs the passive perceptions sent in the frames before on it, if
        /// it's within the perception's range of the sender (Perc_SetRange()).
        /// </summary>
        public void DeliverPassivePerceptions(NpcInstance self, NpcProperties properties)
        {
            if (_deliveryFrame != Time.frameCount)
            {
                _deliveryFrame = Time.frameCount;
                _deliveredPassivePerceptions.Clear();
                _deliveredPassivePerceptions.AddRange(_sentPassivePerceptions);
                _sentPassivePerceptions.Clear();
            }

            if (_deliveredPassivePerceptions.Count == 0)
                return;

            // The hero doesn't react to perceptions.
            if (self.IsHero())
                return;

            // Like the engine (OpenGothic passivePerceptionProcess): the dead and the unconscious don't notice.
            var selfContainer = self.GetUserData();
            if (selfContainer?.Go == null || properties.IsDead ||
                properties.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return;

            var position = selfContainer.Go.transform.position;
            foreach (var perception in _deliveredPassivePerceptions)
            {
                // The sender itself by object - NPCs of the same instance (a wolf pack, the camp's guards) share the
                // instance index and would all be skipped.
                if (perception.Sender.GetUserData() == selfContainer)
                    continue;

                // Like Gothic: the range of the perception (Perc_SetRange()), else the senses range. Passive
                // perceptions ignore the senses (hear/see) themselves.
                var range = _npcHelperService.GetPerceptionRange(perception.Type);
                if (range >= float.MaxValue)
                    range = self.SensesRange / 100f;
                if ((perception.Position - position).sqrMagnitude > range * range)
                    continue;

                if (perception.Item == null)
                {
                    ExecutePerception(perception.Type, properties, self, perception.Victim, perception.Other);
                    continue;
                }

                // Like the engine, the scripts see this perception's victim even if there is none: B_AssessTheft
                // takes a valid victim for picked pockets, a victim left from an earlier call made theft one.
                var vm = _gameStateService.GothicVm;
                var oldItem = vm.GlobalItem;
                var oldVictim = vm.GlobalVictim;
                vm.GlobalItem = perception.Item;
                vm.GlobalVictim = perception.Victim;
                try
                {
                    ExecutePerception(perception.Type, properties, self, perception.Victim, perception.Other);
                }
                finally
                {
                    vm.GlobalItem = oldItem;
                    vm.GlobalVictim = oldVictim;
                }
            }
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
        

        /// <summary>
        /// The nearest dead NPC this one senses (OpenGothic updateNearestBody, free line of sight), within the
        /// perception's range if it has one.
        /// </summary>
        public NpcInstance FindNearestBody(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            if (selfNpc?.Go == null)
                return null;

            var selfPosition = selfNpc.Go.transform.position;
            var range = Mathf.Min(self.SensesRange / 100f,
                _npcHelperService.GetPerceptionRange(VmGothicEnums.PerceptionType.AssessBody));
            var closestSqrDist = range * range;
            NpcContainer closest = null;

            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate == selfNpc || candidate.Props == null || candidate.Go == null || !candidate.Props.IsDead)
                    continue;

                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist > closestSqrDist || !_npcHelperService.CanSenseNpc(self, candidate.Instance, true))
                    continue;

                closestSqrDist = sqrDist;
                closest = candidate;
            }

            return closest?.Instance;
        }

        public void UpdateEnemyNpc(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            selfNpc.Props.EnemyNpc = FindNearestHostile(selfNpc)?.Instance;
        }

        /// <summary>
        /// The candidate is a hostile NPC self senses, closer than the closest one so far (which it then becomes).
        /// </summary>
        private bool IsCloserEnemy(NpcInstance self, Vector3 selfPosition, NpcContainer candidate, float sensesRangeSqr,
            ref float closestSqrDist)
        {
            // Fast-fail checks in order of cheapest first
            if (candidate.Props == null || candidate.Go == null)
                return false;

            if (candidate.Instance.Index == self.Index)
                return false;

            // The downed (corpses, knocked out) and fellow guild members aren't enemies, whatever the attitude table
            // says - the scripts never fight them this way.
            if (candidate.IsDown() || candidate.Instance.Guild == self.Guild)
                return false;

            // Range and closest-so-far gates before the expensive attitude and senses checks
            // (senses may include a line-of-sight raycast for see-only monsters).
            var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
            if (sqrDist > sensesRangeSqr || sqrDist >= closestSqrDist)
                return false;

            if (ExtGetAttitude(self, candidate.Instance) != VmGothicEnums.Attitude.Hostile)
                return false;

            if (!_npcHelperService.CanSenseNpc(self, candidate.Instance, true))
                return false;

            closestSqrDist = sqrDist;
            return true;
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
