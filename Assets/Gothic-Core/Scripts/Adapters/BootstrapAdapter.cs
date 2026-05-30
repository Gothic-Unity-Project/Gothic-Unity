using GUZ.Core.Manager;
using GUZ.Core.Models.Config;
using GUZ.Core.Services;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Adapters
{
    public class BootstrapAdapter : MonoBehaviour
    {
        public DeveloperConfig DeveloperConfig;

        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        private void Awake()
        {
            // Simply set "any" MonoBehaviour (like this) as object to use within game logic.
            _unityMonoService.SetMonoBehaviour(this);
            
            _bootstrapService.AwakeUnity(DeveloperConfig);
        }

        private void Start()
        {
            _bootstrapService.StartUnity();
        }
    }
}
