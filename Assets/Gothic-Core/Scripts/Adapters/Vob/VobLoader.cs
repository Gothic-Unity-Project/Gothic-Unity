using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Adapters.Vob
{
    public class VobLoader : MonoBehaviour
    {
        public VobContainer Container;
        public bool IsLoaded;
        public bool IsQueued;

        /// <summary>
        /// DeveloperConfig.DebugTraceVobVisual matched this VOB: its creation, init and destruction are logged.
        /// </summary>
        public bool IsTraced;

        private void OnDestroy()
        {
            if (IsTraced)
                Logging.Logger.LogWarning($"[VobTrace] '{name}' destroyed at {transform.position} (loaded={IsLoaded})",
                    Logging.LogCat.Vob);
        }
    }
}
