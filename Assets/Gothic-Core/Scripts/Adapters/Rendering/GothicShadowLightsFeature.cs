using Gothic.Core.Services.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Gothic.Core.Adapters.Rendering
{
    /// <summary>
    /// Tells the shaders where URP keeps this frame's shadow casting lamps/fires (StationaryLightsService picks them):
    /// their index among URP's additional lights. GothicShadowedLights (GothicShadows.hlsl) lights every object in their
    /// range with it - URP's per-object list holds 8 lights, a big world chunk near candles and oil lamps didn't get the
    /// lamp, its light ended at the chunk's edge.
    /// On the renderers with shadows (URP-HighFidelity-Renderer). Costs one loop over the visible lights per camera.
    /// </summary>
    public class GothicShadowLightsFeature : ScriptableRendererFeature
    {
        // 10 lamps/fires + hero light + spell projectiles (StationaryLightsService). Same as GothicShadows.hlsl.
        private const int _maxShadowLights = 16;

        private static readonly int _indicesShaderId = Shader.PropertyToID("_GothicShadowLightIndices");
        private static readonly int _countShaderId = Shader.PropertyToID("_GothicShadowLightCount");

        private ShadowLightsPass _pass;

        public override void Create()
        {
            _pass = new ShadowLightsPass { renderPassEvent = RenderPassEvent.BeforeRenderingShadows };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(_pass);
        }

        private class ShadowLightsPass : ScriptableRenderPass
        {
            private readonly Vector4[] _indices = new Vector4[_maxShadowLights / 4];
            private StationaryLightsService _lightsService;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                _lightsService ??= ReflexProjectInstaller.DIContainer?.Resolve<StationaryLightsService>();
                var lightData = frameData.Get<UniversalLightData>();
                var visibleLights = lightData.visibleLights;
                var count = 0;

                if (_lightsService != null && _lightsService.HasShadowLights)
                {
                    // URP's additional light index: the visible lights in order, without the main light, up to its cap
                    // (ForwardLights.SetupAdditionalLightConstants).
                    var additionalIndex = 0;
                    var maxAdditional = UniversalRenderPipeline.maxVisibleAdditionalLights;
                    for (var i = 0; i < visibleLights.Length && additionalIndex < maxAdditional; i++)
                    {
                        if (i == lightData.mainLightIndex)
                            continue;
                        var light = visibleLights[i].light;
                        if (count < _maxShadowLights && light != null && _lightsService.IsShadowLight(light))
                        {
                            var index = _indices[count / 4];
                            index[count % 4] = additionalIndex;
                            _indices[count / 4] = index;
                            count++;
                        }
                        additionalIndex++;
                    }
                }

                Shader.SetGlobalVectorArray(_indicesShaderId, _indices);
                Shader.SetGlobalFloat(_countShaderId, count);
            }
        }
    }
}
