using UnityEngine;

namespace Gothic.Core.Models.Vob
{
    /// <summary>
    /// A Gothic light (zCVobLight) in Unity units: range in meters, color, and its animation - the range scales and
    /// the colors play in a loop at their fps, blended between the keys when smooth, else stepping. A light without
    /// any animation gets a gentle default flicker of its range, so a flame never looks frozen.
    /// </summary>
    public readonly struct LightPreset
    {
        // Default flicker: +-8 % of the range, smooth, 8 new values per second.
        private const float _defaultFlickerAmount = 0.08f;
        private const float _defaultFlickerFps = 8f;

        public readonly float Range;
        public readonly Color Color;
        public readonly float[] RangeAnimationScales;
        public readonly float RangeAnimationFps;
        public readonly bool IsRangeAnimationSmooth;
        public readonly Color[] ColorAnimation;
        public readonly float ColorAnimationFps;
        public readonly bool IsColorAnimationSmooth;
        /// <summary>
        /// Gothic's lens flare effect of the light (e.g. TORCHFX01, defined in LENSFLARE.ZEN) - not a texture.
        /// </summary>
        public readonly string LensFlareName;

        public LightPreset(float range, Color color, float[] rangeAnimationScales = null, float rangeAnimationFps = 0f,
            bool isRangeAnimationSmooth = false, Color[] colorAnimation = null, float colorAnimationFps = 0f,
            bool isColorAnimationSmooth = false, string lensFlareName = null)
        {
            Range = range;
            Color = color;
            RangeAnimationScales = rangeAnimationScales;
            RangeAnimationFps = rangeAnimationFps;
            IsRangeAnimationSmooth = isRangeAnimationSmooth;
            ColorAnimation = colorAnimation;
            ColorAnimationFps = colorAnimationFps;
            IsColorAnimationSmooth = isColorAnimationSmooth;
            LensFlareName = lensFlareName;
        }

        public bool HasRangeAnimation => RangeAnimationScales is { Length: > 0 } && RangeAnimationFps > 0f;

        public bool HasColorAnimation => ColorAnimation is { Length: > 0 } && ColorAnimationFps > 0f;

        /// <summary>
        /// Range (m) and color at this time (s). No allocation - called every frame.
        /// </summary>
        public (float Range, Color Color) Evaluate(float time)
        {
            var range = Range;
            var color = Color;

            if (HasRangeAnimation)
                range *= Sample(RangeAnimationScales, RangeAnimationFps, IsRangeAnimationSmooth, time);
            if (HasColorAnimation)
                color = Sample(ColorAnimation, ColorAnimationFps, IsColorAnimationSmooth, time);
            if (!HasRangeAnimation && !HasColorAnimation)
                range *= 1f + _defaultFlickerAmount * GetDefaultFlicker(time);

            return (range, color);
        }

        private static float Sample(float[] keys, float fps, bool isSmooth, float time)
        {
            var frame = time * fps;
            var index = Mathf.FloorToInt(frame);
            var current = keys[Wrap(index, keys.Length)];
            if (!isSmooth)
                return current;

            return Mathf.Lerp(current, keys[Wrap(index + 1, keys.Length)], frame - index);
        }

        private static Color Sample(Color[] keys, float fps, bool isSmooth, float time)
        {
            var frame = time * fps;
            var index = Mathf.FloorToInt(frame);
            var current = keys[Wrap(index, keys.Length)];
            if (!isSmooth)
                return current;

            return Color.Lerp(current, keys[Wrap(index + 1, keys.Length)], frame - index);
        }

        private static int Wrap(int index, int length)
        {
            var wrapped = index % length;
            return wrapped < 0 ? wrapped + length : wrapped;
        }

        /// <summary>
        /// -1...1, blended between random values that are the same for the same time.
        /// </summary>
        private static float GetDefaultFlicker(float time)
        {
            var frame = time * _defaultFlickerFps;
            var index = Mathf.FloorToInt(frame);
            return Mathf.Lerp(GetNoise(index), GetNoise(index + 1), Mathf.SmoothStep(0f, 1f, frame - index));
        }

        private static float GetNoise(int index)
        {
            var hash = unchecked((uint)index * 0x9E3779B9u);
            hash ^= hash >> 16;
            hash = unchecked(hash * 0x85EBCA6Bu);
            hash ^= hash >> 13;
            return (hash & 0xFFFF) / 32767.5f - 1f;
        }
    }
}
