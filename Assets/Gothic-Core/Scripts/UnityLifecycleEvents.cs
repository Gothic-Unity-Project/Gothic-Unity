using GUZ.Core.Manager;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Culling;
using GUZ.Core.Services.Player;
using GUZ.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.VR
{
    /// <summary>
    /// Each Service can be added to leverage Unity lifecycle events.
    /// This ensures a central overview of usage.
    /// </summary>
    public class UnityLifecycleEvents : MonoBehaviour
    {
        [Inject] private readonly GameStateService _gameStateService;
        
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly VobSoundCullingService _vobSoundCullingService;

        [Inject] private readonly FrameSkipperService _frameSkipperService;

        // Caches
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly MorphMeshCacheService _morphMeshCacheService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly NpcArmorPositionCacheService _npcArmorCacheService;

        // Misc
        [Inject] private readonly StationaryLightsService _lightsService;
        [Inject] private readonly SkyService _skyService;
        [Inject] private readonly BarrierService _barrierService;
        [Inject] private readonly SpeechToTextService _speechToTextService;


        private void Update()
        {
            _frameSkipperService.Update();
            _npcMeshCullingService.Update();
            _loadingService.Update();
        }

        private void LateUpdate()
        {
            _lightsService.LateUpdate();
        }

        private void FixedUpdate()
        {
            _barrierService.FixedUpdate();
        }

        private void OnApplicationQuit()
        {
            _gameStateService.Dispose();

            _npcMeshCullingService.OnApplicationQuit();
            _vobMeshCullingService.OnApplicationQuit();
            _vobSoundCullingService.OnApplicationQuit();

            // Caches
            _textureCacheService.Dispose();
            _morphMeshCacheService.Dispose();
            _multiTypeCacheService.Dispose();
            _npcArmorCacheService.Dispose();
            _vmCacheService.Dispose();
            
            _speechToTextService.Dispose();
        }


#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            _vobMeshCullingService?.OnDrawGizmos();
        }

        private void OnValidate()
        {
            _skyService?.OnValidate();
        }
#endif
    }
}
