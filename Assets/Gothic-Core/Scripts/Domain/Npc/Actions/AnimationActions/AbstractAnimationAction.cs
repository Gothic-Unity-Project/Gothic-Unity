using Gothic.Core.Adapters.Npc;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Creator;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

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
        [Inject] protected readonly ConfigService ConfigService;


        protected readonly NpcContainer NpcContainer;
        protected readonly NpcInstance NpcInstance;
        protected readonly NpcProperties Props;
        protected readonly NpcProxy Vob;

        // Read when used, not when queued: scripts queue actions (AI_Output, SVMs) for NPCs whose object isn't built
        // yet - a copy taken then stayed null and the action threw every frame.
        protected GameObject NpcGo => NpcContainer.Go;
        protected NpcPrefabProperties PrefabProps => NpcContainer.PrefabProps;

        protected float ActionTime;
        protected float ActionEndEventTime;

        protected bool IsFinishedFlag;

        protected AbstractAnimationAction(AnimationAction action, NpcContainer npcData)
        {
            Action = action;
            NpcContainer = npcData;
            NpcInstance = npcData.Instance;
            Props = npcData.Props;
            Vob = npcData.Vob;

            // As we will need a lot of different *Service interactions, we inject all of our elements now.
            this.Inject();
        }

        public virtual void Start()
        {
            // By default, every Daedalus animation starts without using physics. But they can always overwrite it (e.g.) for walking.
            PhysicsService.DisablePhysicsForNpc(PrefabProps);
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

        /// <summary>
        /// Whether the action is done, for other NPCs to ask (AI_WaitTillEnd). Unlike IsFinished() it doesn't move the
        /// action on - some count their time down in IsFinished().
        /// </summary>
        public virtual bool HasEnded => IsFinishedFlag;

        public virtual void StopImmediately()
        {
            IsFinishedFlag = true;
        }
    }
}
