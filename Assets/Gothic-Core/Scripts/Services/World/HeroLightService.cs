using Gothic.Core.Logging;
using Gothic.Core.Models.Vob;
using UnityEngine;
using UnityEngine.Rendering;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// One sharp light that the lit shaders compute for every pixel and by the angle of each surface
    /// (GothicHeroLight.hlsl) - the burning torch in the player's hand. All other lights stay as they are (per vertex,
    /// see Docs/lighting.md). It follows its anchor every frame and flickers with its profile's animation. One owner at
    /// a time: Show takes it over, only that owner's Hide fades it out. It goes out at once when the anchor is gone or
    /// the world unloads. This service only owns the shader state (two globals and the keyword _HERO_LIGHT).
    /// </summary>
    public class HeroLightService
    {
        // As long as the light pool needs to give a returning normal light its slot again - so there's no dark moment.
        private const float _fadeSeconds = 0.5f;

        private static readonly int _positionRangeId = Shader.PropertyToID("_HeroLightPositionRange");
        private static readonly int _colorId = Shader.PropertyToID("_HeroLightColor");
        private static readonly GlobalKeyword _keyword = GlobalKeyword.Create("_HERO_LIGHT");

        private object _owner;
        private Transform _anchor;
        private LightProfile _profile;
        private bool _isOn;
        private bool _isFading;
        private float _fadeEndTime;

        public HeroLightService()
        {
            // The editor keeps global keywords between play sessions.
            Shader.SetKeyword(_keyword, false);
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(TurnOff);
        }

        /// <summary>
        /// The light shines from the anchor with the profile's range, color and flicker. Takes the light over from any
        /// other owner and stops a fade-out.
        /// </summary>
        public void Show(object owner, Transform anchor, LightProfile profile)
        {
            if (owner == null || anchor == null)
                return;

            _owner = owner;
            _anchor = anchor;
            _profile = profile;
            _isFading = false;
            if (_isOn)
                return;

            _isOn = true;
            Shader.SetKeyword(_keyword, true);
            Logger.Log($"[HeroLight] on at {anchor.name}, range {profile.Range:F1} m.", LogCat.Vob);
        }

        /// <summary>
        /// The owner's light fades out. Another owner's light stays.
        /// </summary>
        public void Hide(object owner)
        {
            if (!_isOn || _isFading || owner != _owner)
                return;

            _isFading = true;
            _fadeEndTime = Time.time + _fadeSeconds;
        }

        public void LateUpdate()
        {
            if (!_isOn)
                return;

            if (_anchor == null)
            {
                TurnOff();
                return;
            }

            var fade = 1f;
            if (_isFading)
            {
                fade = (_fadeEndTime - Time.time) / _fadeSeconds;
                if (fade <= 0f)
                {
                    TurnOff();
                    return;
                }
            }

            var (range, color) = _profile.Evaluate(Time.time);
            var position = _anchor.position;
            Shader.SetGlobalVector(_positionRangeId,
                new Vector4(position.x, position.y, position.z, 1f / Mathf.Max(range * range, 0.01f)));
            // Linear like the other lights' colors (StationaryLight.RuntimeLinearColor).
            var linear = color.linear;
            Shader.SetGlobalVector(_colorId, new Vector4(linear.r * fade, linear.g * fade, linear.b * fade, 0f));
        }

        private void TurnOff()
        {
            _owner = null;
            _anchor = null;
            _isFading = false;
            if (!_isOn)
                return;

            _isOn = false;
            Shader.SetKeyword(_keyword, false);
            Logger.Log("[HeroLight] off.", LogCat.Vob);
        }
    }
}
