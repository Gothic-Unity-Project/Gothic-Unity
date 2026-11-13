using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Comfort vignette: the edges of the view darken while the player's body is moved without walking - smooth
    /// movement, smooth turning, climbing. Head movement, teleports and snap turns don't count. The Gothic.ini option
    /// moveVignette picks the strength (0 = off).
    /// Sits on the camera like ScreenFade and draws a disc (child in the prefab) in front of the eyes with an on-top
    /// material.
    /// </summary>
    public class VrMoveVignette : MonoBehaviour
    {
        private static readonly int _apertureId = Shader.PropertyToID("_Aperture");
        private static readonly int _featherId = Shader.PropertyToID("_Feather");

        [Tooltip("Material with the Gothic/VR Move Vignette shader. A copy is used at runtime.")]
        [SerializeField] private Material _material;
        [Tooltip("Distance in m of the disc in front of the camera. Far enough that both eyes see it about centered " +
                 "(it's drawn on top of everything anyway).")]
        [SerializeField] private float _distance = 1.5f;
        [Tooltip("Degrees from the view's center the disc covers. Beyond the widest headset's field of view.")]
        [SerializeField] private float _coverAngle = 75f;
        [Tooltip("Disc in front of the eyes (child, on a layer the camera renders, no shadows/probes). Its mesh is " +
                 "built at runtime from distance and cover angle.")]
        [SerializeField] private MeshFilter _discFilter;
        [Tooltip("Renderer of the disc. Gets a copy of the material at runtime.")]
        [SerializeField] private MeshRenderer _discRenderer;
        [Tooltip("Body speed in m/s at which the vignette is closed completely.")]
        [SerializeField] private float _fullMoveSpeed = 3f;
        [Tooltip("Turn speed in degrees per second at which the vignette is closed completely.")]
        [SerializeField] private float _fullTurnSpeed = 90f;
        [Tooltip("How fast the vignette closes and opens again, in strength (0..1) per second.")]
        [SerializeField] private float _fadeSpeed = 4f;
        [Tooltip("Degrees from the view's center that stay clear when closed completely: option light, option strong. " +
                 "Quest 2 shows about 50 degrees to each side.")]
        [SerializeField] private float[] _closedClearAngles = { 35f, 25f };
        [Tooltip("Width of the soft edge in degrees.")]
        [SerializeField] private float _featherAngle = 15f;

        // A bigger step within one frame is a teleport, a bigger turn a snap turn: both are no smooth movement.
        private const float _maxStepPerFrame = 1f;
        private const float _maxTurnPerFrame = 15f;
        private const int _discSegments = 48;
        private const int _discRings = 12;

        [Inject] private readonly ConfigService _configService;
        private IVrRig _rig;
        private Material _materialInstance;
        private MeshRenderer _renderer;
        private int _setting;
        private bool _isSettingRead;
        private float _strength;
        private Vector3 _lastRootPosition;
        private float _lastRootYaw;
        private bool _hasLastRoot;

        private void Awake()
        {
            SetupDisc();
        }

        private void Start()
        {
            _rig = GetComponentInParent<IVrRig>(true);
            if (_rig == null)
                Logger.LogWarning($"[VrMoveVignette] No IVrRig above {name} - no vignette.", LogCat.VR);

            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);
            if (_materialInstance != null)
                Destroy(_materialInstance);
            if (_discFilter != null && _discFilter.sharedMesh != null)
                Destroy(_discFilter.sharedMesh);
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == VRConstants.IniNames.MoveVignette)
                TryReadSetting();
        }

        /// <summary>
        /// The rig exists before Gothic.ini is loaded (game version selection): no vignette until then.
        /// </summary>
        private bool TryReadSetting()
        {
            var gothicIni = _configService?.Gothic;
            if (gothicIni == null)
                return false;

            _setting = Mathf.Clamp(gothicIni.GetInt(VRConstants.IniNames.MoveVignette,
                VRConstants.MoveVignetteDefaultValue), 0, _closedClearAngles.Length);
            _isSettingRead = true;
            return true;
        }

        private void LateUpdate()
        {
            if (_renderer == null || _rig == null || (!_isSettingRead && !TryReadSetting()))
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

            var closedAperture = _closedClearAngles[Mathf.Max(_setting, 1) - 1] / _coverAngle;
            _materialInstance.SetFloat(_apertureId, Mathf.Lerp(1f, closedAperture, _strength));
        }

        private void SetupDisc()
        {
            if (_material == null || _discFilter == null || _discRenderer == null)
            {
                Logger.LogError($"[VrMoveVignette] Material, disc filter or disc renderer not assigned on {name}. " +
                                "No vignette.", LogCat.VR);
                return;
            }

            // The mesh's radius is built for this distance.
            _discFilter.transform.localPosition = new Vector3(0f, 0f, _distance);
            _discFilter.sharedMesh = CreateDiscMesh();

            _materialInstance = new Material(_material);
            _materialInstance.SetFloat(_featherId, _featherAngle / _coverAngle);
            _renderer = _discRenderer;
            _renderer.sharedMaterial = _materialInstance;
            _renderer.enabled = false;
        }

        /// <summary>
        /// Rings around the center, evenly spaced in viewing angle; uv.x is that angle as a share of the cover angle
        /// (0..1), the shader fades with it.
        /// </summary>
        private Mesh CreateDiscMesh()
        {
            var vertices = new Vector3[1 + _discRings * _discSegments];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[_discSegments * 3 + (_discRings - 1) * _discSegments * 6];

            vertices[0] = Vector3.zero;
            uvs[0] = Vector2.zero;
            for (var ring = 0; ring < _discRings; ring++)
            {
                var share = (ring + 1f) / _discRings;
                var radius = _distance * Mathf.Tan(share * _coverAngle * Mathf.Deg2Rad);
                for (var i = 0; i < _discSegments; i++)
                {
                    var angle = i * Mathf.PI * 2f / _discSegments;
                    var index = 1 + ring * _discSegments + i;
                    vertices[index] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                    uvs[index] = new Vector2(share, 0f);
                }
            }

            var t = 0;
            for (var i = 0; i < _discSegments; i++)
            {
                var next = (i + 1) % _discSegments;
                triangles[t++] = 0;
                triangles[t++] = 1 + i;
                triangles[t++] = 1 + next;
            }

            for (var ring = 1; ring < _discRings; ring++)
            {
                var inner = 1 + (ring - 1) * _discSegments;
                var outer = 1 + ring * _discSegments;
                for (var i = 0; i < _discSegments; i++)
                {
                    var next = (i + 1) % _discSegments;
                    triangles[t++] = inner + i;
                    triangles[t++] = outer + i;
                    triangles[t++] = outer + next;
                    triangles[t++] = inner + i;
                    triangles[t++] = outer + next;
                    triangles[t++] = inner + next;
                }
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
