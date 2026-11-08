using Gothic.Core.Domain.Vobs;
using UnityEngine;

namespace Gothic.Core.Services.Meshes
{
    /// <summary>
    /// Creates the light of a VISUALFX. Implemented in Adapters/, registered in ReflexProjectInstaller.
    /// </summary>
    public interface IVisualFxLightFactory
    {
        /// <summary>
        /// A light at the position, following the parent. rangeMeters below 0: the preset's range.
        /// </summary>
        IVisualFxLight Create(Transform parent, Vector3 position, LightPresetLoader.LightPreset preset,
            float rangeMeters);
    }
}
