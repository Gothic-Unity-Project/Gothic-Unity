using System;
using System.Linq;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Creator;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Npc.Actions.AnimationActions
{
    public abstract class AbstractAnimationAction
    {
        public readonly AnimationAction Action;

        [Inject] protected readonly AnimationService AnimationService;
        [Inject] protected readonly VmCacheService VmCacheService;
        [Inject] protected readonly PhysicsService PhysicsService;
        [Inject] protected readonly WayNetService WayNetService;
        [Inject] protected readonly VobService VobService;
        [Inject] protected readonly VmService VmService;
        [Inject] protected readonly GameStateService GameStateService;


        protected readonly NpcContainer NpcContainer;
        protected readonly NpcInstance NpcInstance;
        protected readonly GameObject NpcGo;
        protected readonly NpcProperties Props;
        protected readonly NpcProxy Vob;
        protected readonly NpcPrefabProperties PrefabProps;

        protected float ActionTime;
        protected float ActionEndEventTime;

        protected bool IsFinishedFlag;

        protected AbstractAnimationAction(AnimationAction action, NpcContainer npcData)
        {
            Action = action;
            NpcContainer = npcData;
            NpcInstance = npcData.Instance;
            NpcGo = npcData.Go;
            Props = npcData.Props;
            Vob = npcData.Vob;
            PrefabProps = npcData.PrefabProps;

            // As we will need a lot of different *Service interactions, we inject all of our elements now.
            this.Inject();
        }

        public virtual void Start()
        {
            // By default, every Daedalus animation starts without using physics. But they can always overwrite it (e.g.) for walking.
            PhysicsService.DisablePhysicsForNpc(PrefabProps);
        }

        protected virtual void InsertItem(string slot1, string slot2)
        {
            if (slot2.Any())
            {
                throw new Exception("Slot 2 is set but not yet handled by InsertItem as AnimationEvent.");
            }

            var slotGo = NpcGo.FindChildRecursively(slot1);

            VobService.CreateItemMesh(Props.CurrentItem, slotGo);

            Props.UsedItemSlot = slot1;
        }

        private void RemoveItem()
        {
            // Some animations need to force remove items, some not.
            if (Props.UsedItemSlot == "")
            {
                return;
            }

            var slotGo = NpcGo.FindChildRecursively(Props.UsedItemSlot);
            var item = slotGo!.transform.GetChild(0);

            Object.Destroy(item.gameObject);
        }

        /// <summary>
        /// Called every update cycle.
        /// Can be used to handle frequent things internally.
        /// </summary>
        public virtual void Tick()
        {
            ActionTime += Time.deltaTime;

            if (ActionEndEventTime != 0.0f && ActionTime >= ActionEndEventTime)
            {
                AnimationEnd();
            }

        }

        /// <summary>
        /// Most of our animations are fine if we just set this flag and return it via IsFinished()
        /// If an animation has also a next animation set, it will be called within NpcAnimationHandler automatically (e.g. idle animation).
        /// </summary>
        protected virtual void AnimationEnd()
        {
            IsFinishedFlag = true;
        }

        /// <summary>
        /// Most of our animations are fine if we just set this flag and return it via IsFinished()
        /// </summary>
        public virtual bool IsFinished()
        {
            return IsFinishedFlag;
        }

        public virtual void StopImmediately()
        {
            IsFinishedFlag = true;
        }
    }
}
