using System.Collections;
using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Models.Effects;
using Gothic.Core.Services.Caches;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The whole-screen script effects for the VR player (WorldEffectService). A screen blend (DEMENTOR_FX, a spell's
    /// flash) tints the view through a small cube around the head - with a texture (the Dementors' animated
    /// ScreenFX_Dem) drawn over each eye. An earthquake rumbles both controllers while the hero is within its radius:
    /// shaking the view makes VR players sick.
    /// </summary>
    public class VrWorldEffectView : MonoBehaviour
    {
        private const string _materialPath = "VR/Materials/VRScreenBlend";
        private static readonly int _colorId = Shader.PropertyToID("_Color");
        private static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
        // Edge length of the cube around the head (m) - both eyes and their near clip plane fit in.
        private const float _cubeSize = 0.3f;
        private const int _uiLayer = 5;
        // Render queue: below the screen fade (3897 + 1), so its fades to black cover the tint.
        private const int _renderQueue = 3896;
        private const float _rumbleInterval = 0.15f;
        // Rumble strength for an amplitude of 5 cm (FX_EarthQuake) and more.
        private const float _fullRumbleAmplitude = 0.05f;
        private const float _maxRumble = 0.6f;

        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly IVrHaptics _haptics;

        private IVrRig _rig;
        private Material _material;
        private MeshRenderer _renderer;
        private readonly List<ActiveBlend> _blends = new();
        private readonly Dictionary<string, List<Texture2D>> _textures = new();
        private float _rumbleEnd;
        private float _rumbleStrength;
        private Vector3 _rumblePosition;
        private float _rumbleRadius;
        private Coroutine _rumble;

        private class ActiveBlend
        {
            public ScreenBlend Blend;
            public float StartTime;
            public float StopTime = float.MaxValue;
            public List<Texture2D> Frames;
        }

        private void Start()
        {
            _rig = GetComponent<IVrRig>();
            CreateCube();
            GlobalEventDispatcher.ScreenBlendStarted.AddListener(OnScreenBlendStarted);
            GlobalEventDispatcher.ScreenBlendStopped.AddListener(OnScreenBlendStopped);
            GlobalEventDispatcher.EarthquakeStarted.AddListener(OnEarthquakeStarted);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.ScreenBlendStarted.RemoveListener(OnScreenBlendStarted);
            GlobalEventDispatcher.ScreenBlendStopped.RemoveListener(OnScreenBlendStopped);
            GlobalEventDispatcher.EarthquakeStarted.RemoveListener(OnEarthquakeStarted);
            if (_material != null)
                Destroy(_material);
        }

        private void OnScreenBlendStarted(ScreenBlend blend)
        {
            _blends.Add(new ActiveBlend
            {
                Blend = blend,
                StartTime = Time.time,
                StopTime = blend.Seconds > 0f ? Time.time + blend.Seconds : float.MaxValue,
                Frames = GetFrames(blend.TextureName)
            });
        }

        private void OnScreenBlendStopped(ScreenBlend blend)
        {
            foreach (var active in _blends)
            {
                if (active.Blend == blend)
                    active.StopTime = Mathf.Min(active.StopTime, Time.time);
            }
        }

        private void Update()
        {
            if (_renderer == null)
                return;

            _blends.RemoveAll(b => Time.time >= b.StopTime + b.Blend.FadeSeconds);
            if (_blends.Count == 0)
            {
                _renderer.enabled = false;
                return;
            }

            // The newest blend shows - two at once are rare (a spell's flash during a Dementor's talk).
            var active = _blends[^1];
            var blend = active.Blend;
            var alpha = blend.Color.a * GetFade(active);
            var frame = GetFrame(active);
            _material.SetTexture(_mainTexId, frame != null ? frame : Texture2D.whiteTexture);
            // With a texture only the alpha counts (OpenGothic GlobalEffects::scrBlend).
            var color = frame != null ? Color.white : blend.Color;
            color.a = alpha;
            _material.SetColor(_colorId, color);
            _renderer.enabled = alpha > 0.001f;
        }

        private static float GetFade(ActiveBlend active)
        {
            var fade = active.Blend.FadeSeconds;
            if (fade <= 0f)
                return Time.time < active.StopTime ? 1f : 0f;

            var fadeIn = (Time.time - active.StartTime) / fade;
            var fadeOut = (active.StopTime + fade - Time.time) / fade;
            return Mathf.Clamp01(Mathf.Min(fadeIn, fadeOut));
        }

        private static Texture2D GetFrame(ActiveBlend active)
        {
            if (active.Frames == null || active.Frames.Count == 0)
                return null;

            var index = active.Blend.FramesPerSecond > 0f
                ? (int)((Time.time - active.StartTime) * active.Blend.FramesPerSecond) % active.Frames.Count
                : 0;
            return active.Frames[index];
        }

        /// <summary>
        /// The frames of an animated texture: NAME_A0, NAME_A1, ... like Gothic's texture animations.
        /// </summary>
        private List<Texture2D> GetFrames(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
                return null;
            if (_textures.TryGetValue(textureName, out var cached))
                return cached;

            var frames = new List<Texture2D>();
            var baseName = System.IO.Path.GetFileNameWithoutExtension(textureName);
            var frameIndex = baseName.Length - 1;
            var isAnimated = frameIndex > 1 && char.IsDigit(baseName[frameIndex]) &&
                             char.ToUpperInvariant(baseName[frameIndex - 1]) == 'A';
            if (isAnimated)
            {
                var prefix = baseName[..frameIndex];
                for (var i = 0; i < 100; i++)
                {
                    var frame = _textureCacheService.TryGetTexture($"{prefix}{i}");
                    if (frame == null)
                        break;
                    frames.Add(frame);
                }
            }
            else
            {
                var texture = _textureCacheService.TryGetTexture(baseName);
                if (texture != null)
                    frames.Add(texture);
            }

            if (frames.Count == 0)
                Logger.LogWarning($"[VrWorldEffectView] Screen blend texture {textureName} not found.", LogCat.VR);
            _textures[textureName] = frames;
            return frames;
        }

        private void OnEarthquakeStarted(Vector3 position, float radius, float seconds, float amplitude)
        {
            _rumblePosition = position;
            _rumbleRadius = radius;
            _rumbleEnd = Mathf.Max(_rumbleEnd, Time.time + seconds);
            _rumbleStrength = Mathf.Max(_rumbleStrength,
                Mathf.Lerp(0.2f, _maxRumble, Mathf.Clamp01(amplitude / _fullRumbleAmplitude)));
            _rumble ??= StartCoroutine(Rumble());
        }

        private IEnumerator Rumble()
        {
            var wait = new WaitForSeconds(_rumbleInterval);
            while (Time.time < _rumbleEnd)
            {
                var head = _rig?.Head != null ? _rig.Head.position : transform.position;
                if (Vector3.Distance(head, _rumblePosition) <= _rumbleRadius)
                {
                    _haptics?.Vibrate(VrHandSide.Left, VrHapticPreset.Contact, _rumbleStrength);
                    _haptics?.Vibrate(VrHandSide.Right, VrHapticPreset.Contact, _rumbleStrength);
                }
                yield return wait;
            }

            _rumbleStrength = 0f;
            _rumble = null;
        }

        private void CreateCube()
        {
            var baseMaterial = Resources.Load<Material>(_materialPath);
            var head = _rig?.Camera != null ? _rig.Camera.transform : Camera.main?.transform;
            if (baseMaterial == null || head == null)
            {
                Logger.LogError($"[VrWorldEffectView] Material {_materialPath} or camera missing - screen blends " +
                                "aren't shown.", LogCat.VR);
                return;
            }

            var cube = new GameObject("ScreenBlendCube")
            {
                layer = _uiLayer
            };
            cube.transform.SetParent(head, false);
            cube.transform.localScale = Vector3.one * _cubeSize;
            cube.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            _material = new Material(baseMaterial)
            {
                renderQueue = _renderQueue
            };
            _renderer = cube.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.allowOcclusionWhenDynamic = false;
            _renderer.enabled = false;
        }
    }
}
