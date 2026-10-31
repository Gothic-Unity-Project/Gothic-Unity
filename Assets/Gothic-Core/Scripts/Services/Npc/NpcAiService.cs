using System;
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

            // A knocked out NPC senses nothing but magic until he stands up (ZS_Unconscious only enables
            // PERC_ASSESSMAGIC) - also in the frames before ZS_Unconscious starts.
            if (type != VmGothicEnums.PerceptionType.AssessMagic && NpcHelperService.IsKnockedOut(self?.GetUserData()))
                return;

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
            // A knocked out NPC lies on the ground: nobody notices him by sight (no enemy, no fighter).
            var otherContainer = other?.GetUserData();
            if (otherContainer != null && NpcHelperService.IsKnockedOut(otherContainer))
                return false;

            // A seated NPC looks the way its body faces - NpcHelperService.CanSeeNpc.
            return _npcHelperService.CanSeeNpc(self, other, freeLOS, fov);
        }

        public void ExtNpcClearAiQueue(NpcInstance npc)
        {
            var queue = npc.GetUserData().Props.AnimationQueue;

            // The startled jump survives the queue being cleared, like the engine's whirl that starts right away.
            // G1 ZS_AssessFighter: B_WhirlAround, then B_SelectWeapon -> B_EquipAndDrawBestMeleeWeapon -> B_FullStop
            // clears the queue again - Ghorri drew his sword with his back to the hero and only turned afterwards.
            var hasWhirl = false;
            foreach (var action in queue)
            {
                if (action is not WhirlAround)
                    continue;
                hasWhirl = true;
                break;
            }

            if (!hasWhirl)
            {
                queue.Clear();
                return;
            }

            var whirls = queue.Where(action => action is WhirlAround).ToList();
            queue.Clear();
            foreach (var whirl in whirls)
                queue.Enqueue(whirl);
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

        public void ExtAiStartState(NpcInstance npc, int action, bool stopCurrentState, string wayPointName)
        {
            var other = (NpcInstance)_gameStateService.GothicVm.GlobalOther;
            var victim = (NpcInstance)_gameStateService.GothicVm.GlobalVictim;

            var container = npc.GetUserData();

            // AI_StartState(..., 1, ...) ends the current state with its _End function like the engine - zeroing it
            // here meant it never ran: a dialog that ends in a fight
            // (AI_StartState(self, ZS_Attack, 1, "")) skipped ZS_Talk_End, the hero kept the dialog's AIV_INVINCIBLE and
            // nobody talked to or fought him anymore. StartState calls it when the queue reaches it.
            var endFunction = 0;
            if (stopCurrentState)
            {
                // Called from the state's own _End (ZS_Attack_End: "$NeverTryThatAgain" + AI_StartState) or after it:
                // the state already ended - its _End ran a second time and the NPC said everything twice.
                // LoopState.End alone means the _End is still to come.
                var hasStateEnded = container.Props.IsInStateEnd ||
                                    container.Props.CurrentLoopState == NpcProperties.LoopState.AfterEnd;
                endFunction = hasStateEnded ? 0 : container.Props.StateEnd;
                container.Props.StateEnd = 0;
                container.Props.CurrentWayPoint = null;

                // Like the engine (AI_StartState is an AI message): lines still queued in a dialog are said first - the
                // fight or the dragon's escape starts after them, not in the middle of the talk.
                if (IsTalking(container))
                {
                    Logger.Log($"[ExtAiStartState] {npc.GetName(NpcNameSlot.Slot0)}: queued after the dialog's lines",
                        LogCat.Ai);
                }
                else if (!container.Props.AnimationQueue.OfType<UndrawWeapon>().Any() &&
                         !container.Props.AnimationQueue.OfType<StandUp>().Any())
                {
                    container.PrefabProps?.AiHandler?.ClearState(false);
                }
            }

            container.Props.AnimationQueue.Enqueue(new StartState(
                new AnimationAction(int0: action, int1: endFunction, bool0: stopCurrentState, string0: wayPointName,
                    instance0: other, instance1: victim),
                container));
        }

        /// <summary>
        /// Lines of a dialog (or its start/stop) still waiting in the NPC's queue.
        /// </summary>
        private static bool IsTalking(NpcContainer container)
        {
            return container.Props.AnimationQueue.Any(i =>
                i is Output or OutputSvm or StartProcessInfos or StopProcessInfos);
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

        public void ExtAiWhirlAround(NpcInstance npc, NpcInstance other)
        {
            if (other == null)
                return;

            npc.GetUserData().Props.AnimationQueue.Enqueue(new WhirlAround(
                new AnimationAction(instance0: other),
                npc.GetUserData()));
        }

        /// <summary>
        /// AI_GotoItem: walks to a world item (G2's wisp shows the hero where items are).
        /// </summary>
        public void ExtAiGotoItem(NpcInstance npc, ItemInstance item)
        {
            var npcContainer = npc?.GetUserData();
            var itemContainer = _npcHelperService.GetWorldItemContainer(npc, item);
            if (npcContainer == null || itemContainer?.Go == null)
            {
                Logger.Log($"[AI_GotoItem] {npc?.GetName(NpcNameSlot.Slot0)}: no world item for '{item?.Name}' - skipped",
                    LogCat.Ai);
                return;
            }

            npcContainer.Props.AnimationQueue.Enqueue(new GoToItem(new AnimationAction(), npcContainer,
                itemContainer.Go));
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

        /// <summary>
        /// AI_TurnAway turns the NPC's back to the other one.
        /// </summary>
        public void ExtAiTurnAway(NpcInstance npc, NpcInstance other)
        {
            if (other?.GetUserData()?.Go == null)
                return;

            npc.GetUserData().Props.AnimationQueue.Enqueue(new TurnAwayFromNpc(
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
        /// AI_UseItem: take the item out, use it up (UseItemEffect: on_state[0], potions heal) and put the hand down.
        /// </summary>
        public void ExtAiUseItem(NpcInstance npc, int itemId)
        {
            var container = npc?.GetUserData();
            if (container?.Props == null)
            {
                Logger.LogWarning($"AI_UseItem: no NPC for item {itemId} - skipped", LogCat.Ai);
                return;
            }

            ExtAiUseItemToState(npc, itemId, 0);
            container.Props.AnimationQueue.Enqueue(new UseItemEffect(new AnimationAction(int0: itemId), container));
            ExtAiUseItemToState(npc, itemId, -1);
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
            return npc.GetUserData()?.Vob.CurrentStateIndex == state;
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

        public bool ExtNpcIsInFightMode(NpcInstance npc, VmGothicEnums.FightMode fightMode)
        {
            return npc.GetUserData().Vob.FightMode == (int)fightMode;
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
            npc.GetUserData().Vob.Attitude = (int)value;
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
                return false;
            }

            // Npc_GetTarget() also fills >other< with the target - scripts use it immediately afterwards.
            _gameStateService.GothicVm.GlobalOther = target;
            return true;
        }

        public void ExtSetTarget(NpcInstance npc, NpcInstance target)
        {
            npc.GetUserData().Props.TargetNpc = target;
        }

        /// <summary>
        /// Like the engine (OpenGothic WorldObjects::sendPassivePerc): the perception goes to every NPC around the
        /// sender - not to the sender itself (a guard's ASSESSWARN calls the other guards, it doesn't warn itself).
        /// </summary>
        public void Npc_SendPassivePerc(NpcInstance npc,VmGothicEnums.PerceptionType perc, NpcInstance victim, NpcInstance other)
        {
            var sender = npc?.GetUserData();
            if (sender == null)
                return;

            var notified = BroadcastPassivePerception(sender, perc, victim, other);
            Logger.Log($"[PassivePerc] {npc.GetName(NpcNameSlot.Slot0)} sends {perc} " +
                       $"(other={other?.GetName(NpcNameSlot.Slot0)}, victim={victim?.GetName(NpcNameSlot.Slot0)}) " +
                       $"to {notified} NPC(s)", LogCat.Ai);
        }

        /// <summary>
        /// Passive perception around the sender to every NPC which registered it: not the sender, not the hero, not
        /// dead/unconscious or culled NPCs. Range: Perc_SetRange, capped by the receiver's senses_range.
        /// </summary>
        public int BroadcastPassivePerception(NpcContainer sender, VmGothicEnums.PerceptionType perception,
            NpcInstance victim, NpcInstance other)
        {
            if (sender?.Go == null)
                return 0;

            // The receivers' functions run inside the sender's external: one that sends again (a mod's B_CallComrades)
            // could bounce between two NPCs until the VM stack overflows. OpenGothic queues them for the next tick.
            if (_passivePerceptionDepth >= _maxPassivePerceptionDepth)
            {
                Logger.LogWarning($"[PassivePerc] {perception} from {sender.Instance?.GetName(NpcNameSlot.Slot0)} " +
                                  $"dropped - sent from inside {_passivePerceptionDepth} other passive perceptions",
                    LogCat.Ai);
                return 0;
            }

            _passivePerceptionDepth++;
            try
            {
                return SendPassivePerception(sender, perception, victim, other);
            }
            finally
            {
                _passivePerceptionDepth--;
            }
        }

        private const int _maxPassivePerceptionDepth = 2;
        private int _passivePerceptionDepth;

        private int SendPassivePerception(NpcContainer sender, VmGothicEnums.PerceptionType perception,
            NpcInstance victim, NpcInstance other)
        {
            var senderPos = sender.Go.transform.position;
            var notified = 0;

            // Copy: a perception function can insert/remove NPCs (Wld_InsertNpc, AI_Teleport).
            foreach (var candidate in _multiTypeCacheService.NpcCache.ToArray())
            {
                if (candidate == sender || candidate.Props == null || candidate.Instance == null) continue;
                if (candidate.PrefabProps != null && candidate.PrefabProps.IsHero()) continue;
                if (NpcHelperService.IsDown(candidate)) continue;
                if (candidate.Go == null || !candidate.Go.activeInHierarchy) continue;
                if (!candidate.Props.Perceptions.TryGetValue(perception, out var perceptionFunction) || perceptionFunction < 0) continue;

                var range = Mathf.Min(_npcHelperService.GetPerceptionRange(perception), candidate.Instance.SensesRange / 100f);
                if ((candidate.Go.transform.position - senderPos).sqrMagnitude > range * range) continue;

                // B_AssessWarn targets a non-human warner as a monster (ZS_AssessMonster -> ZS_Attack): the scripts expect
                // only humans to warn, but orcs run the human AI - the comrades attacked the orc that called them.
                // A friendly non-human warner: hand over the threat as the victim, B_AssessWarn takes its human path.
                var receiverVictim = perception == VmGothicEnums.PerceptionType.AssessWarn &&
                                     IsFriendlyNonHumanWarner(candidate, victim, other)
                    ? other
                    : victim;
                ExecutePassivePerception(perception, perceptionFunction, candidate, receiverVictim, other);
                notified++;
            }

            return notified;
        }

        private bool IsFriendlyNonHumanWarner(NpcContainer receiver, NpcInstance warner, NpcInstance threat)
        {
            return warner != null && threat != null && warner.Guild > (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM &&
                   ExtGetAttitude(receiver.Instance, warner) == VmGothicEnums.Attitude.Friendly;
        }

        /// <summary>
        /// The engine starts a perception function that is a state (ZS_AssessMurder, ZS_AssessDefeat) as the NPC's
        /// new AI state with its _Loop/_End, a B_ function is just called.
        /// </summary>
        private void ExecutePassivePerception(VmGothicEnums.PerceptionType perception, int perceptionFunction,
            NpcContainer receiver, NpcInstance victim, NpcInstance other)
        {
            var vm = _gameStateService.GothicVm;
            var symbolName = vm.GetSymbolByIndex(perceptionFunction)?.Name;
            if (symbolName == null || !symbolName.StartsWith("ZS_", StringComparison.OrdinalIgnoreCase) ||
                vm.GetSymbolByName(symbolName + "_LOOP") == null)
            {
                ExecutePerception(perception, receiver.Props, receiver.Instance, victim, other);
                return;
            }

            var oldSelf = vm.GlobalSelf;
            var oldOther = vm.GlobalOther;
            var oldVictim = vm.GlobalVictim;
            vm.GlobalSelf = receiver.Instance;
            if (other != null)
                vm.GlobalOther = other;
            if (victim != null)
                vm.GlobalVictim = victim;
            try
            {
                ExtAiStartState(receiver.Instance, perceptionFunction, true, "");
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
                vm.GlobalOther = oldOther;
                vm.GlobalVictim = oldVictim;
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
        /// PERC_ASSESSBODY's other: the closest dead NPC the NPC senses (senses_range, sight/hearing like enemies).
        /// </summary>
        public NpcInstance FindClosestSensedBody(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            if (selfNpc?.Go == null)
                return null;

            var selfPosition = selfNpc.Go.transform.position;
            var sensesRangeMeters = self.SensesRange / 100f;
            var closestSqrDist = sensesRangeMeters * sensesRangeMeters;
            NpcContainer closest = null;

            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                if (candidate == selfNpc || candidate.Props == null || candidate.Go == null) continue;
                if (candidate.Props.BodyState != VmGothicEnums.BodyState.BsDead) continue;

                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist > closestSqrDist) continue;
                if (!_npcHelperService.CanSenseNpc(self, candidate.Instance, false)) continue;

                closestSqrDist = sqrDist;
                closest = candidate;
            }

            return closest?.Instance;
        }

        public void UpdateEnemyNpc(NpcInstance self)
        {
            var selfNpc = self.GetUserData();
            var selfPosition = selfNpc.Go.transform.position; // Cache position

            NpcContainer closestEnemy = null;
            var closestSqrDist = float.MaxValue;

            var sensesRangeMeters = self.SensesRange / 100f;
            var sensesRangeSqr = sensesRangeMeters * sensesRangeMeters;

            // FIXME - Performance - Can we clean this up to support only spawned and visible NPCs/Monsters?
            //         A spatial lookup (e.g. the culling system's distance buckets) would avoid the full scan.
            foreach (var candidate in _multiTypeCacheService.NpcCache)
            {
                // Fast-fail checks in order of cheapest first
                if (candidate.Props == null || candidate.Go == null)
                {
                    continue;
                }

                if (candidate.Instance.Index == self.Index)
                {
                    continue;
                }

                // Corpses aren't enemies.
                if (candidate.Props.BodyState == VmGothicEnums.BodyState.BsDead)
                {
                    continue;
                }

                // Range and closest-so-far gates before the expensive attitude and senses checks
                // (senses may include a line-of-sight raycast for see-only monsters).
                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist > sensesRangeSqr || sqrDist >= closestSqrDist)
                {
                    continue;
                }

                if (ExtGetAttitude(self, candidate.Instance) != VmGothicEnums.Attitude.Hostile)
                {
                    continue;
                }

                if (!_npcHelperService.CanSenseNpc(self, candidate.Instance, true))
                {
                    continue;
                }

                closestSqrDist = sqrDist;
                closestEnemy = candidate;
            }

            selfNpc.Props.EnemyNpc = closestEnemy?.Instance;
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
