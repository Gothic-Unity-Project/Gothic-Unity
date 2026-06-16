using System.Linq;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Vector3 = UnityEngine.Vector3;

namespace Gothic.Core.Services.Npc
{
    public class NpcAiService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;


        private static int _raycastLayersToUse = (1 << Constants.DefaultLayer);
        
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
            var oldOther = _gameStateService.GothicVm.GlobalOther;
            var oldVictim = _gameStateService.GothicVm.GlobalVictim;

            _gameStateService.GothicVm.GlobalSelf = self;

            if(other != null) 
            {
                _gameStateService.GothicVm.GlobalOther = other;
            }

            if(victim != null)
            {
                _gameStateService.GothicVm.GlobalVictim = victim;
            }

            _gameStateService.GothicVm.Call(perceptionFunction);

            _gameStateService.GothicVm.GlobalSelf = oldSelf;
            _gameStateService.GothicVm.GlobalOther = oldOther;
            _gameStateService.GothicVm.GlobalVictim = oldVictim;
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
            var selfContainer = self.GetUserData();
            var otherContainer = other.GetUserData();

            if (selfContainer == null || otherContainer == null)
            {
                return false;
            }

            // Hint: For forward direction check, we can't use HeadMesh as e.g., Molerat's head is rotated to have red axis (right) as forward.
            //       OpenGothic is therefore using Body rotation, too.
            var selfRoot = selfContainer.Go.transform;
            var otherRoot = otherContainer.Go.transform;
            var selfHead = selfContainer.PrefabProps.Head ?? selfRoot;
            var otherHead = otherContainer.PrefabProps.Head ?? otherRoot;

            var selfGroundPosition = selfRoot.position;
            var otherGroundPosition = otherRoot.position;
            // Unity places positions of objects at the bottom. We need to lift them up towards the head
            var selfRealHeadPosition = new Vector3(selfRoot.position.x, selfHead.position.y, selfRoot.position.z);
            var otherRealHeadPosition = new Vector3(otherRoot.position.x, otherHead.position.y, otherRoot.position.z);

            var distanceToNpc = Vector3.Distance(selfRealHeadPosition, otherRealHeadPosition);
            var inSightRange = distanceToNpc <= self.SensesRange;

            var hasLineOfSightCollisions = Physics.Linecast(selfRealHeadPosition, otherRealHeadPosition, _raycastLayersToUse);

            // DEBUG - collision detection and fetching the object which is blocking LoS.
            // var hasLineOfSightCollisionsDebug = Physics.Linecast(selfRealHeadPosition, otherRealHeadPosition, out var hit, _raycastLayersToUse);

            // Calculate horizontal direction only (ignore Y axis for FOV check), basically a Gobbo is only using x+z for FOV and hero standing in front of it will work correctly. 
            var directionToTarget = new Vector3(
                otherGroundPosition.x - selfGroundPosition.x,
                0f,
                otherGroundPosition.z - selfGroundPosition.z
            ).normalized;
            var selfForwardHorizontal = new Vector3(selfRoot.forward.x, 0f, selfRoot.forward.z).normalized;
            var angleToTarget = Vector3.Angle(selfForwardHorizontal, directionToTarget);
            var inFov = angleToTarget <= fov;

            return inSightRange && !hasLineOfSightCollisions && (freeLOS || inFov);
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

        public void ExtAiStartState(NpcInstance npc, int action, bool stopCurrentState, string wayPointName)
        {
            var other = (NpcInstance)_gameStateService.GothicVm.GlobalOther;
            var victim = (NpcInstance)_gameStateService.GothicVm.GlobalOther;

            var container = npc.GetUserData();

            if (stopCurrentState)
            {
                // Abandon current state immediately so the new one starts next frame, not after the whole queue drains.
                container.PrefabProps?.AiHandler?.ClearState(false);
                container.Props.StateEnd = 0;
                container.Props.CurrentWayPoint = null; // forces GoToWp to use nearest WP, not stale pre-interrupt WP

            }

            container.Props.AnimationQueue.Enqueue(new StartState(
                new AnimationAction(int0: action, bool0: stopCurrentState, string0: wayPointName, instance0: other, instance1: victim),
                container));
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
            // FIXME - Implement remaining tasks from G1 documentation:
            // * Ist der Nsc in einem Animatinsstate, wird die passende Rücktransition abgespielt.
            // * Benutzt der NSC gerade ein MOBSI, poppt er ins stehen.
            var container = npc.GetUserData();
            // Reset immediately (not via queue) so Daedalus C_BodyStateContains checks in the same ZS_*_Loop tick see BsStand.
            container.Props.BodyState = VmGothicEnums.BodyState.BsStand;
            container.Props.AnimationQueue.Enqueue(new StandUp(new AnimationAction(), container));
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
            return npc.Index == _gameStateService.GothicVm.GlobalHero!.Index;
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
            return npc.GetUserData().Props.TargetNpc != null;
        }

        public void ExtSetTarget(NpcInstance npc, NpcInstance target)
        {
            npc.GetUserData().Props.TargetNpc = target;
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
            var selfPosition = selfNpc.Go.transform.position; // Cache position

            NpcContainer closestEnemy = null;
            var closestSqrDist = float.MaxValue;

            // FIXME - Performance - Can we clean this up to support only spawned and visible NPCs/Monsters?
            //         Otherwise we might need to loop through it for each visible monster and 1000x per visible NPC/Monster
            //         -> about 10k checks per frame!
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

                if (!_npcHelperService.CanSenseNpc(self, candidate.Instance, true))
                {
                    continue;
                }

                if (ExtGetAttitude(self, candidate.Instance) != VmGothicEnums.Attitude.Hostile)
                {
                    continue;
                }

                // Compare squared distances to avoid sqrt calculation
                var sqrDist = (candidate.Go.transform.position - selfPosition).sqrMagnitude;
                if (sqrDist < closestSqrDist)
                {
                    closestSqrDist = sqrDist;
                    closestEnemy = candidate;
                }
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
