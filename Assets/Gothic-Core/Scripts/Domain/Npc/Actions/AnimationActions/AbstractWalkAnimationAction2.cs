using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public abstract class AbstractWalkAnimationAction2 : AbstractAnimationAction
    {
        protected Transform NpcTransform => NpcGo.transform;
        protected bool IsDestReached;
        // Only NpcWaterService sets Chest for NPCs - and only for models with swim animations.
        private bool IsSwimming => Vob.AiHuman.WaterLevel == (int)ZenGineConst.WaterLevel.Chest;

        // Name of the animation StartWalk() actually played. StopWalk() must stop exactly this one:
        // recalculating the name would stop the wrong animation when walk/fight mode changed mid-walk
        // (e.g. via an immediately executed AI_SetWalkmode), leaving the walk loop sliding the NPC forever.
        private string _startedWalkAnimationName;
        // Water level the walk animation was picked for (wading/swimming loops differ, see NpcWaterService).
        private int _startedWaterLevel;

        // Ladders on the way (DeveloperConfig.EnableNpcJumpAndFall): like the engine, a waypoint on another height is
        // reached over a ladder nearby - walk to its end, climb, walk on.
        [Inject] private readonly NpcNavMeshService _npcNavMeshService;
        private const float _ladderMinHeightDifference = 1.5f;
        private const float _ladderReachedDistance = 0.6f;
        private const float _ladderCheckInterval = 0.5f;
        private Vector3? _ladderStart;
        private Vector3 _ladderEnd;
        private float _nextLadderCheckTime;
        private bool _isPausedByJumpFall;

        protected AbstractWalkAnimationAction2(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        protected virtual void OnDestinationReached()
        {
            StopWalk();
        }

        /// <summary>
        /// We need to define the final destination spot within overriding class.
        /// </summary>
        protected abstract Vector3 GetWalkDestination();

        public override void Start()
        {
            base.Start();

            // NPCs spawn on top of a WP. We need to inform the implementing class to act (e.g. alter destination WP)
            if (IsDestinationReached())
            {
                OnDestinationReached();

                // Already at the final destination (e.g. a FP_ROAM FreePoint right next to the NPC):
                // never start the walk loop - nobody would stop it again and its root motion
                // would slide the NPC around (visible e.g. on roaming Molerats).
                // IsDestReached covers subclasses which continue at the spot without finishing
                // (e.g. UseMob playing its transition animation) - the walk loop would blend
                // that animation out again. Only a multi-stop route (GoToWp) resets the flag
                // and walks on.
                if (IsFinishedFlag || IsDestReached)
                    return;
            }

            StartWalk();
        }

        public override void Tick()
        {
            base.Tick();

            if (IsFinishedFlag)
            {
                return;
            }

            RefreshWalkAnimationForWater();

            if (HandleJumpFallAndLadders())
                return;

            if (IsDestinationReached())
                OnDestinationReached();
            // Do not rotate when a destination is reached this frame. Either rotate next frame (e.g. GoToWP.nextRoute) or stop it fully.
            else
                HandleRotation();
        }

        protected virtual void StartWalk()
        {
            PhysicsService.EnablePhysicsForNpc(PrefabProps);

            var walkMode = (VmGothicEnums.WalkMode)Vob.AiHuman.WalkMode;
            Props.BodyState = walkMode == VmGothicEnums.WalkMode.Walk
                ? VmGothicEnums.BodyState.BsWalk
                : VmGothicEnums.BodyState.BsRun;
            if (IsSwimming)
                Props.BodyState = VmGothicEnums.BodyState.BsSwim;

            _startedWaterLevel = Vob.AiHuman.WaterLevel;
            _startedWalkAnimationName = AnimationService.GetAnimationName(VmGothicEnums.AnimationType.Move, NpcContainer);
            PrefabProps.AnimationSystem.PlayAnimation(_startedWalkAnimationName);
        }

        protected virtual void StopWalk()
        {
            PhysicsService.EnablePhysicsForNpc(PrefabProps);
            Props.BodyState = IsSwimming ? VmGothicEnums.BodyState.BsSwim : VmGothicEnums.BodyState.BsStand;

            if (_startedWalkAnimationName != null)
            {
                PrefabProps.AnimationSystem.StopAnimation(_startedWalkAnimationName);
            }
        }

        /// <summary>
        /// Walked into (or out of) water mid-walk: wade/swim loop instead of the walk loop and back.
        /// </summary>
        private void RefreshWalkAnimationForWater()
        {
            if (_startedWalkAnimationName == null || IsDestReached || Vob.AiHuman.WaterLevel == _startedWaterLevel)
                return;

            PrefabProps.AnimationSystem.StopAnimation(_startedWalkAnimationName);
            StartWalk();
        }

        /// <summary>
        /// True while falling/climbing (the walk waits) or right when a ladder climb starts.
        /// </summary>
        private bool HandleJumpFallAndLadders()
        {
            if (!ConfigService.Dev.EnableNpcJumpAndFall)
                return false;

            var jumpFall = NpcJumpFall.Get(NpcContainer);
            if (jumpFall == null)
                return false;

            if (jumpFall.IsBusy)
            {
                _isPausedByJumpFall = true;
                return true;
            }
            if (_isPausedByJumpFall)
            {
                _isPausedByJumpFall = false;
                _ladderStart = null;
                StartWalk();
            }

            if (_ladderStart == null && Time.time >= _nextLadderCheckTime)
            {
                _nextLadderCheckTime = Time.time + _ladderCheckInterval;
                var destination = GetWalkDestination();
                var feet = NpcTransform.position - Vector3.up * PrefabProps.AnimationSystem.RestRootHeight;
                if (Mathf.Abs(destination.y - feet.y) > _ladderMinHeightDifference &&
                    _npcNavMeshService.TryFindLadderRoute(feet, destination, out var start, out var end))
                {
                    _ladderStart = start;
                    _ladderEnd = end;
                }
            }

            if (_ladderStart == null)
                return false;

            var toLadder = _ladderStart.Value - NpcTransform.position;
            toLadder.y = 0f;
            if (toLadder.magnitude > _ladderReachedDistance)
                return false; // walk on towards the ladder (HandleRotation steers there)

            var ladderStart = _ladderStart.Value;
            _ladderStart = null;
            if (!jumpFall.TryStartLadder(ladderStart, _ladderEnd))
                return false;

            _isPausedByJumpFall = true;
            return true;
        }

        private bool IsDestinationReached()
        {
            // On the way to a ladder the waypoint above/below isn't reached yet, even if it's right overhead.
            if (_ladderStart != null)
                return false;

            var npcPos = NpcTransform.position;
            var walkPos = GetWalkDestination();
            var npcDistPos = new Vector3(npcPos.x, walkPos.y, npcPos.z);

            var distance = Vector3.Distance(npcDistPos, walkPos);

            // FIXME - Scorpio is above FP, but values don't represent it.
            if (distance < Constants.NpcDestinationReachedThreshold)
            {
                IsDestReached = true;
            }

            return IsDestReached;
        }

        private void HandleRotation()
        {
            var destination = _ladderStart ?? GetWalkDestination();
            var npcPos = NpcTransform.position;
            var sameHeightDirection = new Vector3(destination.x, npcPos.y, destination.z);
            var direction = (sameHeightDirection - npcPos);
            var destinationRotation = Quaternion.LookRotation(direction);
            NpcTransform.rotation = Quaternion.RotateTowards(NpcTransform.rotation, destinationRotation, Time.deltaTime * Constants.NpcRotationSpeed); 
        }
    }
}
