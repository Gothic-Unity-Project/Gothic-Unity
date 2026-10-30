using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Comfort vignette: the edges of the view darken while the player's body is moved without walking - smooth
    /// movement, smooth turning, climbing. Head movement, teleports and snap turns don't count. The Gothic.ini option
    /// moveVignette picks the strength (0 = off).
    /// Sits on the camera like ScreenFade and draws a disc in front of the eyes with an on-top material.
    /// </summary>
    public class VrMoveVignette : MonoBehaviour
    {
        private static readonly int _apertureId = Shader.PropertyToID("_Aperture");
        private static readonly int _featherId = Shader.PropertyToID("_Feather");

        [Tooltip("Material with the Gothic/VR Move Vignette shader. A copy is used at runtime.")]
        [SerializeField] private Material _material;
        [Tooltip("Distance in m of the disc in front of the camera. Has to be beyond the near clip plane.")]
        [SerializeField] private float _distance = 0.1f;
        [Tooltip("Radius in m of the disc. Has to cover the view of both eyes at the distance above.")]
        [SerializeField] private float _radius = 0.4f;
        [Tooltip("Layer of the disc. Has to be rendered by the camera.")]
        [SerializeField] private int _layer = 5;
        [Tooltip("Body speed in m/s at which the vignette is closed completely.")]
        [SerializeField] private float _fullMoveSpeed = 3f;
        [Tooltip("Turn speed in degrees per second at which the vignette is closed completely.")]
        [SerializeField] private float _fullTurnSpeed = 90f;
        [Tooltip("How fast the vignette closes and opens again, in strength (0..1) per second.")]
        [SerializeField] private float _fadeSpeed = 4f;
        [Tooltip("Clear part of the disc (0..1 of its radius) when closed completely: option light, option strong.")]
        [SerializeField] private float[] _closedApertures = { 0.7f, 0.45f };
        [Tooltip("Width of the soft edge, 0..1 of the disc radius.")]
        [SerializeField] private float _feather = 0.25f;

        // A bigger step within one frame is a teleport, a bigger turn a snap turn: both are no smooth movement.
        private const float _maxStepPerFrame = 1f;
        private const float _maxTurnPerFrame = 15f;
        private const int _discSegments = 48;

        private ConfigService _configService;
        private IVrRig _rig;
        private Material _materialInstance;
        private MeshRenderer _renderer;
        private int _setting;
        private float _strength;
        private Vector3 _lastRootPosition;
        private float _lastRootYaw;
        private bool _hasLastRoot;

        private void Awake()
        {
            CreateDisc();
        }

        private void Start()
        {
            _configService = ReflexProjectInstaller.DIContainer.Resolve<ConfigService>();
            _rig = GetComponentInParent<IVrRig>(true);
            if (_rig == null)
                Logger.LogWarning($"[VrMoveVignette] No IVrRig above {name} - no vignette.", LogCat.VR);

            ReadSetting();
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);
            if (_materialInstance != null)
                Destroy(_materialInstance);
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == VRConstants.IniNames.MoveVignette)
                ReadSetting();
        }

        private void ReadSetting()
        {
            _setting = Mathf.Clamp(_configService.Gothic.GetInt(VRConstants.IniNames.MoveVignette,
                VRConstants.MoveVignetteDefaultValue), 0, _closedApertures.Length);
        }

        private void LateUpdate()
        {
            if (_renderer == null || _rig == null)
                return;

            var root = _rig.Root;
            var position = root.position;
            var yaw = root.eulerAngles.y;
            var target = 0f;

            if (_hasLastRoot && Time.deltaTime > 0f)
            {
                var step = position - _lastRootPosition;
                step.y = 0f;
                var turn = Mathf.Abs(Mathf.DeltaAngle(_lastRootYaw, yaw));

                var moveStrength = step.magnitude > _maxStepPerFrame
                    ? 0f
                    : step.magnitude / Time.deltaTime / _fullMoveSpeed;
                var turnStrength = turn > _maxTurnPerFrame ? 0f : turn / Time.deltaTime / _fullTurnSpeed;
                target = Mathf.Clamp01(Mathf.Max(moveStrength, turnStrength));
            }

            _lastRootPosition = position;
            _lastRootYaw = yaw;
            _hasLastRoot = true;

            if (_setting == 0)
                target = 0f;

            _strength = Mathf.MoveTowards(_strength, target, _fadeSpeed * Time.deltaTime);
            _renderer.enabled = _strength > 0.01f;
            if (!_renderer.enabled)
                return;

            var closedAperture = _closedApertures[Mathf.Max(_setting, 1) - 1];
            _materialInstance.SetFloat(_apertureId, Mathf.Lerp(1f, closedAperture, _strength));
        }

        private void CreateDisc()
        {
            if (_material == null)
            {
                Logger.LogError($"[VrMoveVignette] No material assigned on {name}. No vignette.", LogCat.VR);
                return;
            }

            var disc = new GameObject("MoveVignetteDisc")
            {
                layer = _layer
            };
            disc.transform.SetParent(transform, false);
            disc.transform.localPosition = new Vector3(0f, 0f, _distance);

            disc.AddComponent<MeshFilter>().sharedMesh = CreateDiscMesh();

            _materialInstance = new Material(_material);
            _materialInstance.SetFloat(_featherId, _feather);
            _renderer = disc.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _materialInstance;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.allowOcclusionWhenDynamic = false;
            _renderer.enabled = false;
        }

        /// <summary>
        /// A fan around the center; uv.x is the distance from the center (0..1), the shader fades with it.
        /// </summary>
        private Mesh CreateDiscMesh()
        {
            var vertices = new Vector3[_discSegments + 1];
            var uvs = new Vector2[_discSegments + 1];
            var triangles = new int[_discSegments * 3];

            vertices[0] = Vector3.zero;
            uvs[0] = Vector2.zero;
            for (var i = 0; i < _discSegments; i++)
            {
                var angle = i * Mathf.PI * 2f / _discSegments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * _radius;
                uvs[i + 1] = new Vector2(1f, 0f);

                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 1 == _discSegments ? 1 : i + 2;
            }

            var mesh = new Mesh
            {
                name = "MoveVignetteDisc",
                vertices = vertices,
                uv = uvs,
                triangles = triangles
            };
            // Always in front of the camera: never culled.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

            return mesh;
        }
    }
}
