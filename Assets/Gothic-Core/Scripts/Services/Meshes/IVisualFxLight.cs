using System;
using Gothic.Core.Domain.Vobs;
using UnityEngine;

namespace Gothic.Core.Services.Meshes
{
    /// <summary>
    /// The light of a VISUALFX (the VisualFxLight adapter). ParticleService creates it with VisualFxLightFactory.Create.
    /// </summary>
    public interface IVisualFxLight
    {
        void SetRange(float rangeMeters);
        void FadeOut(float seconds, float delay = 0f);
        // The light's object - a spell's charging light is destroyed with its effects (VRRuneCaster).
        GameObject Go { get; }
    }

    public static class VisualFxLightFactory
    {
        /// <summary>
        /// Set by the VisualFxLight adapter at startup: parent, position, preset, range (below 0 the preset's).
        /// </summary>
        public static Func<Transform, Vector3, LightPresetLoader.LightPreset, float, IVisualFxLight> Create;
    }
}
