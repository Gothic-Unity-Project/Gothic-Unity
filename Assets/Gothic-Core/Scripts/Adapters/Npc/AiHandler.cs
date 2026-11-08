using Gothic.Core.Models.Npc;
using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Const;
using Gothic.Core.Creator;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Domain.Vm;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using FreePoint = Gothic.Core.Models.Vob.WayNet.FreePoint;
using WayPoint = Gothic.Core.Models.Vob.WayNet.WayPoint;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.World;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Npc
{
    public class AiHandler : BasePlayerBehaviour
    {
#if UNITY_EDITOR
        public List<(string name, AnimationAction properties)> AiActionHistory = new();
#endif

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly PhysicsService _physicsService;

        // One log per new enemy (PERC_ASSESSENEMY runs every perception tick).
        private NpcInstance _lastLoggedEnemy;

        private DaedalusVm Vm => _gameStateService.GothicVm;
        private readonly HashSet<Type> _failedActionTypes = new();

        // When the state's _Loop may run again (Time.time). See the Loop case in Update.
        private float _nextLoopTime;
        private const int _daedalusLoopContinue = 0; // Id taken from a Daedalus constant.
        private const int _daedalusLoopEnd = 1;

        // True while the combo-preload CallAiFunction is executing. Used by ExtNpcClearAiQueue to
        // avoid stopping the active AttackPlayAni when Npc_ClearAIQueue fires from ZS_Attack_Loop.
        public bool IsInComboPreload { get; private set; }
        private const string _deadStateAnimation = "S_DeadB";
        private const string _unconsciousState = "ZS_UNCONSCIOUS";
        private const string _unconsciousStateAnimation = "S_Wounded";

        private void Start()
        {
            Properties.CurrentAction = new None(new AnimationAction(), NpcData);

            // Killed by startup scripts before its mesh was created (e.g. Nek in G1), or died before a save game was
            // written (or before we left the world): it lies dead from the start.
            var spawnHp = Vob.GetAttribute((int)NpcAttribute.HitPoints);
            if (spawnHp <= 0 || Properties.IsDead)
            {
                Properties.BodyState = VmGothicEnums.BodyState.BsDead;
                StartDead();
                Logger.Log($"[AiHandler] {NpcInstance.GetName(NpcNameSlot.Slot0)}: dead on spawn HP={spawnHp}",
                    LogCat.Npc);

                // ZS_Dead once, without a killer (other = the dead NPC, like the engine's script kill): with the hero
                // as other, B_DeathXP paid him for Nek in every new game. A corpse from a save ran it already (its
                // state is ZS_Dead, the script variables are restored) - again, B_GiveDeathInv doubled its loot.
                if (spawnHp <= 0 && !NpcData.IsZsDeadCalled && !IsInZsDead())
                    _fightService.RunZsDead(NpcInstance, killer: null);
            }
            // Knocked out ones lie there too until ZS_Unconscious ends (a loaded game, the NPC coming back into view).
            else if (IsUnconsciousFromStart())
                StartUnconscious();
        }

        private bool IsInZsDead()
        {
            return string.Equals(Vob.CurrentStateName, DaedalusConst.ZsDead, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsUnconsciousFromStart()
        {
            if (Properties.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return true;

            var state = Vob.NextStateValid ? Vob.NextStateName : Vob.CurrentStateName;
            return string.Equals(state, DaedalusConst.ZsUnconscious, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The engine drops a knocked out NPC itself (FightService) - ZS_Unconscious only waits. Without it, a loaded
        /// one stood there (with no physics, wherever the load put him) until he got up.
        /// </summary>
        private void StartUnconscious()
        {
            Properties.BodyState = VmGothicEnums.BodyState.BsUnconscious;
            Properties.KnockedOutTime = Time.time;
            Properties.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(DaedalusConst.AniWounded), NpcData));
            _physicsService.DisablePhysicsForNpc(NpcData.PrefabProps);
        }

        /// <summary>
        /// Update() only plays out queued animations of dead NPCs. They lie on the ground already (state ani): the
        /// dying ani (T_DeadB) would let them fall down again on every load. It's the fallback for models without one.
        /// </summary>
        private void StartDead()
        {
            var deadAnimation = _animationService.GetTrack(DaedalusConst.AniDeadB, Properties.MdsNameBase,
                Properties.MdsNameOverlay) != null
                ? DaedalusConst.AniDeadB
                : _animationService.GetAnimationName(VmGothicEnums.AnimationType.DeadB, NpcData);
            Properties.AnimationQueue.Enqueue(new PlayAni(new AnimationAction(deadAnimation), NpcData));
            _physicsService.DisablePhysicsForNpc(NpcData.PrefabProps);
        }

        /// <summary>
        /// Basically:
        /// 1. Send Update (Tick) into current Animation to handle
        /// 2. If finished, then check, if we need to handle the new state. _Start() --> _Loop()
        ///
        /// Hint: The isStateTimeActive is only for AI_StartState() from Daedalus which calls sub-routine within routine.
        /// </summary>
        private void Update()
        {
            // If NPC/Monster is dead, only play out the already queued animations (e.g. the dying animation
            // enqueued by FightService), then stop any further process logic.
            if (Properties.IsDead)
            {
                TickCurrentAction();

                if (IsCurrentActionFinished())
                {
                    if (Properties.AnimationQueue.Count > 0)
                        PlayNextAnimation(Properties.AnimationQueue.Dequeue());
                    else
                        enabled = false;
                }

                return;
            }

            _npcAiService.DeliverPassivePerceptions(NpcData);
            ExecuteActivePerceptions();
            ExecuteStates();

            TickCurrentAction();

            // Add new milliseconds when stateTime shall be measured.
            if (Properties.IsStateTimeActive && Properties.CurrentLoopState == LoopState.Loop)
            {
                Properties.StateTime += Time.deltaTime;
            }

            // If we're not yet done, we won't handle further tasks (like dequeuing another Action)
            if (!IsCurrentActionFinished())
            {
                // Combo preload: Gothic's fight AI runs continuously, so the next attack is always
                // queued before the combo window opens. We replicate this by firing the fight loop
                // as soon as the window opens with an empty queue, so the cut happens at the right
                // frame instead of waiting for the full animation (e.g. s_1hAttack is 5.6s).
                if (Properties.CurrentAction is AttackPlayAni &&
                    Properties.AnimationQueue.Count == 0 &&
                    Properties.CurrentLoopState == LoopState.Loop &&
                    Properties.StateLoop != 0 &&
                    PrefabProps.AnimationSystem.HasComboWindowOpened)
                {
                    var oldSelf = Vm.GlobalSelf;
                    var oldOther = Vm.GlobalOther;
                    Vm.GlobalSelf = NpcInstance;
                    Vm.GlobalOther = Properties.StateOther ?? Vm.GlobalHero;
                    IsInComboPreload = true;
                    int preloadResponse;
                    try
                    {
                        preloadResponse = CallAiFunction(Properties.StateLoop);
                    }
                    finally
                    {
                        IsInComboPreload = false;
                        Vm.GlobalSelf = oldSelf;
                        Vm.GlobalOther = oldOther;
                    }
                    if (preloadResponse != _daedalusLoopContinue)
                        Properties.CurrentLoopState = LoopState.End;
                }
                // The NPC really does something - when it's done, the state's loop may react at once.
                _nextLoopTime = 0f;
                return;
            }

            // Queue is empty. Check if we want to start Looping
            if (Properties.AnimationQueue.Count == 0)
            {
                // We always need to set "self" before executing any Daedalus function.
                // "other" defaults to hero here so routine states (ZS_*_Loop) have a sensible fallback.
                // Perception calls (ExecutePerception) override GlobalOther themselves with their own save/restore.
                if (NpcInstance != null)
                {
                    Vm.GlobalSelf = NpcInstance;
                    Vm.GlobalOther = Properties.StateOther ?? Vm.GlobalHero;
                    if (Properties.StateVictim != null)
                        Vm.GlobalVictim = Properties.StateVictim;
                }

                switch (Properties.CurrentLoopState)
                {
                    // None means, the NPC is newly created and didn't execute any Routine as of now OR a State was changed via Daedalus scripts.
                    case LoopState.None:
                        StartNextRoutine();
                        break;
                    case LoopState.Start:
                        if (Vob.CurrentStateIndex == 0)
                            return;

                        CallAiFunction(Vob.CurrentStateIndex);
                        Properties.CurrentLoopState = LoopState.Loop;
                        _nextLoopTime = 0f;
                        break;
                    case LoopState.Loop:
                        if (Properties.StateLoop == 0 && Vob.CurrentStateIndex != 0)
                        {
                            // Like the engine, a state started via AI_StartState without _Loop (ZS_HealSelf ->
                            // ZS_HealSelfMana after a fight) ends at once and the NPC goes back to its routine (TA
                            // state walks to self.wp). Restarting it kept the NPC standing where it gave up a chase.
                            // The daily routine state / a monster's start state still restart.
                            if (!IsDailyState(Vob.CurrentStateIndex))
                            {
                                Properties.CurrentLoopState = LoopState.End;
                                return;
                            }

                            Properties.CurrentLoopState = LoopState.Start;
                            return;
                        }

                        // A pass that only did instant things (AI_AlignToFP while aligned, pure checks) doesn't run
                        // again every frame: like the engine (OpenGothic Npc::tickRoutine) it waits for the NPC's
                        // perception time. After a real action it runs at once (fights, walking routines).
                        if (Time.time < _nextLoopTime)
                            break;
                        _nextLoopTime = Time.time + Properties.PerceptionTime;

                        var loopResponse = CallAiFunction(Properties.StateLoop);
                        
                        // Some ZS_*_Loop return !=0 when they want to quit.
                        if (loopResponse != _daedalusLoopContinue)
                        {
                            Properties.CurrentLoopState = LoopState.End;
                            LogStateLoopEnded();
                            ForgetHeroAfterLookingAway();
                        }
                        
                        break;
                    case LoopState.End:
                        if (Properties.StateEnd != 0)
                        {
                            Properties.IsInStateEnd = true;
                            try
                            {
                                CallAiFunction(Properties.StateEnd);
                            }
                            finally
                            {
                                Properties.IsInStateEnd = false;
                            }
                        }

                        // We filled the AnimationQueue with the ZS_*_End() animations once. END isn't looping.
                        Properties.CurrentLoopState = LoopState.AfterEnd;
                        break;
                    case LoopState.AfterEnd:
                        // We're done. Restart normal routine.
                        Properties.CurrentLoopState = LoopState.Start;

                        // If we're inside another ZS_*_ loop via Ai_StartState(), we will exit it now. If not, we will simply restart current ZS_* routine.
                        StartNextRoutine();

                        break;
                }
            }
            // Go on
            else
            {
                // Editor-only: this fires for every dequeued action of every NPC - the string interpolation
                // plus file sink would be measurable noise on device.
                Logger.LogEditor($"Start playing >{Properties.AnimationQueue.Peek().GetType()}< on >{Go.transform.parent.name}<", LogCat.Ai);
                PlayNextAnimation(Properties.AnimationQueue.Dequeue());
            }
        }

        /// <summary>
        /// Diagnostics: a non-daily state whose loop ended (berzerk ended at once) - which state, after how long.
        /// </summary>
        private void LogStateLoopEnded()
        {
            if (IsDailyState(Vob.CurrentStateIndex))
                return;
            Logger.Log($"[AiLoop] {NpcInstance.GetName(ZenKit.Daedalus.NpcNameSlot.Slot0)}: {Vob.CurrentStateName} loop ended " +
                       $"(state time {Properties.StateTime:F1} s, active {Properties.IsStateTimeActive})", LogCat.Ai);
        }

        private bool IsDailyState(int stateIndex)
        {
            return stateIndex == Properties.RoutineCurrent?.Action || stateIndex == NpcInstance.StartAiState;
        }

        /// <summary>
        /// A state's _End function, called when AI_StartState(..., 1, ...) is
        /// reached in the queue - with the old state's other/victim, the globals are restored afterwards.
        /// </summary>
        public void CallStateEnd(int endFunction, NpcInstance other, NpcInstance victim)
        {
            // The daily routine's other is the hero (as in Update) - not whatever the last NPC left.
            using (new VmGlobalsScope(Vm, NpcInstance, other ?? Vm.GlobalHero, victim))
            {
                try
                {
                    Logger.Log($"[AiHandler] {NpcInstance.GetName(NpcNameSlot.Slot0)}: " +
                               $"{Vm.GetSymbolByIndex(endFunction)?.Name} (state ended by AI_StartState)", LogCat.Ai);
                    CallAiFunction(endFunction);
                }
                catch (System.Exception e)
                {
                    Logger.LogError($"[AiHandler] State end {endFunction} failed: {e.Message}", LogCat.Ai);
                }
            }
        }

        private int CallAiFunction(int symbolIndex)
        {
            var returnContinue = _daedalusLoopContinue;
            
            var loopSymbol = Vm.GetSymbolByIndex(symbolIndex)!;
            switch (loopSymbol.ReturnType)
            {
                case DaedalusDataType.Int:
                    returnContinue = Vm.Call<int>(symbolIndex);
                    break;
                default:
                    Vm.Call(symbolIndex);
                    break;
            }
            
#if UNITY_EDITOR
            // Limit size to 50 elements
            if (AiActionHistory.Count >= 50)
                AiActionHistory.RemoveRange(0, AiActionHistory.Count - 50);

            foreach (var action in Properties.AnimationQueue)
            {
                AiActionHistory.Add((action.GetType().Name, action.Action));
            }
#endif
            
            return returnContinue;
        }

        /// <summary>
        /// Execute perceptions if it's about time.
        /// </summary>
        private void ExecuteActivePerceptions()
        {
            Properties.CurrentPerceptionTime += Time.deltaTime;
            if (Properties.CurrentPerceptionTime < Properties.PerceptionTime)
            {
                return;
            }

            var hero = (NpcInstance)_gameStateService.GothicVm.GlobalHero;

            // Skip the hero's perception VM calls for NPCs far outside their own sense range.
            // Avoids expensive Daedalus calls for every NPC in the scene (e.g. Old Camp).
            var heroGo = hero?.GetUserData()?.Go;
            var isHeroInSensesRange = heroGo != null && Vector3.Distance(gameObject.transform.position,
                heroGo.transform.position) <= NpcInstance.SensesRange / 100f;

            // Like the engine, an NPC spots enemies and bodies on
            // its own (Milten vs. an orc, a guard vs. a wolf) - the hero doesn't need to stand next to it.
            // Use the NPC's own senses_range (not the fixed global PERC_DIST_ASSESS_PLAYER).
            // ZS_GuidePC sets self.senses_range = HAI_DIST_ASSESS_MONSTER (~35m) so escort NPCs
            // can detect a lagging player far enough away to stop and wait.
            if(isHeroInSensesRange && (IsEscortSensingHero() || _npcHelperService.CanSenseNpc(NpcInstance, hero, false)))
            {
                _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessPlayer, Properties, NpcInstance, null, hero);

                // Active perception: hero is sensed while holding a weapon (FightMode set by
                // NpcAiService.ExtSetHeroWeaponState).
                // B_AssessFighter itself checks the distance (HAI_DIST_MELEE/RANGED) and attitude.
                var heroWeaponState = (VmGothicEnums.WeaponState)hero.GetUserData().Vob.FightMode;
                if (_npcAiService.IsArmedWeaponState(heroWeaponState))
                    _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessFighter, Properties,
                        NpcInstance, null, hero);

                // PERC_OBSERVESUSPECT (original engine only, OpenGothic lacks it): the hero sneaks within its range
                // (Perc_SetRange, G1: PERC_DIST_DIALOG) - G1 B_ObserveSuspect: "What's this supposed to be?".
                // ZS_ObserveSuspect doesn't enable it again, so it won't restart while the hero keeps sneaking.
                if (hero.GetUserData().Props.BodyState == VmGothicEnums.BodyState.BsSneak &&
                    Vector3.Distance(gameObject.transform.position, heroGo.transform.position) <=
                    _npcHelperService.GetPerceptionRange(VmGothicEnums.PerceptionType.ObserveSuspect))
                    _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.ObserveSuspect, Properties,
                        NpcInstance, null, hero);
            }

            // Scanning all NPCs for the closest enemy is expensive - only do it for NPCs that react to enemies at all.
            if (Properties.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.AssessEnemy, out var enemyPerception) &&
                enemyPerception >= 0)
            {
                _npcAiService.UpdateEnemyNpc(NpcInstance);

                // FIXME - Throws a lot of errors and warnings when NPCs are nearby monsters (e.g. Bridge guard next to OC)
                if(Properties.EnemyNpc != null && !IsLookingAwayFrom(Properties.EnemyNpc))
                {
                    if (_lastLoggedEnemy != Properties.EnemyNpc)
                    {
                        _lastLoggedEnemy = Properties.EnemyNpc;
                        var enemyPos = Properties.EnemyNpc.GetUserData()?.Go?.transform.position ?? transform.position;
                        Logger.Log($"[Perc] AssessEnemy {NpcInstance.GetName(ZenKit.Daedalus.NpcNameSlot.Slot0)} -> " +
                                   $"{Properties.EnemyNpc.GetName(ZenKit.Daedalus.NpcNameSlot.Slot0)} " +
                                   $"({Vector3.Distance(transform.position, enemyPos):F1} m)", LogCat.Ai);
                    }
                    _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessEnemy, Properties, NpcInstance,null, Properties.EnemyNpc);
                }
            }

            // PERC_MOVENPC: fire when hero is within reach — collision (RootCollisionHandler) is the primary trigger.
            // Cap to 1m so Gothic's PERC_DIST_DIALOG value (5m) doesn't make NPCs react from meters away in VR.
            // B_MoveNpc itself checks BS_STAND so it won't bark at a moving player.
            if (isHeroInSensesRange &&
                Properties.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.MoveNpc, out var moveNpcPerception) &&
                moveNpcPerception >= 0)
            {
                var moveNpcRange = Mathf.Min(_npcHelperService.GetPerceptionRange(VmGothicEnums.PerceptionType.MoveNpc),
                    AiConst.MoveNpcMaxRangeMeters);
                var distToHero = Vector3.Distance(gameObject.transform.position, heroGo.transform.position);
                if (distToHero <= moveNpcRange)
                    _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.MoveNpc, Properties, NpcInstance, null, hero);
            }

            // PERC_ASSESSBODY (active, like the engine): the closest sensed body. The scripts remember what they
            // did with it (searched, reported the murder), so it firing every tick is fine.
            if (Properties.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.AssessBody, out var bodyPerception) &&
                bodyPerception >= 0)
            {
                var body = _npcAiService.FindClosestSensedBody(NpcInstance);
                if (body != null)
                    _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessBody, Properties, NpcInstance, null, body);
            }

            // TODO: PERC_ASSESSITEM

            // Reset timer if we executed Perceptions.
            Properties.CurrentPerceptionTime = 0f;
        }

        /// <summary>
        /// A guide/follower (ZS_GuidePC/ZS_FollowPC in G1, ZS_Guide_Player/
        /// ZS_Follow_Player in G2) senses the hero all around its senses range - the states set SENSE_SMELL with
        /// HAI_DIST_ASSESS_MONSTER so PERC_ASSESSPLAYER notices a lagging hero. The quiet senses (smell only 3 m)
        /// let the guide run ahead with its back to the hero: Baal Parvez, Lares and Jack Alligator never waited.
        /// </summary>
        private bool IsEscortSensingHero()
        {
            return NpcHelperService.IsInEscortState(NpcInstance);
        }

        private void ExecuteStates()
        {
            if (Properties.RefuseTalkTimer > 0f)
                Properties.RefuseTalkTimer -= Time.deltaTime;
        }

        /// <summary>
        /// Restart means:
        /// 1. Either restart currently looping one or
        /// 2. Start the new one after Ai_ExchangeRoutine() got called and ZS_*END of previous one is done
        /// </summary>
        public void StartNextRoutine()
        {
            Properties.StateTime = 0.0f;
            Properties.ItemAnimationState = -1;

            // We have set some "next" state. Use it instead of going back to daily routine first.
            if (Vob.NextStateValid)
            {
                StartRoutine(Vob.NextStateIndex, Vob.NextStateIsRoutine);

                // As we use NextStateIndex as new "current" one, we clear it now safely.
                Vob.NextStateIndex = -1;
                Vob.NextStateValid = false;
                Vob.NextStateIsRoutine = false;
                Vob.NextStateName = string.Empty;
            }
            // If we have nothing prepared, start daily Routine.
            else
            {
                // Returning to routine — clear combat context so routine states get hero as "other".
                Properties.StateOther = null;
                Properties.StateVictim = null;

                var currentRoutine = Properties.RoutineCurrent;
                if (currentRoutine != null)
                {
                    // Update CurrentWayPoint to the nearest WP so GoToWP uses the correct Dijkstra
                    // starting position instead of the stale spawn WP. Without this, GoToWP(destination)
                    // computes a path from the old spawn WP, walking the NPC backward before going forward.
                    var prevWp = Properties.CurrentWayPoint?.Name ?? "null";
                    var nearestWp = _wayNetService.FindNearestWayPoint(gameObject.transform.position);
                    if (nearestWp != null)
                        Properties.CurrentWayPoint = nearestWp;
                    var pos = gameObject.transform.position;
                    Logger.Log($"[StartNextRoutine] {NpcInstance.GetName(NpcNameSlot.Slot0)}: " +
                               $"routine={currentRoutine.Waypoint} prevWP={prevWp} " +
                               $"nearestWP={nearestWp?.Name ?? "null"} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1})",
                        LogCat.Ai);
                    StartRoutine(currentRoutine.Action, currentRoutine.Waypoint);
                }
                else
                    // If we don't have a routine, we're a monster.
                    StartRoutine(NpcInstance.StartAiState);
            }
        }

        public void StartRoutine(int action, string wayPointName)
        {
            // We need to set WayPoint within Daedalus instance as it calls _self.wp_ during routine loops.
            // If e.g. AssessSc()+B_CheckForImportantInfo() changes state to ZS_TALK(), we have no WP set. Therefore keep original one.
            if (wayPointName.NotNullOrEmpty())
            {
                NpcInstance.Wp = wayPointName; // For execution of self.wp during Routine calls.
                Vob.ScriptWaypoint = wayPointName; // for SaveGame use.
            }
            
            StartRoutine(action);
        }

        /// <summary>
        /// ZS_TurnAway ("I didn't see anything") turns the back to the
        /// hero and ignores him for HAI_TIME_TURNAWAY (20 s) - but the enemy perception senses without a view angle
        /// (like OpenGothic) and B_AssessEnemy checks Npc_CanSeeNpcFreeLOS (no angle either): a beaten guard with a
        /// hostile attitude drew his weapon, put it away and looked away again in a loop, the 20 s never ran out.
        /// The hero isn't an enemy to him in these states; other enemies (monsters) still are.
        /// </summary>
        private bool IsLookingAwayFrom(NpcInstance enemy)
        {
            if (!IsInLookingAwayState())
                return false;

            return enemy.Index == ((NpcInstance)Vm.GlobalHero)?.Index;
        }

        /// <summary>
        /// When ZS_TurnAway runs out, the hostile temporary attitude that sent him there goes back to the permanent
        /// one, like B_ResetTempAttitude - nothing in the scripts resets it after looking away, and he started all over
        /// again whenever he saw the hero.
        /// </summary>
        private void ForgetHeroAfterLookingAway()
        {
            if (!IsInLookingAwayState() ||
                Vob.AttitudeTemp == Vob.Attitude)
                return;

            Logger.Log($"[AiLoop] {NpcInstance.GetName(NpcNameSlot.Slot0)}: done looking away - temporary attitude " +
                       $"{(VmGothicEnums.Attitude)Vob.AttitudeTemp} back to {(VmGothicEnums.Attitude)Vob.Attitude}",
                LogCat.Ai);
            Vob.AttitudeTemp = Vob.Attitude;
        }

        private bool IsInLookingAwayState()
        {
            var state = Vob.CurrentStateName;
            foreach (var lookingAwayState in DaedalusConst.LookingAwayStates)
            {
                if (string.Equals(state, lookingAwayState, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <param name="isRoutineState">Like Gothic's isRtnState (saved): true if the daily routine (or a monster's
        /// StartAiState) runs the state, false for states started by AI_StartState. A loaded NPC continues the latter
        /// (SaveGameNpcDomain).</param>
        public void StartRoutine(int action, bool isRoutineState = true)
        {
            var didRoutineChange = Vob.CurrentStateIndex != action;

            Vob.LastAiState = Vob.CurrentStateIndex;
            Vob.CurrentStateIndex = action;
            Vob.CurrentStateValid = true;
            Vob.CurrentStateIsRoutine = isRoutineState;

            var routineSymbol = Vm.GetSymbolByIndex(action)!;
            Vob.CurrentStateName = routineSymbol.Name;

            // Reset the previous routine's symbols: a new ZS without own _Loop/_End must not call the old ones.
            Properties.StateLoop = 0;
            Properties.StateEnd = 0;

            var symbolLoop = Vm.GetSymbolByName($"{routineSymbol.Name}_Loop");
            if (symbolLoop != null)
                Properties.StateLoop = symbolLoop.Index;

            var symbolEnd = Vm.GetSymbolByName($"{routineSymbol.Name}_End");
            if (symbolEnd != null)
            {
                Properties.StateEnd = symbolEnd.Index;
            }

            Properties.CurrentLoopState = LoopState.Start;

            // Engine (OpenGothic Npc::startState): every state change disables all perceptions, the new ZS_ init
            // re-enables its own. Only StartState.cs did it so far - AI_ContinueRoutine / back-to-routine kept e.g.
            // ZS_AssessFighter's PERC_ASSESSREMOVEWEAPON, so the NPC answered every later weapon/rune removal with
            // $WISEMOVE.
            if (didRoutineChange)
                Properties.Perceptions.Clear();

            // We need to properly start state time as e.g. ZS_Cook won't call AI_StartState() or Npc_SetStateTime()
            // But it's required as it checks immediately how long the Cauldron is already been whirled.
            Properties.IsStateTimeActive = true;

            // When we reached end of ZS_*_END, we also call this method. Check if we really altered the routine action or just restarted it.
            // Skip FP release on the very first routine start (LastAiState==0 means no prior state).
            // This preserves CurrentFreePoint restored from dirty save so Npc_IsOnFP returns true
            // on the first B_GotoFP call, preventing the NPC from walking to a wrong distant FP.
            if (didRoutineChange && Vob.LastAiState != 0)
            {
                if (Properties.CurrentFreePoint != null)
                {
                    Properties.CurrentFreePoint.IsLocked = false;
                    Properties.CurrentFreePoint = null;
                }
                Logger.Log($"Start new routine >{routineSymbol.Name}< on >{Go.transform.parent.name}<", LogCat.Ai);
                Properties.StateTime = 0;
            }
            else if (didRoutineChange)
            {
                Logger.Log($"Start new routine >{routineSymbol.Name}< on >{Go.transform.parent.name}< (FP preserved)",
                    LogCat.Ai);
                Properties.StateTime = 0;
            }
        }

        /// <summary>
        /// Clear ZS functions. If callEndFunction=true, then ZS_*_End() animations will play before moving to new animation.
        /// </summary>
        public void ClearState(bool callEndFunction)
        {
            if (callEndFunction)
            {
                // Next frame/after current animations are done, the End logic will be executed.
                Properties.CurrentLoopState = LoopState.End;
            }
            else
            {
                // Whenever we change routine, we reset some data to "start" from scratch as if the NPC got spawned.
                Vob.CurrentStateValid = false;
                Properties.AnimationQueue.Clear();
                Properties.CurrentLoopState = LoopState.None; // i.e. call StartNextState() next frame

                // AI_StartState keeps a seated NPC seated (B_AssessTalk: AI_StartState(ZS_Talk, 0)). A running
                // sit-down transition finishes first, like in the engine.
                if (NpcData.IsUsingMob() || Properties.CurrentAction is UseMob)
                {
                    if (Properties.CurrentAction is not UseMob)
                        Properties.CurrentAction = new None(new AnimationAction(), NpcData);
                    return;
                }

                Properties.CurrentAction = new None(new AnimationAction(), NpcData);

                // A state change doesn't stand the dead up - it set BS_STAND
                // and stopped S_DEADB: the dead hero took hits again (knocked out, back on his feet with 1 HP).
                if (Properties.BodyState == VmGothicEnums.BodyState.BsDead)
                {
                    Logger.LogWarning($"[AiHandler] {NpcInstance.GetName(NpcNameSlot.Slot0)}: state cleared while " +
                                      "dead - stays dead", LogCat.Ai);
                    return;
                }

                Properties.BodyState = VmGothicEnums.BodyState.BsStand;

                PrefabProps.AnimationSystem.StopAllAnimations();
            }
        }

        private void PlayNextAnimation(AbstractAnimationAction action)
        {
            Properties.CurrentAction = action;
            try
            {
                action.Start();
            }
            catch (Exception e)
            {
                SkipFailedAction(e);
            }
        }

        private void TickCurrentAction()
        {
            try
            {
                Properties.CurrentAction.Tick();
            }
            catch (Exception e)
            {
                SkipFailedAction(e);
            }
        }

        private bool IsCurrentActionFinished()
        {
            try
            {
                return Properties.CurrentAction.IsFinished();
            }
            catch (Exception e)
            {
                SkipFailedAction(e);
                return true;
            }
        }

        /// <summary>
        /// An action that threw can't finish on its own - it threw again every frame and the NPC hung in it (an orc
        /// frozen in his dialog line). It counts as done, the NPC goes on with the next one. Logged once per action
        /// type and NPC.
        /// </summary>
        private void SkipFailedAction(Exception e)
        {
            var actionType = Properties.CurrentAction.GetType();
            if (_failedActionTypes.Add(actionType))
                Logger.LogError($"{actionType.Name} of {Go.transform.parent?.name} threw - skipped. Later " +
                                $"failures of this action on this NPC are not logged. {e}", LogCat.Ai);

            Properties.CurrentAction = new None(new AnimationAction(), NpcData);
        }

        /// <summary>
        /// Lying dead. G1 crawler queen, harpy and meatbug have no S_DEADB, only S_DEAD.
        /// </summary>
        private void PlayDeadPose()
        {
            if (!PrefabProps.AnimationSystem.PlayAnimation(DaedalusConst.AniDeadB))
                PrefabProps.AnimationSystem.PlayAnimation(DaedalusConst.AniDead);
        }

        /// <summary>
        /// Fully reset NPC state.
        /// Called after an NPC is re-enabled in the scene.
        /// </summary>
        public void ReEnableNpc()
        {
            // A corpse stays where it died and lies again - it was moved to its
            // routine waypoint and started its routine: Lukis and Ghorri stood in the bind pose, legs in the ground.
            if (Properties.BodyState == VmGothicEnums.BodyState.BsDead)
            {
                Properties.AnimationQueue.Clear();
                Properties.CurrentAction = new None(new AnimationAction(), NpcData);
                PlayDeadPose();
                Logger.Log($"[AiHandler] {NpcInstance.GetName(NpcNameSlot.Slot0)}: re-enabled dead - lies again",
                    LogCat.Npc);
                return;
            }

            // Spawn to initial spawn location
            var currentRoutine = Properties.RoutineCurrent;
            if (currentRoutine != null)
            {
                var wp = _wayNetService.GetWayNetPoint(currentRoutine.Waypoint);
                if (wp != null)
                {
                    gameObject.transform.position = _npcService.GetFreeAreaAtSpawnPoint(wp.Position, gameObject);
                    // Only overwrite CurrentFreePoint when the routine WP is itself a FreePoint.
                    // If it's a WayPoint (e.g. SPAWN_ZOLLO), preserve the home FP so GoToNextFp
                    // can reclaim it instead of grabbing the nearest unlocked one.
                    if (wp is FreePoint reFp)
                        Properties.CurrentFreePoint = reFp;
                    Properties.CurrentWayPoint = wp as WayPoint;
                }
                else
                    Logger.LogWarning($"ReEnableNpc: waypoint '{currentRoutine.Waypoint}' not found for {gameObject.name} — NPC will re-enable at current position.", LogCat.Npc);
            }

            // Animation state handling
            Properties.AnimationQueue.Clear();
            Properties.CurrentAction = new None(new AnimationAction(), NpcData);
            Properties.StateTime = 0.0f;

            // Mob usage (sitting on a bench, ...) ends with the respawn at the routine waypoint - the routine walks
            // there and sits down again. Before, the NPC kept its sitting state and sat in the air at the respawn
            // point.
            if (Properties.CurrentInteractableStateId >= 0)
            {
                if (PrefabProps.CurrentInteractable != null)
                    PrefabProps.CurrentInteractable.MobState = -1;

                PrefabProps.CurrentInteractable = null;
                PrefabProps.CurrentInteractableSlot = null;
                Properties.CurrentInteractableStateId = -1;
                Properties.BodyState = VmGothicEnums.BodyState.BsStand;
            }

            // WayNet handling
            // Nothing to do -> Even a despawned NPC (based on culling) needs to stick with its WPs/FPs.
            // Whenever re-enabled they are still attached / sit / stand at their points. Otherwise, another NPC
            // Will steal it if un-culled earlier.


            // CurrentItem handling
            Properties.ItemAnimationState = -1;
            if (Properties.CurrentItem != -1)
            {
                // If NPC had an item in its hands, we need to remove the mesh.
                var leftHand = gameObject.FindChildRecursively("ZS_LEFTHAND");
                var rightHand = gameObject.FindChildRecursively("ZS_RIGHTHAND");

                if (leftHand != null)
                {
                    for (var i = 0; i < leftHand.transform.childCount; i++)
                    {
                        Destroy(leftHand.transform.GetChild(i).gameObject);
                    }
                }

                if (rightHand != null)
                {
                    for (var i = 0; i < rightHand.transform.childCount; i++)
                    {
                        Destroy(rightHand.transform.GetChild(i).gameObject);
                    }
                }
            }
            Properties.CurrentItem = -1;

            // Reset "currently" used item

            // FIXME - We need to properly set this value for Gothic2 as well.
            if (_contextGameVersionService.IsGothic1())
            {
                NpcInstance.SetAiVar(_vmService.AIVItemStatusKey, _vmService.TAITNone);
            }

            // Start over
            if (currentRoutine != null)
            {
                StartRoutine(currentRoutine.Action, currentRoutine.Waypoint);
            }
            else
            {
                //if we don't have a routine means it's about a monster
                StartRoutine(Vob.CurrentStateIndex, Vob.CurrentStateIsRoutine);
            }
        }

        /// <summary>
        /// After the hero slept the NPC starts over like after culling
        /// (ReEnableNpc: routine waypoint, empty queue, routine) - and forgets its fight: no enemy/target, weapon away.
        /// Monsters go back to their start state, not the one they were in (an attack).
        /// </summary>
        public void ResetAfterTimeSkip()
        {
            Properties.EnemyNpc = null;
            Properties.TargetNpc = null;
            Properties.StateOther = null;
            Properties.StateVictim = null;
            Properties.LookAtTarget = null;
            Properties.LastPursuitWarnTime = float.MinValue;

            var isWeaponDrawn = (VmGothicEnums.WeaponState)Vob.FightMode != VmGothicEnums.WeaponState.NoWeapon;
            ReEnableNpc();

            if (Properties.RoutineCurrent == null && NpcInstance.StartAiState != 0)
                StartRoutine(NpcInstance.StartAiState);
            if (isWeaponDrawn)
                _npcAiService.ExtAiUndrawWeapon(NpcInstance);

            var target = Properties.RoutineCurrent != null
                ? "routine " + Properties.RoutineCurrent.Waypoint
                : "start state";
            Logger.Log($"[TimeSkipReset] {NpcInstance.GetName(NpcNameSlot.Slot0)} reset ({target}" +
                       $"{(isWeaponDrawn ? ", weapon away" : "")})", LogCat.Ai);
        }

        public void DisableNpc()
        {
            // Stop all animations and reset to T-Pose
            PrefabProps.AnimationSystem.DisableObject();

            // We need to free the FP. When the NPC is re-enabled, it can walk to it again.
            if (Properties.CurrentFreePoint != null)
            {
                Properties.CurrentFreePoint.IsLocked = false;
            }
        }

        public void HeroCollisionDetected()
        {
            _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.MoveNpc, Properties, NpcInstance, null, (NpcInstance)_gameStateService.GothicVm.GlobalHero);
        }
    }
}
