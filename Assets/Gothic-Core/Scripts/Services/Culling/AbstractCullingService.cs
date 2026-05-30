using GUZ.Core.Domain.Culling;

namespace GUZ.Core.Services.Culling
{
    public abstract class AbstractCullingService
    {
        protected AbstractCullingDomain Domain;

        public virtual void Init()
        {
            Domain.Init();
            RegisterEventHandlers();
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
