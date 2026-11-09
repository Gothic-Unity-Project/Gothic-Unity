using UnityEngine;

namespace Gothic.Core.Services.Meshes
{
    /// <summary>
    /// The light of a VISUALFX (the VisualFxLight adapter). ParticleService creates it with IVisualFxLightFactory.
    /// </summary>
    public interface IVisualFxLight
    {
        void SetRange(float rangeMeters);
        void FadeOut(float seconds, float delay = 0f);
    }
}
