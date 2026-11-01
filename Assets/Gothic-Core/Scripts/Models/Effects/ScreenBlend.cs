using UnityEngine;

namespace Gothic.Core.Models.Effects
{
    /// <summary>
    /// A color or texture over the whole view, from a visual effect with the screenblend.scx visual (DEMENTOR_FX,
    /// TRANSFORM_CASTBLEND, ...). Its user strings: loop seconds, color "r g b a" (0-255), fade in/out seconds, texture,
    /// texture frames per second.
    /// </summary>
    public class ScreenBlend
    {
        /// <summary>
        /// The effect that started it - Wld_StopEffect ends it by this name.
        /// </summary>
        public string EffectName;

        /// <summary>
        /// With a texture, only the alpha is used (the texture keeps its colors).
        /// </summary>
        public Color Color;

        public float FadeSeconds;

        /// <summary>
        /// How long it stays, 0 until Wld_StopEffect (DEMENTOR_FX).
        /// </summary>
        public float Seconds;

        /// <summary>
        /// First frame of an animated texture (ScreenFX_Dem_a0 - the frames count up), null for a plain color.
        /// </summary>
        public string TextureName;

        public float FramesPerSecond;
    }
}
