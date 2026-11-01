using Gothic.Core.Manager;
using Gothic.Core.Models.Config;
using Gothic.Core.Services;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Adapters
{
    public class BootstrapAdapter : MonoBehaviour
    {
        public DeveloperConfig DeveloperConfig;

        /// <summary>
        /// A start-up seam for tests and tools: set before the Bootstrap scene loads, the game starts with this config
        /// instead of the scene's. It is used once and cleared, so the next start uses the scene's again.
        /// </summary>
        public static DeveloperConfig ConfigOverride;

        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        private void Awake()
        {
            // Simply set "any" MonoBehaviour (like this) as object to use within game logic.
            _unityMonoService.SetMonoBehaviour(this);
            
            var config = ConfigOverride != null ? ConfigOverride : DeveloperConfig;
            ConfigOverride = null;
            _bootstrapService.AwakeUnity(config);
        }

        private void Start()
        {
            _bootstrapService.StartUnity();
        }
    }
}
