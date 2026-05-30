using System.Collections.Generic;
using GUZ.Core.Adapters.Properties;
using GUZ.Core.Creator;
using GUZ.Core.Domain.Npc.Actions;
using GUZ.Core.Domain.Npc.Actions.AnimationActions;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Npc;
using GUZ.Core.Services.Vm;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.Npc
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

        private void Start()
        {
            Properties.CurrentAction = new None(new AnimationAction(), NpcData);
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
            // If NPC/Monster is dead, stop any further process logic.
            if (Properties.BodyState == VmGothicEnums.BodyState.BsDead)
            {
                enabled = false;
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
                return;
            }

            // Queue is empty. Check if we want to start Looping
            if (Properties.AnimationQueue.Count == 0)
            {
                // We always need to set "self" before executing any Daedalus function.
                if (NpcInstance != null)
                {
                    Vm.GlobalSelf = NpcInstance;
                }

                DaedalusSymbol loopSymbol;
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
                Logger.Log($"Start playing >{Properties.AnimationQueue.Peek().GetType()}< on >{Go.transform.parent.name}<", LogCat.Ai);
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
            
            _npcAiService.UpdateEnemyNpc(NpcInstance);

            // FIXME - CanSense is not separating between smell, hear, and see as of now. Please add functionality.
            if(_npcHelperService.CanSenseNpc(NpcInstance, (NpcInstance)_gameStateService.GothicVm.GlobalHero, false))
            {
                _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessPlayer, Properties, NpcInstance,null, (NpcInstance)_gameStateService.GothicVm.GlobalHero);
            }

            // FIXME - Throws a lot of errors and warnings when NPCs are nearby monsters (e.g. Bridge guard next to OC)
            if(Properties.EnemyNpc != null)
            {
                _npcAiService.ExecutePerception(VmGothicEnums.PerceptionType.AssessEnemy, Properties, NpcInstance,null, Properties.EnemyNpc);
            }


            // FIXME - We need to add other active perceptions here:
            //         PERC_ASSESSBODY, PERC_ASSESSITEM, PERC_ASSESSFIGHTER
            //         But at best when we test it immediately

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
                var currentRoutine = Properties.RoutineCurrent;
                if (currentRoutine != null)
                    StartRoutine(currentRoutine.Action, currentRoutine.Waypoint);
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
            // End original loop first
            // TODO - Calling ClearState(false) was buggy when e.g. Diego dialog "END" was clicked. Then the dialog lines were skipped.
            // if (Properties.CurrentLoopState == NpcProperties.LoopState.Loop)
            // {
            //     // We reuse this function as it is doing what we need.
            //     ClearState(false);
            // }

            var didRoutineChange = Vob.CurrentStateIndex != action;

            Vob.LastAiState = Vob.CurrentStateIndex;
            Vob.CurrentStateIndex = action;
            Vob.CurrentStateValid = true;
            Vob.CurrentStateIsRoutine = false;

            var routineSymbol = Vm.GetSymbolByIndex(action)!;
            Vob.CurrentStateName = routineSymbol.Name;

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
            if (didRoutineChange)
            {
                Logger.Log($"Start new routine >{routineSymbol.Name}< on >{Go.transform.parent.name}<", LogCat.Ai);
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
                var wpPos = _wayNetService.GetWayNetPoint(currentRoutine.Waypoint).Position;
                gameObject.transform.position = _npcService.GetFreeAreaAtSpawnPoint(wpPos);
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
