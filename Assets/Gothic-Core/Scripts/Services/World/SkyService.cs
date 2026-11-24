using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Sky;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Extensions;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Gothic.Core.Services.World
{
    public class SkyService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly StationaryLightsService _stationaryLightsService;

        // The light of indoor worlds (no sun, no fog): dark - mines are lit by their lamps (lightmaps), fires and the
        // hero's torch/Light spell, not by a sky (0.25 lit the Old Mine's rock like a cloudy day).
        private static readonly Color _indoorLight = new(0.12f, 0.12f, 0.15f);

        // The sun's path is tilted by this much towards the south: at noon it's 60 degrees high, shadows never vanish
        // under the objects. Gothic's own sun stands still - its shadows are baked into the vertex light.
        private const float _sunPathTilt = 30f;
        // Degrees above the horizon over which the shadows fade in (sunrise/sunset, moonrise/moonset).
        private const float _sunHorizonFadeDegrees = 8f;
        // How dark the sun's/moon's shadow is: GD3D11 leaves 40% of the sun light in the shadow; the moon is weaker.
        private const float _sunShadowStrength = 0.6f;
        private const float _moonShadowStrength = 0.35f;

        // Darker nights, so lamps and fires stand out (GD3D11-like): the sky's polyColor (Gothic's day light) at night
        // (brightness ~0.43) lights the world at _nightLightFactor of it; day and dusk (from ~0.85) stay as they are.
        private const float _nightLightFactor = 0.5f;
        private const float _nightBrightness = 0.45f;
        private const float _duskBrightness = 0.85f;
        // Rain and thunderstorms: the world's light at full rain (clouds), and the sun's shadow weaker under them.
        private const float _rainLightFactor = 0.7f;
        private const float _rainShadowFactor = 0.35f;

        // The direction the light travels (from the sun - at night the moon - to the ground): the shaders' _SunDirection
        // and the "Sun" light's forward.
        private Vector3 _sunDirection = Vector3.down;
        // URP's main light, only for its shadow map (dynamic sun/moon shadows, GothicShadows.hlsl) and URP-lit
        // materials. Gothic's shaders take the sun's color from _SunColor. Per world (lives in the world scene).
        private Light _sunLight;
        // 0 at the horizon, 1 a few degrees above: the light switches between sun and moon unseen.
        private float _sunShadowFade;
        private bool _isRaining;
        public bool IsRaining => _isRaining;

        private float _masterTime;
        private bool _noSky = true;

        private List<SkyState> _stateList = new();

        // Last interpolated sky polyColor, re-applied by SetShaderProperties() so it doesn't reset the night.
        private Color _polyColor = Color.white;

        // World's BSP tree is indoor (dungeons, mines): the time of day doesn't darken it, see SetDayLight().
        private bool _isIndoorWorld;

        private static readonly int _sunDirectionShaderId = Shader.PropertyToID("_SunDirection");
        private static readonly int _sunColorShaderId = Shader.PropertyToID("_SunColor");
        private static readonly int _ambientShaderId = Shader.PropertyToID("_AmbientColor");
        private static readonly int _pointLightIntensityShaderId = Shader.PropertyToID("_PointLightIntensity");
        private static readonly int _dayLightColorShaderId = Shader.PropertyToID("_DayLightColor");
        private static readonly int _underwaterColorShaderId = Shader.PropertyToID("_UnderwaterColor");

        private SkyStateRain _rainState = new();

        // Rolled every day at noon (UpdateRainTime()). Game state: it survives world changes and is saved
        // (SkySaveSection). Null until the first roll: InitRainState() uses Gothic's default then.
        private (float Start, float End)? _rainWindow;
        private ParticleSystem _rainParticleSystem;
        private AudioSource _rainParticleSound;
        private float _rainWeightAndVolume;

        private const int _maxParticleCount = 700;

        private static readonly int _skyTex1ShaderId = Shader.PropertyToID("_Sky1");
        private static readonly int _skyTex2ShaderId = Shader.PropertyToID("_Sky2");
        private static readonly int _skyTex3ShaderId = Shader.PropertyToID("_Sky3");
        private static readonly int _skyTex4ShaderId = Shader.PropertyToID("_Sky4");
        private static readonly int _skyMovement1ShaderId = Shader.PropertyToID("_Vector1");
        private static readonly int _skyMovement2ShaderId = Shader.PropertyToID("_Vector2");
        private static readonly int _skyMovement3ShaderId = Shader.PropertyToID("_Vector3");
        private static readonly int _skyMovement4ShaderId = Shader.PropertyToID("_Vector4");
        private static readonly int _sky1OpacityShaderId = Shader.PropertyToID("_Sky1Opacity");
        private static readonly int _sky2OpacityShaderId = Shader.PropertyToID("_Sky2Opacity");
        private static readonly int _sky3OpacityShaderId = Shader.PropertyToID("_Sky3Opacity");
        private static readonly int _sky4OpacityShaderId = Shader.PropertyToID("_Sky4Opacity");
        private static readonly int _layersBlendShaderId = Shader.PropertyToID("_LayerBlend");
        private static readonly int _fogColor1ShaderId = Shader.PropertyToID("_FogColor");
        private static readonly int _fogColor2ShaderId = Shader.PropertyToID("_FogColor2");
        private static readonly int _domeColor1ShaderId = Shader.PropertyToID("_DomeColor1");
        private static readonly int _domeColor2ShaderId = Shader.PropertyToID("_DomeColor2");


        public void OnValidate()
        {
            SetShaderProperties();
        }

        /// <summary>
        /// Start and end of today's rain (0...1 of the sky's day, which starts at noon).
        /// </summary>
        public (float Start, float End) GetRainWindow()
        {
            return (_rainState.Time, _rainState.EndTime);
        }

        /// <summary>
        /// The next world start uses Gothic's default rain window.
        /// </summary>
        public void ResetRainWindow()
        {
            _rainWindow = null;
        }

        public void SetRainWindow(float start, float end)
        {
            _rainWindow = (start, end);
            _rainState.Time = start;
            _rainState.EndTime = end;
        }

        public void InitWorld()
        {
            // Remove old world events if necessary.
            GlobalEventDispatcher.GameTimeSecondChangeCallback.RemoveListener(Interpolate);
            GlobalEventDispatcher.GameTimeHourChangeCallback.RemoveListener(UpdateRainTime);
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);

            // And now re-add the events.
            GlobalEventDispatcher.GameTimeSecondChangeCallback.AddListener(Interpolate);
            GlobalEventDispatcher.GameTimeHourChangeCallback.AddListener(UpdateRainTime);
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);

            _isIndoorWorld = _saveGameService.CurrentWorldData?.BspTree?.Type == ZenKit.BspTreeType.Indoor;
            Logger.Log($"[Sky] Indoor world: {_isIndoorWorld} - day/night light is " +
                       $"{(_isIndoorWorld ? "off" : "on")}.", LogCat.Loading);

            RenderSettings.skybox = Object.Instantiate(_textureService.SkyMaterial);
            InitRainGo();
            InitSky();
            if (_isIndoorWorld)
            {
                RenderSettings.fog = false;
                Logger.Log("[Sky] Indoor world - no sun, no fog", LogCat.Vob);
            }
        }

        /// <summary>
        /// We need to initialize sky with its colors to show mesh textures.
        /// Otherwise, our Lit/World and Lit/SingleMesh shaders will show black textures only.
        /// </summary>
        private void InitSky()
        {
            RotateSun(_gameTimeService.GetCurrentDateTime());
            // Once per world: a second world load added a second listener.
            GlobalEventDispatcher.GameTimeSecondChangeCallback.RemoveListener(RotateSun);
            GlobalEventDispatcher.GameTimeMinuteChangeCallback.RemoveListener(RotateSun);
            GlobalEventDispatcher.GameTimeHourChangeCallback.RemoveListener(RotateSun);
            // Every game second (14 times a second, a 240th of a degree) the sun's shadows move smoothly; once per game
            // minute they jumped.
            switch (_configService.Dev.SunUpdateInterval)
            {
                case GameTimeService.GameTimeInterval.EveryGameSecond:
                    GlobalEventDispatcher.GameTimeSecondChangeCallback.AddListener(RotateSun);
                    break;
                case GameTimeService.GameTimeInterval.EveryGameMinute:
                    GlobalEventDispatcher.GameTimeMinuteChangeCallback.AddListener(RotateSun);
                    break;
                case GameTimeService.GameTimeInterval.EveryGameHour:
                    GlobalEventDispatcher.GameTimeHourChangeCallback.AddListener(RotateSun);
                    break;
            }

            _stateList.AddRange(new[]
            {
                CreatePresetState(new SkyState(), state => state.PresetDay1()),
                CreatePresetState(new SkyState(), state => state.PresetDay2()),
                CreatePresetState(new SkyState(), state => state.PresetEvening()),
                CreatePresetState(new SkyState(), state => state.PresetNight0()),
                CreatePresetState(new SkyState(), state => state.PresetNight1()),
                CreatePresetState(new SkyState(), state => state.PresetNight2()),
                CreatePresetState(new SkyState(), state => state.PresetDawn()),
                CreatePresetState(new SkyState(), state => state.PresetDay0())
            });


            // Indoor BSP worlds (Old Mine, Free Mine, Orc Temple, mod caves): the engine's indoor sky controller has no
            // sun and no fog there - our sun lit the mines like the day.
            RenderSettings.fog = !_isIndoorWorld;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = AmbientMode.Flat;
            InitRainState();

            Interpolate(new DateTime());
        }

        private void UpdateStateTexAndFog()
        {
            var currentDay = _gameTimeService.GetDay();
            var day = currentDay + 1;

            float[] colorValues;

            try
            {
                // hacky way to use the proper color for the current day until animTex is implemented
                // % 2 is used as there are only 2 textures for the sky, consistent between G1 and G2
                colorValues = _configService.Gothic.IniSkyDayColor(day % 2).Split(' ').Select(float.Parse).ToArray();
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString(), LogCat.Mesh);
                return;
            }

            var fogColor = new Vector3(colorValues[0], colorValues[1], colorValues[2]);

            foreach (var state in _stateList)
            {
                if (!(state.Time < 0.35) || !(state.Time > 0.65)) // set new textures only for day states 
                {
                    continue;
                }

                state.Layer[0].TEXName = "SKYDAY_LAYER0_A" + day % 2 + ".TGA";

                if (!(state.Time < 0.3) || !(state.Time > 0.7))
                {
                    continue;
                }

                state.Layer[1].TEXName = "SKYDAY_LAYER1_A" + day % 2 + ".TGA";
                state.FogColor = fogColor;
                state.DomeColor0 = fogColor;
            }
        }

        private void Interpolate(DateTime _)
        {
            _masterTime = _gameTimeService.GetSkyTime(); // Current time

            var (previousIndex, currentIndex) = FindNextStateIndex();

            var lastState = _stateList[previousIndex];
            var currentState = _stateList[currentIndex];
            _isRaining = _masterTime > _rainState.Time && _masterTime < _rainState.EndTime;

            UpdateStateTexAndFog();

            if (_isRaining)
            {
                UpdateRain(_rainState.Time);
                InterpolateSky(currentState, _rainState, _rainState.Time, _rainState.LerpDuration);
                return;
            }

            if (_masterTime > _rainState.Time &&
                _masterTime < _rainState.EndTime + _rainState.LerpDuration) // when rain is ending
            {
                UpdateRain(_rainState.EndTime);
                InterpolateSky(_rainState, currentState, _rainState.EndTime, _rainState.LerpDuration);
                return;
            }

            UpdateRain(_rainState.Time);
            InterpolateSky(lastState, currentState, currentState.Time, currentState.LerpDuration);
        }

        private void InterpolateSky(SkyState lastState, SkyState newState, float startTime, float lerpDuration = 0.05f)
        {
            // Calculate how far we are between the two ticks (0.0 to 1.0)
            var lerpFraction = Mathf.Clamp01((_masterTime - startTime) / lerpDuration);

            if (lerpFraction >= 1 && _noSky == false)
            {
                return; // finished blending
            }

            var oldPolyColor = lastState.PolyColor.ToUnityColor(255) / 255f;
            var newPolyColor = newState.PolyColor.ToUnityColor(255) / 255f;

            var oldDomeColor = lastState.DomeColor0.ToUnityColor(255) / 255f;
            var newDomeColor = newState.DomeColor0.ToUnityColor(255) / 255f;

            var oldFogColor = lastState.FogColor.ToUnityColor(255) / 255f;
            var newFogColor = newState.FogColor.ToUnityColor(255) / 255f;

            var polyColor = Color.Lerp(oldPolyColor, newPolyColor, lerpFraction);
            // Indoor worlds don't follow the sky: hands keep day light, fog stays dark (a bright day fog lit caves up).
            RenderSettings.ambientLight = _isIndoorWorld ? Color.white : polyColor;
            SetDayLight(polyColor);
            RenderSettings.fogColor = _isIndoorWorld
                ? Color.black
                : Color.Lerp(oldFogColor, newFogColor, lerpFraction);

            // Old sky layer 1.
            if (!string.IsNullOrEmpty(lastState.Layer[0].TEXName))
            {
                RenderSettings.skybox.SetTexture(_skyTex1ShaderId,
                    _textureCacheService.TryGetTexture(lastState.Layer[0].TEXName));
            }

            RenderSettings.skybox.SetVector(_skyMovement1ShaderId, lastState.Layer[0].TEXSpeed);
            RenderSettings.skybox.SetFloat(_sky1OpacityShaderId, lastState.Layer[0].TEXAlpha / 255f);

            // Old sky layer 2.
            if (!string.IsNullOrEmpty(lastState.Layer[1].TEXName))
            {
                RenderSettings.skybox.SetTexture(_skyTex2ShaderId,
                    _textureCacheService.TryGetTexture(lastState.Layer[1].TEXName));
            }

            RenderSettings.skybox.SetVector(_skyMovement2ShaderId, lastState.Layer[1].TEXSpeed);
            RenderSettings.skybox.SetFloat(_sky2OpacityShaderId, lastState.Layer[1].TEXAlpha / 255f);

            // New sky layer 1.
            if (!string.IsNullOrEmpty(newState.Layer[0].TEXName))
            {
                RenderSettings.skybox.SetTexture(_skyTex3ShaderId,
                    _textureCacheService.TryGetTexture(newState.Layer[0].TEXName));
            }

            RenderSettings.skybox.SetVector(_skyMovement3ShaderId, newState.Layer[0].TEXSpeed);
            RenderSettings.skybox.SetFloat(_sky3OpacityShaderId, newState.Layer[0].TEXAlpha / 255f);

            // New sky layer 2.
            if (!string.IsNullOrEmpty(newState.Layer[1].TEXName))
            {
                RenderSettings.skybox.SetTexture(_skyTex4ShaderId,
                    _textureCacheService.TryGetTexture(newState.Layer[1].TEXName));
            }

            RenderSettings.skybox.SetVector(_skyMovement4ShaderId, newState.Layer[1].TEXSpeed);
            RenderSettings.skybox.SetFloat(_sky4OpacityShaderId, newState.Layer[1].TEXAlpha / 255f);

            // Fog and dome color.
            RenderSettings.skybox.SetColor(_fogColor1ShaderId, oldFogColor);
            RenderSettings.skybox.SetColor(_fogColor2ShaderId, newFogColor);
            RenderSettings.skybox.SetColor(_domeColor1ShaderId, oldDomeColor);
            RenderSettings.skybox.SetColor(_domeColor2ShaderId, newDomeColor);

            RenderSettings.skybox.SetFloat(_layersBlendShaderId, lerpFraction);
            SetShaderProperties();
        }

        private void InitRainState()
        {
            // values taken from the original game
            _rainState.Time = 0.187f; // 16:30
            _rainState.EndTime = 0.229f; // 17:30
            _rainState.LerpDuration = 0.01f;
            _rainState.PolyColor = new Vector3(255.0f, 250.0f, 235.0f);
            _rainState.FogColor = new Vector3(72.0f, 72.0f, 72.0f);
            _rainState.DomeColor0 = new Vector3(72.0f, 72.0f, 72.0f);
            _rainState.Layer[0].TEXName = "SKYRAINCLOUDS.TGA";
            _rainState.Layer[0].TEXAlpha = 255.0f;

            if (_rainWindow is { } rainWindow)
            {
                _rainState.Time = rainWindow.Start;
                _rainState.EndTime = rainWindow.End;
            }
        }

        /// <summary>
        /// Gothic's sky polyColor (bright by day, dark blue at night) scales the outdoor world's baked light and the
        /// sun/ambient of VOBs and NPCs. Fires and torches (stationary lights) are added after it and stay bright.
        /// </summary>
        private void SetDayLight(Color polyColor)
        {
            _polyColor = polyColor;
            // Indoor worlds (e.g. G1's Old Mine, Orc Temple) have no day/night - like in Gothic.
            // Indoors no sun - the engine's indoor sky lit the mines like the night.
            var nightFactor = Mathf.Lerp(_nightLightFactor, 1f,
                Mathf.InverseLerp(_nightBrightness, _duskBrightness, polyColor.grayscale));
            var rainFactor = Mathf.Lerp(1f, _rainLightFactor, _rainWeightAndVolume);
            var dayLight = _isIndoorWorld ? _indoorLight : polyColor * (nightFactor * rainFactor);
            dayLight.a = 1f;
            // Outdoor lamps and fires dim by day.
            _stationaryLightsService.SetDaylight(dayLight.grayscale);

            Shader.SetGlobalColor(_dayLightColorShaderId, dayLight);
            Shader.SetGlobalColor(_sunColorShaderId,
                _isIndoorWorld ? Color.black : _configService.Dev.SunLightColor * dayLight);
            Shader.SetGlobalColor(_ambientShaderId, _configService.Dev.AmbientLightColor * dayLight);
            if (_sunLight != null)
                _sunLight.color = _configService.Dev.SunLightColor * dayLight;
        }

        private void SetShaderProperties()
        {
            Shader.SetGlobalVector(_sunDirectionShaderId, _sunDirection);
            // InterpolateSky calls this right after SetDayLight - keep its sky color (white overwrote the night).
            SetDayLight(_polyColor);
            Shader.SetGlobalFloat(_pointLightIntensityShaderId, _configService.Dev.SunLightIntensity);

            // TODO - Could be moved to another Service as Sky handling for dive color is not 100% true ;-)
            // TODO - We can also outsource this value to an Ini file if gamers want to have more control over its color.
            var underwaterColor = new Color(0.15f, 0.23f, 0.7f);
            Shader.SetGlobalVector(_underwaterColorShaderId,
                new Vector3(underwaterColor.r, underwaterColor.g, underwaterColor.b));
        }

        private void InitRainGo()
        {
            // by default rainPFX is disabled so we need to find the parent and activate it
            // Rain.prefab follows the player's head (RainFollower).
            var rainParticlesGameObject = GameObject.Find("Rain").FindChildRecursively("RainParticles");
            rainParticlesGameObject.SetActive(true);
            _rainParticleSystem = rainParticlesGameObject.GetComponent<ParticleSystem>();
            _rainParticleSystem.Stop();

            _rainParticleSound = rainParticlesGameObject.GetComponentInChildren<AudioSource>();
            _rainParticleSound.clip = _audioService.CreateAudioClip("RAIN_01");
            _rainParticleSound.volume = 0;
            _rainParticleSound.Stop();
        }

        private void UpdateRainTime(DateTime _)
        {
            if (_masterTime > 0.02f || // This function is called every hour but is run only once a day at 12:00 pm
                _gameTimeService.GetDay() == 1) // Dont update if it is the first day
            {
                return;
            }

            _rainState.Time = Random.Range(0f, 1f);

            if (0.96f < _rainState.Time)
            {
                _rainState.Time = 0.96f;
            }

            _rainState.EndTime = Random.Range(0f, 0.06f) + 0.04f + _rainState.Time;

            if (1.0f < _rainState.EndTime)
            {
                _rainState.EndTime = 1.0f;
            }

            _rainWindow = (_rainState.Time, _rainState.EndTime);
        }

        private void UpdateRain(float stateTime, float lerpDuration = 0.01f)
        {
            if (_rainParticleSound == null || _rainParticleSystem == null)
            {
                return;
            }

            if (_masterTime < _rainState.Time ||
                _masterTime > _rainState.EndTime + _rainState.LerpDuration) // is not raining nor after rain
            {
                _rainParticleSound.volume = 0;
                _rainParticleSound.Stop();
                _rainParticleSystem.Stop();
                return;
            }

            var lerpFraction = (_masterTime - stateTime) / lerpDuration;

            lerpFraction = Mathf.Clamp01(lerpFraction);

            if (_isRaining)
            {
                _rainWeightAndVolume = lerpFraction;
            }
            else if (_masterTime > _rainState.Time && _masterTime < _rainState.EndTime + _rainState.LerpDuration)
            {
                _rainWeightAndVolume = 1 - lerpFraction;
            }

            _rainParticleSound.volume = _rainWeightAndVolume;

            var module = _rainParticleSystem.emission;
            module.rateOverTime = new ParticleSystem.MinMaxCurve(_maxParticleCount * _rainWeightAndVolume);

            if (!_rainParticleSound.isPlaying && _configService.Dev.EnableGameSounds)
            {
                _rainParticleSound.Play();
            }

            if (!_rainParticleSystem.isPlaying)
            {
                _rainParticleSystem.Play();
            }
        }

        /// <summary>
        /// Find the previous and next state indices based on the current master time.
        /// </summary>
        private (int previousIndex, int nextIndex) FindNextStateIndex()
        {
            var nextIndex = _stateList.FindLastIndex(x => x.Time < _masterTime);

            if (nextIndex == -1)
            {
                nextIndex = 0;
            }

            var previousIndex = nextIndex - 1;
            if (previousIndex < 0)
            {
                previousIndex = _stateList.Count - 1;
            }

            return (previousIndex, nextIndex);
        }

        private SkyState CreatePresetState(SkyState skyState, Action<SkyState> applyPreset)
        {
            applyPreset(skyState);
            return skyState;
        }

        /// <summary>
        /// Based on performance settings, the sun direction is changed more or less frequent.
        ///
        /// Unity rotation settings:
        /// 270° = midnight (no light)
        /// 90° = noon (full light)
        ///
        /// Calculation: 270f is the starting midnight value
        /// Calculation: One full DateTime == 360°. --> e.g. 15° * 24h + 0min + 0sec == 360°
        /// </summary>
        /// <summary>
        /// The sun rises at 6:00, stands highest at 12:00 and sets at 18:00 (one turn per day); at night the moon takes
        /// the opposite place on the same path. _SunDirection was these angles themselves (not a direction) - the
        /// shaders' N·L was a switch on the normal's x then.
        /// </summary>
        private void RotateSun(DateTime time)
        {
            var hours = time.Hour + time.Minute / 60f + time.Second / 3600f;
            var sunDirection = GetSunPathDirection(270f + 15f * hours);
            var isDay = sunDirection.y < 0f;
            _sunDirection = isDay ? sunDirection : -sunDirection;

            var elevation = Mathf.Asin(Mathf.Clamp(-_sunDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
            _sunShadowFade = Mathf.Clamp01(elevation / _sunHorizonFadeDegrees);

            UpdateSunLight(isDay);
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == Constants.IniNames.Shadows)
                ApplyShadowOption();
        }

        /// <summary>
        /// Option "Shadows": off, hard (cheapest), medium or soft (wider filter, default) - the sun's/moon's shadow.
        /// </summary>
        private void ApplyShadowOption()
        {
            if (_sunLight == null)
                return;

            var option = _configService.Gothic.GetInt(Constants.IniNames.Shadows, Constants.IniNames.ShadowsDefaultValue);
            _sunLight.shadows = option switch
            {
                0 => LightShadows.None,
                1 => LightShadows.Hard,
                _ => LightShadows.Soft
            };
            var lightData = _sunLight.GetUniversalAdditionalLightData();
            lightData.softShadowQuality = option == 2 ? SoftShadowQuality.Low : SoftShadowQuality.High;
            Logger.Log($"[Sky] Shadows option {option}: sun {_sunLight.shadows}, soft quality " +
                       $"{lightData.softShadowQuality}.", LogCat.Loading);
        }

        /// <summary>
        /// Light direction for the angle on the sun's path (0 = sunrise, 90 = noon, from above), tilted by _sunPathTilt.
        /// </summary>
        private static Vector3 GetSunPathDirection(float pathAngle)
        {
            return Quaternion.AngleAxis(_sunPathTilt, Vector3.forward) * Quaternion.Euler(pathAngle, 0f, 0f) *
                   Vector3.forward;
        }

        /// <summary>
        /// The "Sun": a directional light along _sunDirection, URP's main light. Its shadow map gives the dynamic
        /// shadows; its strength fades out at the horizon and is weaker under the moon. Indoor worlds have none.
        /// The URP asset decides whether shadows are drawn at all (PC: on, Quest: off).
        /// </summary>
        private void UpdateSunLight(bool isDay)
        {
            if (_sunLight == null)
            {
                var sunGo = new GameObject("Sun");
                _sunLight = sunGo.AddComponent<Light>();
                _sunLight.type = LightType.Directional;
                _sunLight.shadows = LightShadows.Soft;
                _sunLight.intensity = 1f;
                RenderSettings.sun = _sunLight;
                Logger.Log("[Sky] Sun light created - dynamic sun/moon shadows where the quality level has them.",
                    LogCat.Loading);
                ApplyShadowOption();
            }

            _sunLight.enabled = !_isIndoorWorld;
            _sunLight.transform.rotation = Quaternion.LookRotation(_sunDirection);
            Shader.SetGlobalVector(_sunDirectionShaderId, _sunDirection);
            _sunLight.shadowStrength = (isDay ? _sunShadowStrength : _moonShadowStrength) * _sunShadowFade *
                                       Mathf.Lerp(1f, _rainShadowFactor, _rainWeightAndVolume);
            _sunLight.color = _configService.Dev.SunLightColor * _polyColor;
        }
    }
}
