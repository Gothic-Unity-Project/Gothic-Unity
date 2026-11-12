using UnityEngine;

namespace Gothic.Core.Models.Vob
{
    /// <summary>
    /// What the engine shows for a burning torch in hand - read from the visual of ItLsTorchBurning
    /// (ITLSTORCHBURNING.ZEN: the torch mesh with a fire particle effect and a light below it).
    /// G1: 15 m range, G2: 10 m; both flicker through a list of colors.
    /// </summary>
    public class BurningTorchVisual
    {
        public string FirePfxName;
        // Fire and light relative to the torch mesh's pivot (the grip), in meters.
        public Vector3 FireLocalPosition;
        public float LightRange;
        public Color LightColor;
        // The light's flicker: its color steps through these at LightColorFps. Empty - always LightColor.
        public Color[] LightColors;
        public float LightColorFps;

        /// <summary>
        /// The light's color at a time in seconds - the flicker step, or LightColor without a list.
        /// </summary>
        public Color GetLightColor(float seconds)
        {
            if (LightColors == null || LightColors.Length == 0 || LightColorFps <= 0f)
                return LightColor;
            return LightColors[(int)(seconds * LightColorFps) % LightColors.Length];
        }
    }
}
