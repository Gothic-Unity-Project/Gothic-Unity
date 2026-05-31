using System.Collections.Generic;
using System.Threading.Tasks;
using GUZ.Core.Adapters.UI.LoadingBars;
using GUZ.Core.Extensions;
using Gothic.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Context;
using GUZ.Core.Services.StaticCache;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Vobs;
using Light = ZenKit.Vobs.Light;
using Logger = Gothic.Core.Logging.Logger;

namespace GUZ.Core.Domain.StaticCache
{
    public class StationaryLightCacheCreatorDomain
    {
        public List<StaticCacheService.StationaryLightInfo> StationaryLightInfos = new();
        // Used for world chunk creation.
        public List<Bounds> StationaryLightBounds = new();


        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly FrameSkipperService _frameSkipperService;
        [Inject] private readonly LoadingService _loadingService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        
        private bool _debugSpeedUpLoading => _configService.Dev.SpeedUpLoading;


        public async Task CalculateStationaryLights(List<IVirtualObject> vobs, int worldIndex)
        {
            var elementAmount = CalculateElementAmount(vobs);
            _loadingService.SetPhase($"{nameof(PreCachingLoadingBarHandler.ProgressTypesPerWorld.CalculateStationaryLights)}_{worldIndex}", elementAmount);

            await CalculateStationaryLights(vobs);
            _loadingService.FinalizePhase();
        }
        
        private int CalculateElementAmount(List<IVirtualObject> vobs)
        {
            var count = 0;
            foreach (var vob in vobs)
            {
                count++; // We count each element as we update potentially with each FrameSkipper call, which is unaffected if it's a light or sth. else.
                count += CalculateElementAmount(vob.Children);
            }
            return count;
        }

        private bool _isFireTreeG2LoggedOnce = false;
        private async Task CalculateStationaryLights(List<IVirtualObject> vobs, Vector3 parentWorldPosition = default)
        {
            foreach (var vob in vobs)
            {
                if (!_debugSpeedUpLoading)
                {
                    await _frameSkipperService.TrySkipToNextFrame();
                }
                _loadingService.Tick();

                var vobWorldPosition = CalculateWorldPosition(parentWorldPosition, vob.Position.ToUnityVector());

                if (vob.Type == VirtualObjectType.zCVobLight)
                {
                    // TODO - Check if we need to look for "isStatic == true"?

                    var light = (Light)vob;

                    // For chunking and shader usage, we need to use the non-static lights (e.g. from a fire) only.
                    // StaticLights will be handled later.
                    if (light.LightStatic)
                    {
                        continue;
                    }

                    var linearColor = new Color(light.Color.R / 255f, light.Color.G / 255f, light.Color.B / 255f, light.Color.A / 255f).linear;
                    // Range/100 --> m (ZenKit) in centimeter (Unity)
                    StationaryLightInfos.Add(new StaticCacheService.StationaryLightInfo(vobWorldPosition, light.Range / 100, linearColor));

                    // Calculation: Vector3.One (vectorify) * Range / 100 (centimeter to meter in Unity) * 2 (from range (half-width) to width)
                    var boundsSize = Vector3.one * light.Range / 100 * 2;
                    StationaryLightBounds.Add(new Bounds(vobWorldPosition, boundsSize));
                }
                else if (vob.Type == VirtualObjectType.oCMobFire)
                {
                    var fire = (Fire)vob;

                    if (fire.VobTree.IsNullOrEmpty())
                    {
                        continue;
                    }

                    // FIXME - For some reason, FIRETREE_LARGE.ZEN is broken in G2. Let's fix it properly later.)
                    if (_contextGameVersionService.Version == GameVersion.Gothic2 && fire.VobTree == "FIRETREE_LARGE.ZEN")
                    {
                        if (!_isFireTreeG2LoggedOnce)
                            Logger.LogError("For some reason, FIRETREE_LARGE.ZEN is broken in G2. Let's fix it properly for caching.", LogCat.PreCaching);
                        _isFireTreeG2LoggedOnce = true;
                        continue;
                    }

                    var fireWorldVobs = _resourceCacheService.TryGetWorld(fire.VobTree, _contextGameVersionService.Version, true)!.RootObjects;

                    // As we loaded the child-VOBs for fire*.zen at this time, we iterate now.
                    await CalculateStationaryLights(fireWorldVobs, vobWorldPosition);
                }

                // Recursion
                await CalculateStationaryLights(vob.Children, vobWorldPosition);
            }
        }

        /// <summary>
        /// Elements like Fire have children which position is relative to parent.
        /// But at least Fire are .zen files where the children have zeroed world positions which we need to handle properly.
        ///
        /// TODO - Rotation isn't handled for positioning right now. I'm not sure if we need it. If so, please add.
        /// </summary>
        private Vector3 CalculateWorldPosition(Vector3 parentPosition, Vector3 localPosition)
        {
            return parentPosition + localPosition;
        }
    }
}
