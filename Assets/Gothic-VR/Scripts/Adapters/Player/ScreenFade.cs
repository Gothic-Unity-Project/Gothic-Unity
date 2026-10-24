using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Fades the VR camera to a color (black). Sits on the camera and draws the inner faces of a small cube around
    /// the head with an on-top material, so it covers the world (and walls the head is in), but not the on-top UI.
    ///
    /// Two independent fades are combined, the stronger one wins:
    /// - Fade(): requested by game code via IVrRig.Fade (diving darkness by air, VRSeat, transitions).
    /// - SetWallFade(): driven every frame by WallFade (head inside walls).
    /// Fades run on unscaled time: they also work while the game is paused (menu, cinema).
    /// </summary>
    public class ScreenFade : MonoBehaviour
    {
        private static readonly int _colorId = Shader.PropertyToID("_Color");

        [Tooltip("Material with the Gothic/VR Screen Fade shader. A copy is used at runtime.")]
        [SerializeField] private Material _material;

        [Tooltip("Color faded to. Its alpha is ignored.")]
        [SerializeField] private Color _color = Color.black;

        [Tooltip("Edge length in m of the cube around the head. Both eyes plus their near clip plane must fit in.")]
        [SerializeField] private float _size = 0.3f;

        [Tooltip("Layer of the fade cube. Has to be rendered by the camera, also in the cinema (UI only).")]
        [SerializeField] private int _layer = 5;

        private Material _materialInstance;
        private MeshRenderer _renderer;
        private float _alpha;
        private float _targetAlpha;
        private float _speed = float.MaxValue;
        private float _wallAlpha;
        private float _wallTargetAlpha;
        private float _wallSpeed = float.MaxValue;
        private float _appliedAlpha = -1f;

        /// <summary>
        /// Current alpha of the requested fade (Fade()), without the wall fade. 0 = clear, 1 = color.
        /// </summary>
        public float FadeAlpha => _alpha;

        /// <summary>
        /// Alpha that is visible right now (requested and wall fade combined).
        /// </summary>
        public float VisibleAlpha => Mathf.Max(_alpha, _wallAlpha);

        private void Awake()
        {
            CreateCube();
            Apply();
        }

        private void OnDestroy()
        {
            if (_materialInstance != null)
                Destroy(_materialInstance);
        }

        /// <summary>
        /// Fades towards alpha (0 = clear, 1 = color) with speed alpha per second. float.MaxValue = immediately.
        /// </summary>
        public void Fade(float alpha, float speed)
        {
            _targetAlpha = Mathf.Clamp01(alpha);
            _speed = speed;

            if (speed >= float.MaxValue)
            {
                _alpha = _targetAlpha;
                Apply();
            }
        }

        /// <summary>
        /// Target of the wall fade (head inside geometry), set by WallFade every frame.
        /// </summary>
        public void SetWallFade(float alpha, float speed)
        {
            _wallTargetAlpha = Mathf.Clamp01(alpha);
            _wallSpeed = speed;
        }

        private void LateUpdate()
        {
            var deltaTime = Time.unscaledDeltaTime;
            _alpha = Step(_alpha, _targetAlpha, _speed, deltaTime);
            _wallAlpha = Step(_wallAlpha, _wallTargetAlpha, _wallSpeed, deltaTime);
            Apply();
        }

        private static float Step(float current, float target, float speed, float deltaTime)
        {
            if (speed >= float.MaxValue)
                return target;
            if (speed <= 0f)
                return current;

            return Mathf.MoveTowards(current, target, speed * deltaTime);
        }

        private void Apply()
        {
            var alpha = VisibleAlpha;
            if (Mathf.Approximately(alpha, _appliedAlpha) || _renderer == null)
                return;

            _appliedAlpha = alpha;
            _renderer.enabled = alpha > 0f;

            var color = _color;
            color.a = alpha;
            _materialInstance.SetColor(_colorId, color);
        }

        private void CreateCube()
        {
            if (_material == null)
            {
                Logger.LogError($"[ScreenFade] No material assigned on {name}. The screen can't be faded.", LogCat.VR);
                return;
            }

            var cube = new GameObject("ScreenFadeCube")
            {
                layer = _layer
            };
            cube.transform.SetParent(transform, false);
            cube.transform.localScale = Vector3.one * _size;

            cube.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            _materialInstance = new Material(_material);
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
