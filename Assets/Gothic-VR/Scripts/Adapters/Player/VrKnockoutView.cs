using System.Collections;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// What the VR player sees while the hero is knocked out (GlobalEventDispatcher.HeroKnockedOut): the view fades
    /// out, comes back at ground level under a heavy grey veil, and the veil clears while the time runs - it's the
    /// timer. A slow heartbeat pulses in both controllers. Once the hero gets up (HeroRecovered), the view fades out
    /// and comes back at the normal height. The view is only lowered, never turned: tilting the world makes players
    /// sick.
    /// Sits on the camera next to ScreenFade.
    /// </summary>
    public class VrKnockoutView : MonoBehaviour
    {
        private static readonly int _colorId = Shader.PropertyToID("_Color");

        [Tooltip("Material with the Gothic/VR Screen Fade shader. A copy is used, drawn just below the screen fade.")]
        [SerializeField] private Material _material;
        [Tooltip("Color of the veil. Its alpha is ignored.")]
        [SerializeField] private Color _veilColor = new(0.3f, 0.3f, 0.3f);
        [Tooltip("Opacity of the veil right after falling down.")]
        [SerializeField] private float _veilStartAlpha = 0.85f;
        [Tooltip("Opacity of the veil just before getting up.")]
        [SerializeField] private float _veilEndAlpha = 0.15f;
        [Tooltip("Camera height in m above the floor while lying.")]
        [SerializeField] private float _lyingEyeHeight = 0.3f;
        [Tooltip("Speed of the fades to black and back, in alpha per second.")]
        [SerializeField] private float _fadeSpeed = 3f;
        [Tooltip("Seconds between two heartbeats.")]
        [SerializeField] private float _heartbeatInterval = 1.2f;
        [Tooltip("Strength of a heartbeat, 0..1 of the Contact vibration.")]
        [SerializeField] private float _heartbeatAmplitude = 0.4f;
        [Tooltip("Edge length in m of the veil cube around the head. Both eyes and their near clip plane fit in.")]
        [SerializeField] private float _size = 0.3f;
        [Tooltip("Layer of the veil cube. Has to be rendered by the camera.")]
        [SerializeField] private int _layer = 5;

        // If the hero's recovery never comes (his state changed by a script), the view gets up on its own.
        private const float _recoveryGraceSeconds = 5f;

        private IVrRig _rig;
        private IVrHaptics _haptics;
        private Material _materialInstance;
        private MeshRenderer _renderer;
        private Coroutine _knockout;
        private bool _isRecovered;

        private void Awake()
        {
            CreateCube();
        }

        private void Start()
        {
            _rig = GetComponentInParent<IVrRig>(true);
            if (_rig == null)
                Logger.LogWarning($"[VrKnockoutView] No IVrRig above {name} - knockouts aren't shown.", LogCat.VR);

            _haptics = ReflexProjectInstaller.DIContainer.Resolve<IVrHaptics>();

            GlobalEventDispatcher.HeroKnockedOut.AddListener(OnHeroKnockedOut);
            GlobalEventDispatcher.HeroRecovered.AddListener(OnHeroRecovered);
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.HeroKnockedOut.RemoveListener(OnHeroKnockedOut);
            GlobalEventDispatcher.HeroRecovered.RemoveListener(OnHeroRecovered);
            GlobalEventDispatcher.LoadGameStart.RemoveListener(OnLoadGameStart);
            if (_materialInstance != null)
                Destroy(_materialInstance);
        }

        private void OnHeroKnockedOut(float seconds)
        {
            if (_rig == null || _renderer == null)
                return;

            if (_knockout != null)
                StopCoroutine(_knockout);

            _isRecovered = false;
            _knockout = StartCoroutine(KnockOut(seconds));
        }

        private void OnHeroRecovered()
        {
            _isRecovered = true;
        }

        /// <summary>
        /// Loading ends a knockout without HeroRecovered: back to normal at once, the loading screen covers it.
        /// </summary>
        private void OnLoadGameStart()
        {
            if (_knockout == null)
                return;

            StopCoroutine(_knockout);
            _knockout = null;
            _rig.SetEyeHeightOverride(null);
            SetVeil(0f);
        }

        private IEnumerator KnockOut(float seconds)
        {
            _rig.Fade(1f, _fadeSpeed);
            yield return new WaitUntil(() => _rig.FadeAlpha >= 1f);

            _rig.SetEyeHeightOverride(_lyingEyeHeight);
            SetVeil(_veilStartAlpha);
            // The rig moves the camera down in its next update.
            yield return null;
            _rig.Fade(0f, _fadeSpeed);

            var lyingTime = 0f;
            var nextHeartbeat = 0f;
            while (!_isRecovered && lyingTime < seconds + _recoveryGraceSeconds)
            {
                lyingTime += Time.deltaTime;
                SetVeil(Mathf.Lerp(_veilStartAlpha, _veilEndAlpha, seconds > 0f ? lyingTime / seconds : 1f));

                if (lyingTime >= nextHeartbeat)
                {
                    _haptics?.Vibrate(VrHandSide.Left, VrHapticPreset.Contact, _heartbeatAmplitude);
                    _haptics?.Vibrate(VrHandSide.Right, VrHapticPreset.Contact, _heartbeatAmplitude);
                    nextHeartbeat += _heartbeatInterval;
                }

                yield return null;
            }

            _rig.Fade(1f, _fadeSpeed);
            yield return new WaitUntil(() => _rig.FadeAlpha >= 1f);

            _rig.SetEyeHeightOverride(null);
            SetVeil(0f);
            yield return null;
            _rig.Fade(0f, _fadeSpeed);
            _knockout = null;
        }

        private void SetVeil(float alpha)
        {
            _renderer.enabled = alpha > 0f;
            var color = _veilColor;
            color.a = alpha;
            _materialInstance.SetColor(_colorId, color);
        }

        private void CreateCube()
        {
            if (_material == null)
            {
                Logger.LogError($"[VrKnockoutView] No material assigned on {name}. Knockouts aren't shown.", LogCat.VR);
                return;
            }

            var cube = new GameObject("KnockoutVeilCube")
            {
                layer = _layer
            };
            cube.transform.SetParent(transform, false);
            cube.transform.localScale = Vector3.one * _size;

            cube.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            _materialInstance = new Material(_material)
            {
                // Below the screen fade, so its fades to black cover the veil.
                renderQueue = _material.renderQueue - 1
            };
            _renderer = cube.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _materialInstance;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.allowOcclusionWhenDynamic = false;
            _renderer.enabled = false;
        }
    }
}
