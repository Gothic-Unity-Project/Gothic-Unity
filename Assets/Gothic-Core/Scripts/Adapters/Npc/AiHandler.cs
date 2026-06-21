using System.Collections.Generic;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Creator;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using FreePoint = Gothic.Core.Models.Vob.WayNet.FreePoint;
using WayPoint = Gothic.Core.Models.Vob.WayNet.WayPoint;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vm;
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
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly VmService _vmService;


        private DaedalusVm Vm => _gameStateService.GothicVm;
        private const int _daedalusLoopContinue = 0; // Id taken from a Daedalus constant.
        private const int _daedalusLoopEnd = 1;

        // True while the combo-preload CallAiFunction is executing. Used by ExtNpcClearAiQueue to
        // avoid stopping the active AttackPlayAni when Npc_ClearAIQueue fires from ZS_Attack_Loop.
        public bool IsInComboPreload { get; private set; }

        private void Start()
        {
            Properties.CurrentAction = new None(new AnimationAction(), NpcData);

            // NPC was dead at save time (or killed by startup scripts before mesh creation, e.g. Nek).
            // Also catches HP=1 dead humans: FightService sets HP=1 on knockout; save-restore sets BsDead
            // before InitNpc runs (via ApplyNpcSavedState in OnNpcCullingChanged), so the BsDead check
            // here fires even when HP wasn't zeroed on kill.
            if (Vob.GetAttribute((int)NpcAttribute.HitPoints) <= 0 || Properties.BodyState == VmGothicEnums.BodyState.BsDead)
            {
                Properties.BodyState = VmGothicEnums.BodyState.BsDead;
                PrefabProps.AnimationSystem.PlayAnimation("S_DEADB");
                Logger.Log($"[AiHandler] {NpcInstance.GetName(NpcNameSlot.Slot0)}: dead on spawn HP={Vob.GetAttribute((int)NpcAttribute.HitPoints)}", LogCat.Npc);
            }
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
            if (Properties.BodyState == VmGothicEnums.BodyState.BsDead)
            {
                Properties.CurrentAction.Tick();

                if (Properties.CurrentAction.IsFinished())
                {
                    if (Properties.AnimationQueue.Count > 0)
                        PlayNextAnimation(Properties.AnimationQueue.Dequeue());
                    else
                        enabled = false;
                }

                return;
            }

            ExecuteActivePerceptions();
            ExecuteStates();

            Properties.CurrentAction.Tick();

            // Add new milliseconds when stateTime shall be measured.
            if (Properties.IsStateTimeActive && Properties.CurrentLoopState == NpcProperties.LoopState.Loop)
            {
                Properties.StateTime += Time.deltaTime;
            }

            // If we're not yet done, we won't handle further tasks (like dequeuing another Action)
            if (!Properties.CurrentAction.IsFinished())
            {
                // Combo preload: Gothic's fight AI runs continuously, so the next attack is always
                // queued before the combo window opens. We replicate this by firing the fight loop
                // as soon as the window opens with an empty queue, so the cut happens at the right
                // frame instead of waiting for the full animation (e.g. s_1hAttack is 5.6s).
                if (_configService.Dev.EnableNpcCombatCombos &&
                    Properties.CurrentAction is AttackPlayAni &&
                    Properties.AnimationQueue.Count == 0 &&
                    Properties.CurrentLoopState == NpcProperties.LoopState.Loop &&
                    Properties.StateLoop != 0 &&
                    PrefabProps.AnimationSystem.HasComboWindowOpened)
                {
                    Vm.GlobalSelf = NpcInstance;
                    Vm.GlobalOther = Properties.StateOther ?? Vm.GlobalHero;
                    IsInComboPreload = true;
                    var preloadResponse = CallAiFunction(Properties.StateLoop);
                    IsInComboPreload = false;
                    if (preloadResponse != _daedalusLoopContinue)
                        Properties.CurrentLoopState = NpcProperties.LoopState.End;
                }
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
                    case NpcProperties.LoopState.None:
                        StartNextRoutine();
                        break;
                    case NpcProperties.LoopState.Start:
                        if (Vob.CurrentStateIndex == 0)
                            return;

                        CallAiFunction(Vob.CurrentStateIndex);
                        Properties.CurrentLoopState = NpcProperties.LoopState.Loop;
                        break;
                    case NpcProperties.LoopState.Loop:
                        if (Properties.StateLoop == 0 && Vob.CurrentStateIndex != 0)
                        {
                            Properties.CurrentLoopState = NpcProperties.LoopState.Start;
                            return;
                        }

                        var loopResponse = CallAiFunction(Properties.StateLoop);
                        
                        // Some ZS_*_Loop return !=0 when they want to quit.
                        if (loopResponse != _daedalusLoopContinue)
                            Properties.CurrentLoopState = NpcProperties.LoopState.End;
                        
                        break;
                    case NpcProperties.LoopState.End:
                        if (Properties.StateEnd != 0)
                            CallAiFunction(Properties.StateEnd);

                        // We filled the AnimationQueue with the ZS_*_End() animations once. END isn't looping.
                        Properties.CurrentLoopState = NpcProperties.LoopState.AfterEnd;
                        break;
                    case NpcProperties.LoopState.AfterEnd:
                        // We're done. Restart normal routine.
                        Properties.CurrentLoopState = NpcProperties.LoopState.Start;

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

            // Skip perception VM calls for NPCs far outside their own sense range.
            // Avoids expensive Daedalus calls for every NPC in the scene (e.g. Old Camp).
            var heroGo = hero?.GetUserData()?.Go;
            if (heroGo != null)
            {
                var dist = Vector3.Distance(gameObject.transform.position, heroGo.transform.position);
                if (dist > NpcInstance.SensesRange / 100f)
                {
                    Properties.CurrentPerceptionTime = 0f;
                    return;
                }
            }
            var assessPlayerRange = _npcHelperService.GetPerceptionRange(VmGothicEnums.PerceptionType.AssessPlayer);

            if(_npcHelperService.CanSenseNpc(NpcInstance, hero, false, assessPlayerRange))
            {
                _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessPlayer, Properties, NpcInstance, null, hero);
            }

            // Scanning all NPCs for the closest enemy is expensive - only do it for NPCs that react to enemies at all.
            if (Properties.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.AssessEnemy, out var enemyPerception) &&
                enemyPerception >= 0)
            {
                _npcAiService.UpdateEnemyNpc(NpcInstance);

                // FIXME - Throws a lot of errors and warnings when NPCs are nearby monsters (e.g. Bridge guard next to OC)
                if(Properties.EnemyNpc != null)
                {
                    _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessEnemy, Properties, NpcInstance,null, Properties.EnemyNpc);
                }
            }


            // PERC_ASSESSFIGHTER: fire when hero has any weapon drawn (not fists).
            // B_AssessFighter checks distance and fight mode internally — we only need to gate on
            // the hero being in a non-fist fight mode so we don't flood the VM every tick.
            if (Properties.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.AssessFighter, out var fighterPerception) &&
                fighterPerception >= 0)
            {
                var heroWeaponState = (VmGothicEnums.WeaponState)(hero?.GetUserData()?.Vob?.FightMode ?? 0);
                if (heroWeaponState != VmGothicEnums.WeaponState.NoWeapon &&
                    heroWeaponState != VmGothicEnums.WeaponState.Fist)
                {
                    _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessFighter, Properties, NpcInstance, null, hero);
                }
            }

            // PERC_MOVENPC: fire when hero is within reach — collision (RootCollisionHandler) is the primary trigger.
            // Cap to 1m so Gothic's PERC_DIST_DIALOG value (5m) doesn't make NPCs react from meters away in VR.
            // B_MoveNpc itself checks BS_STAND so it won't bark at a moving player.
            if (Properties.Perceptions.TryGetValue(VmGothicEnums.PerceptionType.MoveNpc, out var moveNpcPerception) &&
                moveNpcPerception >= 0)
            {
                const float moveNpcMaxRange = 1f;
                var moveNpcRange = Mathf.Min(_npcHelperService.GetPerceptionRange(VmGothicEnums.PerceptionType.MoveNpc), moveNpcMaxRange);
                var distToHero = Vector3.Distance(gameObject.transform.position, heroGo.transform.position);
                if (distToHero <= moveNpcRange)
                    _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.MoveNpc, Properties, NpcInstance, null, hero);
            }

            // TODO: PERC_ASSESSBODY, PERC_ASSESSITEM

            // Reset timer if we executed Perceptions.
            Properties.CurrentPerceptionTime = 0f;
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
                StartRoutine(Vob.NextStateIndex);

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
                    Logger.Log($"[StartNextRoutine] {NpcInstance.GetName(NpcNameSlot.Slot0)}: routine={currentRoutine.Waypoint} prevWP={prevWp} nearestWP={nearestWp?.Name ?? "null"} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1})", LogCat.Ai);
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

        public void StartRoutine(int action)
        {
            var didRoutineChange = Vob.CurrentStateIndex != action;

            Vob.LastAiState = Vob.CurrentStateIndex;
            Vob.CurrentStateIndex = action;
            Vob.CurrentStateValid = true;
            Vob.CurrentStateIsRoutine = false;

            var routineSymbol = Vm.GetSymbolByIndex(action)!;
            Vob.CurrentStateName = routineSymbol.Name;

            // Reset the previous routine's symbols: a new ZS without own _Loop/_End must not call the old ones.
            Properties.StateLoop = 0;
            Properties.StateEnd = 0;

            var symbolLoop = Vm.GetSymbolByName($"{routineSymbol.Name}_Loop");
            if (symbolLoop != null)
            {
                // If we have a _Loop entry, we can safely assume, we are in a routine and not just a monster AiState.
                Vob.CurrentStateIsRoutine = true;
                Properties.StateLoop = symbolLoop.Index;
            }

            var symbolEnd = Vm.GetSymbolByName($"{routineSymbol.Name}_End");
            if (symbolEnd != null)
            {
                Properties.StateEnd = symbolEnd.Index;
            }

            Properties.CurrentLoopState = NpcProperties.LoopState.Start;

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
                Logger.Log($"Start new routine >{routineSymbol.Name}< on >{Go.transform.parent.name}< (FP preserved)", LogCat.Ai);
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
                Properties.CurrentLoopState = NpcProperties.LoopState.End; // Next frame/after current animations are done, the End logic will be executed.
            }
            else
            {
                // Whenever we change routine, we reset some data to "start" from scratch as if the NPC got spawned.
                Vob.CurrentStateValid = false;
                Properties.AnimationQueue.Clear();
                Properties.CurrentAction = new None(new AnimationAction(), NpcData);
                Properties.CurrentLoopState = NpcProperties.LoopState.None; // i.e. call StartNextState() next frame
                Properties.BodyState = VmGothicEnums.BodyState.BsStand;

                PrefabProps.AnimationSystem.StopAllAnimations();
            }
        }

        private void PlayNextAnimation(AbstractAnimationAction action)
        {
            Properties.CurrentAction = action;
            action.Start();
        }

        /// <summary>
        /// Fully reset NPC state.
        /// Called after an NPC is re-enabled in the scene.
        /// </summary>
        public void ReEnableNpc()
        {
            // Spawn to initial spawn location
            var currentRoutine = Properties.RoutineCurrent;
            if (currentRoutine != null)
            {
                var wp = _wayNetService.GetWayNetPoint(currentRoutine.Waypoint);
                if (wp != null)
                {
                    gameObject.transform.position = _npcService.GetFreeAreaAtSpawnPoint(wp.Position);
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
            if (_configService.Dev.GameVersion == GameVersion.Gothic1)
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
                StartRoutine(Vob.CurrentStateIndex);
            }
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
