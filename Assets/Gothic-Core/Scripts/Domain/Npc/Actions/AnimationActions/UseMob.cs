using System.Linq;
using GUZ.Core.Adapters.Vob;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Container;
using GUZ.Core.Models.Vm;
using JetBrains.Annotations;
using MyBox;
using UnityEngine;
using ZenKit.Vobs;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class UseMob : AbstractWalkAnimationAction2
    {
        private const string _mobTransitionAnimationString = "T_{0}{1}{2}_2_{3}";
        private const string _mobLoopAnimationString = "S_{0}_S{1}";
        private VobContainer _mobContainer;
        private GameObject _slotGo;
        private Vector3 _destination;
        private string _mobsiScheme;

        private string _schemeName => Action.String0;
        private int _desiredState => Action.Int0;
        private bool IsStopUsingMob => _desiredState <= -1;

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

                StartMobUseAnimation();
                return;
            }

            // Else: We have a new animation where we seek the Mob before walking towards and executing action.
            var container = GetNearestMob();
            _mobContainer = container;
            _mobsiScheme = _mobContainer?.Props.GetVisualScheme();

            if (container!.Go == null)
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
            // We call Start only if the Mobsi is already available.
            base.Start();

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
            
            UpdateState();

            // If we arrived at the Mobsi, we will further execute the transitions step-by-step until demanded state is reached.
            if (Props.CurrentInteractableStateId != _desiredState)
            {
                PlayTransitionAnimation();
                return;
            }

            // Mobsi isn't in use any longer
            if (Props.CurrentInteractableStateId == -1)
            {
                PrefabProps.CurrentInteractable = null;
                PrefabProps.CurrentInteractableSlot = null;
                Props.BodyState = VmGothicEnums.BodyState.BsStand;

                PhysicsService.EnablePhysicsForNpc(PrefabProps);
            }
            // Loop Mobsi animation until the same UseMob with -1 is called.
            else
            {
                var animName = string.Format(_mobLoopAnimationString, _mobsiScheme, _desiredState);
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
            // FIXME - We need to check. For Cauldron/Cook we have only t_s0_2_Stand, but not t_s1_2_s0 - But is it for all of them?
            if (IsStopUsingMob)
            {
                Props.CurrentInteractableStateId = -1;
                Props.CurrentItem = -1;
            }
            else
            {
                var newStateAddition = Props.CurrentInteractableStateId > _desiredState ? -1 : +1;
                Props.CurrentInteractableStateId += newStateAddition;
            }
        }

        private void PlayTransitionAnimation()
        {
            string from;
            string to;

            // FIXME - We need to check. For Cauldron/Cook we have only t_s0_2_Stand, but not t_s1_2_s0 - But is it for all of them?
            if (IsStopUsingMob)
            {
                from = "S0";
                to = "Stand";
            }
            else
            {
                from = Props.CurrentInteractableStateId.ToString();
                to = $"S{Props.CurrentInteractableStateId + 1}";

                from = from switch
                {
                    "-1" => "Stand",
                    _ => $"S{from}"
                };
            }

            var slotPositionName = GetSlotPositionTag(_slotGo.name);
            var animName = string.Format(_mobTransitionAnimationString, _mobsiScheme, slotPositionName, from, to);

            _currentMobAnimation = animName;
            PrefabProps.AnimationSystem.PlayAnimation(animName);
        }
    }
}
