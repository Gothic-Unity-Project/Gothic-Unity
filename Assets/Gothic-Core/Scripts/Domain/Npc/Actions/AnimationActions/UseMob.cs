using System.Linq;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Extensions;
using JetBrains.Annotations;
using MyBox;
using UnityEngine;
using ZenKit.Vobs;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public class UseMob : AbstractWalkAnimationAction2
    {
        private const string _mobTransitionAnimationString = "T_{0}{1}{2}_2_{3}";
        private const string _mobLoopAnimationString = "S_{0}{1}S{2}";
        private VobContainer _mobContainer;
        private GameObject _slotGo;
        private Vector3 _destination;
        private string _mobsiScheme;

        private string _schemeName => Action.String0;
        private int _desiredState => Action.Int0;
        private bool IsStopUsingMob => _desiredState <= -1;

        // -1 is the not-in-use state; scripts may send any negative value to stop.
        private int TargetState => IsStopUsingMob ? -1 : _desiredState;

        private bool _isMobFoundButNotYetInitialized;
        private string _currentMobAnimation;


        public UseMob(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        {
        }

        public override void Start()
        {
            // NPC is already interacting with a Mob, we therefore assume it's a change of state (e.g. -1 to stop Mob usage)
            if (Props.BodyState == VmGothicEnums.BodyState.BsMobinteract)
            {
                _mobContainer = PrefabProps.CurrentInteractable;
                _slotGo = PrefabProps.CurrentInteractableSlot;
                _mobsiScheme = _mobContainer.Props.GetVisualScheme();

                // We already stand at the slot. Without this, Tick() would treat the unset
                // _destination (0,0,0) as walk target and never reach TickMobUsage().
                _destination = _slotGo.transform.position;
                IsDestReached = true;

                StartMobUseAnimation();
                return;
            }

            // Else: We have a new animation where we seek the Mob before walking towards and executing action.
            var container = GetNearestMob();
            _mobContainer = container;
            _mobsiScheme = _mobContainer?.Props.GetVisualScheme();

            // No free Mobsi of this scheme within reach (e.g. all occupied by other NPCs).
            if (container == null || !container.Go)
            {
                IsFinishedFlag = true;
                return;
            }
            
            if (container.Go.GetComponent<VobLoader>().IsLoaded)
                StartNow();
            else
                StartDelayed();
        }

        private void StartNow()
        {
            _isMobFoundButNotYetInitialized = false;

            var slot = GetNearestMobSlot();

            if (slot == null)
            {
                IsFinishedFlag = true;
                return;
            }

            _slotGo = slot;
            _destination = _slotGo.transform.position;

            PrefabProps.CurrentInteractable = _mobContainer;
            PrefabProps.CurrentInteractableSlot = _slotGo;

            SetBodyState();

            // base.Start() checks the walk destination and may start the walk loop - it must only
            // run once _destination is set, and not at all when no slot was found.
            base.Start();
        }

        private void SetBodyState()
        {
            if (VmService.MobSit.Contains(_mobsiScheme))
                Props.BodyState = VmGothicEnums.BodyState.BsSit;
            else if (VmService.MobLie.Contains(_mobsiScheme))
                Props.BodyState = VmGothicEnums.BodyState.BsLie;
            else if (VmService.MobClimb.Contains(_mobsiScheme))
                Props.BodyState = VmGothicEnums.BodyState.BsClimb;
            else if (VmService.MobNotInterruptable.Contains(_mobsiScheme))
                Props.BodyState = VmGothicEnums.BodyState.BsMobinteract;
            else
                Props.BodyState = VmGothicEnums.BodyState.BsMobinteractInterrupt;
        }

        /// <summary>
        /// We need to wait until it's there...
        /// </summary>
        private void StartDelayed()
        {
            _isMobFoundButNotYetInitialized = true;
        }
        
        public override void Tick()
        {
            if (_isMobFoundButNotYetInitialized)
            {
                if (!_mobContainer.Go.GetComponent<VobLoader>().IsLoaded)
                {
                    return;
                }
                else
                {
                    StartNow();
                }
            }

            
            if (IsDestReached)
            {
                TickMobUsage();
            }
            else
            {
                base.Tick();
            }
        }

        private void TickMobUsage()
        {
            if (PrefabProps.AnimationSystem.IsPlaying(_currentMobAnimation))
                return;

            // A finished transition moves the state one step toward the target (e.g. S1 -> S0 -> Stand when stopping).
            if (Props.CurrentInteractableStateId != TargetState)
                UpdateState();

            // If we arrived at the Mobsi, we will further execute the transitions step-by-step until demanded state is reached.
            if (Props.CurrentInteractableStateId != TargetState)
            {
                PlayTransitionAnimation();
                return;
            }

            // Mobsi isn't in use any longer
            if (Props.CurrentInteractableStateId == -1)
            {
                PrefabProps.CurrentInteractable = null;
                PrefabProps.CurrentInteractableSlot = null;
                Props.CurrentItem = -1;
                Props.BodyState = VmGothicEnums.BodyState.BsStand;

                PhysicsService.EnablePhysicsForNpc(PrefabProps);
            }
            // Loop Mobsi animation until the same UseMob with -1 is called.
            else
            {
                // Loop animations carry the slot position as well (e.g. s_Bed_Front_S1 vs. s_Cauldron_S1).
                var animName = string.Format(_mobLoopAnimationString, _mobsiScheme, GetSlotPositionTag(_slotGo.name), TargetState);
                PrefabProps.AnimationSystem.PlayAnimation(animName);
            }

            IsFinishedFlag = true;
        }

        [CanBeNull]
        private VobContainer GetNearestMob()
        {
            var pos = NpcGo.transform.position;
            return VobService.GetFreeInteractableWithin10M(pos, Action.String0);
        }

        [CanBeNull]
        private GameObject GetNearestMobSlot()
        {
            if (_mobContainer == null)
                return null;

            var pos = NpcGo.transform.position;
            var slot = VobService.GetNearestSlot(_mobContainer.Go, pos);

            return slot;
        }

        protected override void OnDestinationReached()
        {
            base.OnDestinationReached();
            
            StartMobUseAnimation();
        }

        private void StartMobUseAnimation()
        {
            // Place item for Mobsi usage in hand - if needed. Will be "spawned" via animation >EventType.ItemInsert< later.
            var itemName = _mobContainer.VobAs<IInteractiveObject>().Item;
            if (itemName.NotNullOrEmpty())
            {
                var item = VmCacheService.TryGetItemData(itemName);
                Props.CurrentItem = item!.Index;
            }
            
            PhysicsService.DisablePhysicsForNpc(PrefabProps);

            NpcGo.transform.SetPositionAndRotation(_slotGo.transform.position, _slotGo.transform.rotation);

            // Already in the demanded state (e.g. a repeated AI_UseMob with the same state):
            // TickMobUsage() will replay the loop animation and finish without a transition.
            if (Props.CurrentInteractableStateId != TargetState)
                PlayTransitionAnimation();
        }

        private string GetSlotPositionTag(string name)
        {
            if (name.EndsWithIgnoreCase("_FRONT"))
            {
                return "_FRONT_";
            }

            if (name.EndsWithIgnoreCase("_BACK"))
            {
                return "_BACK_";
            }

            return "_";
        }

        protected override Vector3 GetWalkDestination()
        {
            return _destination;
        }

        private void UpdateState()
        {
            var step = Props.CurrentInteractableStateId > TargetState ? -1 : +1;
            Props.CurrentInteractableStateId += step;
        }

        private void PlayTransitionAnimation()
        {
            var current = Props.CurrentInteractableStateId;
            var next = current + (TargetState > current ? +1 : -1);

            // -1 is the not-in-use state and is named Stand inside the animations.
            // Both directions exist as own animations (e.g. t_Cauldron_Stand_2_S0, t_Cauldron_S0_2_S1, t_Cauldron_S1_2_S0).
            var from = current == -1 ? "Stand" : $"S{current}";
            var to = next == -1 ? "Stand" : $"S{next}";

            var slotPositionName = GetSlotPositionTag(_slotGo.name);
            var animName = string.Format(_mobTransitionAnimationString, _mobsiScheme, slotPositionName, from, to);

            _currentMobAnimation = animName;
            PrefabProps.AnimationSystem.PlayAnimation(animName);
        }
    }
}
