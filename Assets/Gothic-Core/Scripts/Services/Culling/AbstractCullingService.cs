using Gothic.Core.Domain.Culling;

namespace Gothic.Core.Services.Culling
{
    public abstract class AbstractCullingService
    {
        protected AbstractCullingDomain Domain;

        public virtual void Init()
        {
            Domain.Init();
            RegisterEventHandlers();
        }

        public void PreWorldCreate()
        {
            Domain.PreWorldCreate();
        }

        public void OnApplicationQuit()
        {
            UnregisterEventHandlers();
            Domain.OnApplicationQuit();
        }

        protected virtual void RegisterEventHandlers()
        {
            GlobalEventDispatcher.LoadGameStart.AddListener(Domain.PreWorldCreate);
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(Domain.PostWorldCreate);
        }

        protected virtual void UnregisterEventHandlers()
        {
            GlobalEventDispatcher.LoadGameStart.RemoveListener(Domain.PreWorldCreate);
            GlobalEventDispatcher.WorldSceneLoaded.RemoveListener(Domain.PostWorldCreate);
        }
    }
}
