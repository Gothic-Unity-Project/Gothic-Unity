using System.Collections;
using Gothic.Core;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// What the VR player sees while the hero is knocked out (GlobalEventDispatcher.HeroKnockedOut): a heavy grey veil
    /// that clears while the time runs - it's the timer - and a slow heartbeat in both controllers. The VR hero body
    /// (VRHeroBody, EnableHeroFallCamera) lowers the camera with the falling body. Without it, the view fades out and
    /// comes back at ground level, and once the hero gets up (HeroRecovered) fades out and back at the normal height.
    /// The view is only lowered, never turned: tilting the world makes players sick.
    /// When the hero dies (HeroDied), he falls the same way and the view closes to black from the edges (the move
    /// vignette, VrMoveVignette); VRPlayer opens the menu. Veil and black hide while the menu is open, so it can be
    /// read and pointed at.
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
        [Tooltip("Seconds the view takes to close to black from the edges after the hero's death.")]
        [SerializeField] private float _deathCloseSeconds = 3f;
        [Tooltip("Camera height in m above the floor while lying - only without the VR hero body.")]
        [SerializeField] private float _lyingEyeHeight = 0.3f;
        [Tooltip("Speed of the fades to black and back, in alpha per second.")]
        [SerializeField] private float _fadeSpeed = 3f;
        [Tooltip("Seconds between two heartbeats.")]
        [SerializeField] private float _heartbeatInterval = 1.2f;
        [Tooltip("Strength of a heartbeat, 0..1 of the Contact vibration.")]
        [SerializeField] private float _heartbeatAmplitude = 0.4f;
        [Tooltip("Edge length in m of the veil cube around the head. Both eyes and their near clip plane fit in.")]
        [SerializeField] private float _size = 0.3f;

        // If the hero's recovery never comes (his state changed by a script), the view gets up on its own.
        private const float _recoveryGraceSeconds = 5f;

        // A rig whose screen fade doesn't work never turns black: the knockout goes on without it.
        private const float _maxFadeSeconds = 2f;

        // Opening the menu stutters for a few frames (building it, its on-top materials) - veil and black stay over it.
        private const int _menuShowFrames = 3;
        private const float _menuShowSeconds = 0.1f;

        private IVrRig _rig;
        private IVrHaptics _haptics;
        private VrMoveVignette _vignette;
        private Material _materialInstance;
        private MeshRenderer _renderer;
        // The knockout or the death, whichever runs.
        private Coroutine _knockout;
        private bool _isRecovered;
        // The view was lowered by us (no VR hero body to do it).
        private bool _isLying;
        private bool _isMenuOpen;
        private Color _currentVeil;
        private Coroutine _menuShow;
        // 0..1 closed to black since the hero's death.
        private float _deathClosure;

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
            _vignette = GetComponent<VrMoveVignette>();

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

        private void OnHeroKnockedOut(NpcContainer hero)
        {
            if (_rig == null || _renderer == null)
                return;

            if (_knockout != null)
                StopCoroutine(_knockout);

            _isRecovered = false;
            _knockout = StartCoroutine(KnockOut(FightService.HeroKnockoutSeconds));
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
            // Raised again for every sub menu.
            if (_isMenuOpen || _menuShow != null)
                return;

            _menuShow = StartCoroutine(ShowMenu());
        }

        private IEnumerator ShowMenu()
        {
            var openedAt = Time.realtimeSinceStartup;
            for (var frame = 0;
                 frame < _menuShowFrames || Time.realtimeSinceStartup - openedAt < _menuShowSeconds;
                 frame++)
                yield return null;

            _menuShow = null;
            _isMenuOpen = true;
            ApplyVeil();
            ApplyDeathClosure();
        }

        private void OnMenuClosed()
        {
            if (_menuShow != null)
            {
                StopCoroutine(_menuShow);
                _menuShow = null;
            }

            _isMenuOpen = false;
            ApplyVeil();
            ApplyDeathClosure();
        }

        /// <summary>
        /// Loading ends a knockout (without HeroRecovered) or a death: back to normal at once, the loading screen
        /// covers it. Also the fade, which can be stopped halfway to black.
        /// </summary>
        private void OnLoadGameStart()
        {
            SetDeathClosure(0f);
            if (_knockout == null)
                return;

            StopCoroutine(_knockout);
            _knockout = null;
            _isLying = false;
            _rig.SetEyeHeightOverride(null);
            SetVeil(0f);
            _rig.Fade(0f, float.MaxValue);
        }

        private IEnumerator KnockOut(float seconds)
        {
            yield return LieDown(_veilStartAlpha);

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

            // The body stands up and takes the camera with it.
            if (!_isLying)
            {
                SetVeil(0f);
                _knockout = null;
                yield break;
            }

            yield return FadeToBlack();

            _rig.SetEyeHeightOverride(null);
            _isLying = false;
            SetVeil(0f);
            yield return null;
            _rig.Fade(0f, _fadeSpeed);
            _knockout = null;
        }

        /// <summary>
        /// Dead: down to the ground (unless already lying from a knockout), the knockout's grey veil goes and the view
        /// closes to black from the edges. It stays black until a game is loaded.
        /// </summary>
        private IEnumerator Die()
        {
            SetVeil(0f);
            if (!_isLying)
                yield return LieDown(0f);

            for (var time = 0f; time < _deathCloseSeconds; time += Time.unscaledDeltaTime)
            {
                SetDeathClosure(time / _deathCloseSeconds);
                yield return null;
            }
            SetDeathClosure(1f);
        }

        private void SetDeathClosure(float closure)
        {
            _deathClosure = closure;
            ApplyDeathClosure();
        }

        /// <summary>
        /// The move vignette closes; a camera without one gets a black veil instead.
        /// </summary>
        private void ApplyDeathClosure()
        {
            var closure = _isMenuOpen ? 0f : _deathClosure;
            if (_vignette != null)
                _vignette.SetClosureOverride(closure > 0f ? closure : null);
            else if (_deathClosure > 0f)
                SetVeil(new Color(0f, 0f, 0f, _deathClosure));
        }

        /// <summary>
        /// The VR hero body lowers the camera itself (VRHeroBody.IsHeroDown, set in its LateUpdate). Without it, the
        /// view goes down behind a fade.
        /// </summary>
        private IEnumerator LieDown(float veilAlpha)
        {
            SetVeil(veilAlpha);
            yield return new WaitForEndOfFrame();
            if (VRHeroBody.IsHeroDown)
                yield break;

            yield return FadeToBlack();

            _rig.SetEyeHeightOverride(_lyingEyeHeight);
            _isLying = true;
            // The rig moves the camera down in its next update.
            yield return null;
            _rig.Fade(0f, _fadeSpeed);
        }

        private IEnumerator FadeToBlack()
        {
            _rig.Fade(1f, _fadeSpeed);
            var timeout = Time.time + _maxFadeSeconds;
            yield return new WaitUntil(() => _rig.FadeAlpha >= 1f || Time.time > timeout);

            if (_rig.FadeAlpha < 1f)
                Logger.LogWarning($"[VrKnockoutView] The view didn't turn black within {_maxFadeSeconds} s - " +
                                  "going on without the fade.", LogCat.VR);
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
                layer = Constants.UILayer
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
