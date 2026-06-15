using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Domain.Culling
{
    public abstract class AbstractCullingDomain
    {
        [Inject] protected readonly ConfigService ConfigService;


        protected enum State
        {
            None,
            Loading,
            WorldLoaded
        }

        /// <summary>
        /// Logical culling state of a tracked object. Unknown forces the first event/initial sweep to apply a state.
        /// </summary>
        protected enum ObjectState : byte
        {
            Unknown,
            Enabled,
            Disabled
        }

        // Objects can move between cullingDistance and cullingDistance*factor without a state change (hysteresis).
        // Prevents SetActive() flickering at the culling border while the VR headset bobs around.
        protected const float HysteresisFactor = 1.15f;

        protected State CurrentState;

        // Stored for resetting after world switch
        protected CullingGroup CullingGroup;

        // Main camera transform, set once the world finished loading.
        protected Transform ReferencePoint;

        protected abstract void VisibilityChanged(CullingGroupEvent evt);


        public virtual void Init()
        {
            // Unity demands CullingGroups to be created in Awake() or Start() earliest.
            CullingGroup = new CullingGroup();
        }

        public virtual void PreWorldCreate()
        {
            CullingGroup.Dispose();
            CullingGroup = new CullingGroup();

            CurrentState = State.Loading;
        }

        /// <summary>
        /// Set main camera once world is loaded fully.
        /// Doesn't work at loading time as we change scenes etc.
        /// </summary>
        public virtual void PostWorldCreate()
        {
            // Set main camera as reference point
            var mainCamera = Camera.main!;
            CullingGroup.targetCamera = mainCamera; // Needed for FrustumCulling and OcclusionCulling to work.
            CullingGroup.SetDistanceReferencePoint(mainCamera.transform); // Needed for BoundingDistances to work.
            ReferencePoint = mainCamera.transform;

            CurrentState = State.WorldLoaded;
        }

        public virtual void OnApplicationQuit()
        {
            CullingGroup.Dispose();
        }
    }
}
