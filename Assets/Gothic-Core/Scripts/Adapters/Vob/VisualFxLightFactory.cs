using Gothic.Core.Domain.Vobs;
using Gothic.Core.Services.Meshes;
using UnityEngine;

namespace Gothic.Core.Adapters.Vob
{
    public class VisualFxLightFactory : IVisualFxLightFactory
    {
        public IVisualFxLight Create(Transform parent, Vector3 position, LightPresetLoader.LightPreset preset,
            float rangeMeters)
        {
            return VisualFxLight.Create(parent, position, preset, rangeMeters);
        }
    }
}
