using System.Collections;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
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
    /// When the hero dies (HeroDied), he falls the same way and the veil darkens until the menu opens (VRPlayer).
    /// The veil hides while the menu is open, so it can be read.
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
        [Tooltip("Color of the veil once the hero is dead. Its alpha is ignored.")]
        [SerializeField] private Color _deathVeilColor = new(0.05f, 0.05f, 0.05f);
        [Tooltip("Opacity of the veil once the hero is dead.")]
        [SerializeField] private float _deathVeilAlpha = 0.9f;
        [Tooltip("Seconds the veil takes to darken after the hero's death.")]
        [SerializeField] private float _deathDarkenSeconds = 4f;
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
        // The knockout or the death, whichever runs.
        private Coroutine _knockout;
        private bool _isRecovered;
        private bool _isLying;
        private bool _isMenuOpen;
        private Color _currentVeil;

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
            GlobalEventDispatcher.HeroDied.AddListener(OnHeroDied);
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);
            GlobalEventDispatcher.MenuOpened.AddListener(OnMenuOpened);
            GlobalEventDispatcher.MenuClosed.AddListener(OnMenuClosed);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.HeroKnockedOut.RemoveListener(OnHeroKnockedOut);
            GlobalEventDispatcher.HeroRecovered.RemoveListener(OnHeroRecovered);
            GlobalEventDispatcher.HeroDied.RemoveListener(OnHeroDied);
            GlobalEventDispatcher.LoadGameStart.RemoveListener(OnLoadGameStart);
            GlobalEventDispatcher.MenuOpened.RemoveListener(OnMenuOpened);
            GlobalEventDispatcher.MenuClosed.RemoveListener(OnMenuClosed);
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

        private void OnHeroDied(NpcContainer killer)
        {
            if (_rig == null || _renderer == null)
                return;

            if (_knockout != null)
                StopCoroutine(_knockout);

            _knockout = StartCoroutine(Die());
        }

        private void OnMenuOpened(GameObject menu)
        {
            _isMenuOpen = true;
            ApplyVeil();
        }

        private void OnMenuClosed()
        {
            _isMenuOpen = false;
            ApplyVeil();
        }

        /// <summary>
        /// Loading ends a knockout (without HeroRecovered) or a death: back to normal at once, the loading screen covers
        /// it.
        /// </summary>
        private void OnLoadGameStart()
        {
            if (_knockout == null)
                return;

            StopCoroutine(_knockout);
            _knockout = null;
            _isLying = false;
            _rig.SetEyeHeightOverride(null);
            SetVeil(0f);
        }

        private IEnumerator KnockOut(float seconds)
        {
            yield return LieDown();

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
            _isLying = false;
            SetVeil(0f);
            yield return null;
            _rig.Fade(0f, _fadeSpeed);
            _knockout = null;
        }

        /// <summary>
        /// Dead: down to the ground (unless already lying from a knockout), and the veil darkens. It stays until a game
        /// is loaded.
        /// </summary>
        private IEnumerator Die()
        {
            if (!_isLying)
                yield return LieDown();

            var from = _currentVeil;
            var to = _deathVeilColor;
            to.a = _deathVeilAlpha;
            for (var time = 0f; time < _deathDarkenSeconds; time += Time.unscaledDeltaTime)
            {
                SetVeil(Color.Lerp(from, to, time / _deathDarkenSeconds));
                yield return null;
            }
            SetVeil(to);
        }

        private IEnumerator LieDown()
        {
            _rig.Fade(1f, _fadeSpeed);
            yield return new WaitUntil(() => _rig.FadeAlpha >= 1f);

            _rig.SetEyeHeightOverride(_lyingEyeHeight);
            _isLying = true;
            SetVeil(_veilStartAlpha);
            // The rig moves the camera down in its next update.
            yield return null;
            _rig.Fade(0f, _fadeSpeed);
        }

        private void SetVeil(float alpha)
        {
            var color = _veilColor;
            color.a = alpha;
            SetVeil(color);
        }

        private void SetVeil(Color color)
        {
            _currentVeil = color;
            ApplyVeil();
        }

        private void ApplyVeil()
        {
            if (_renderer == null)
                return;

            _renderer.enabled = _currentVeil.a > 0f && !_isMenuOpen;
            _materialInstance.SetColor(_colorId, _currentVeil);
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
