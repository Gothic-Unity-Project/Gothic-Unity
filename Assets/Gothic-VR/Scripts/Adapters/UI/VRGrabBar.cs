using System;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Context;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// A bar at a world canvas like SteamVR's windows - reach it with a hand, hold the grip and the target moves (and
    /// turns) with that hand until the grip is released. An inactive child of the canvas in its prefab, outside its rect and ignored by its layout group - Setup turns it on. Optional dock:
    /// while grabbed a sphere floats above a dock anchor (the left hand) - released in it, onDock is called (put the
    /// target back there). Optional head follow: released outside the dock, the target floats in the air at its spot
    /// relative to the head and lazily follows it (turns after it only once the head looked away further, then
    /// glides) - not glued to it. Any VR backend (IVrRig, IVrInput), see SetPorts.
    /// </summary>
    public class VRGrabBar : MonoBehaviour
    {
        private static readonly Color _color = new(1f, 1f, 1f, 0.35f);
        private static readonly Color _hoverColor = new(1f, 1f, 1f, 0.7f);
        private static readonly Color _grabbedColor = new(1f, 0.85f, 0.4f, 0.9f);

        // Thickness relative to the canvas width.
        private const float _barThickness = 0.035f;
        // Meters around the bar in which the hand grabs it.
        private const float _reach = 0.06f;
        private const float _dockRadius = 0.06f;
        private const float _dockHeight = 0.12f;
        private static readonly Color _dockColor = new(1f, 0.85f, 0.4f, 1f);

        // Head follow: the head turns this far (degrees) away before the target turns after it, until it's within
        // the settle angle again. Smoothing per second.
        private const float _followStartAngle = 35f;
        private const float _followSettleAngle = 3f;
        private const float _followSharpness = 5f;

        [Tooltip("The bar's image - grabbed by the hand, not clicked by the UI laser (no raycast target).")]
        [SerializeField]
        private Image _image;

        [Tooltip("Along the canvas' right side instead of under it. Anchors and pivot are the prefab's.")]
        [SerializeField]
        private bool _isVertical;

        [Tooltip("Length relative to the canvas side it runs along.")] [SerializeField] [Range(0f, 1f)]
        private float _length = 0.4f;

        [Inject] private readonly IVrInput _input;
        [Inject] private readonly VrHapticsService _haptics;
        [Inject] private readonly ContextInteractionService _contextInteractionService;

        private IVrRig _rig;
        private Material _dockMaterial;
        private RectTransform _rect;
        private Transform _target;
        private Action _onBeforeGrab;
        private Transform _dockAnchor;
        private Action _onDock;
        private bool _isHeadFollow;
        private GameObject _dockSphere;
        private Transform _grabTransform;
        private VrHandSide _grabSide;
        private Vector3 _grabPositionOffset;
        private Quaternion _grabRotationOffset;

        private bool _isHovered;
        private bool _isFollowing;
        private bool _isFollowTurning;
        private float _followYaw;
        private Vector3 _followPositionOffset;
        private Quaternion _followRotationOffset;

        public bool IsGrabbed => _grabTransform != null;

        private void Awake()
        {
            this.Inject();
        }

        /// <summary>
        /// The current player's rig - looked up again once the player was replaced (restart, a new scene).
        /// </summary>
        private IVrRig GetRig()
        {
            if (_rig is Object rigObject && rigObject != null)
                return _rig;
            // The player can be gone already (a bar outliving its player) - no rig then, not an exception.
            var player = _contextInteractionService.GetCurrentPlayerController();
            _rig = player != null ? player.GetComponent<IVrRig>() : null;
            return _rig;
        }

        /// <summary>
        /// Activates the bar (inactive in its prefab) and sizes it to its canvas (the parent), whose size can come from
        /// a layout (VirtualKeyboard). target: what moves (the canvas itself or e.g. the anchor it follows).
        /// onBeforeGrab: on every grab before the offset to the hand is taken (detach from a parent here).
        /// dockAnchor + onDock: see the class. isHeadFollow: see the class.
        /// </summary>
        public void Setup(Transform target, Action onBeforeGrab = null, Transform dockAnchor = null,
            Action onDock = null, bool isHeadFollow = false)
        {
            _rect = (RectTransform)transform;
            var size = ((RectTransform)_rect.parent).rect.size;
            var thickness = size.x * _barThickness;
            if (_isVertical)
            {
                _rect.sizeDelta = new Vector2(thickness, size.y * _length);
                _rect.anchoredPosition = new Vector2(thickness, 0f);
            }
            else
            {
                _rect.sizeDelta = new Vector2(size.x * _length, thickness);
                _rect.anchoredPosition = new Vector2(0f, -thickness);
            }

            _target = target;
            _onBeforeGrab = onBeforeGrab;
            _dockAnchor = dockAnchor;
            _onDock = onDock;
            _isHeadFollow = isHeadFollow;
            _image.color = _color;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Update, not LateUpdate: a VirtualKeyboard copies its follow target in LateUpdate.
        /// </summary>
        private void Update()
        {
            var rig = GetRig();
            if (rig == null || _input == null)
                return;

            if (_grabTransform != null)
            {
                if (_target == null || !_input.GetButton(_grabSide, VrButton.Grip).IsActive)
                {
                    Release();
                    return;
                }
                _target.SetPositionAndRotation(
                    _grabTransform.position + _grabTransform.rotation * _grabPositionOffset,
                    _grabTransform.rotation * _grabRotationOffset);
                UpdateDockSphere();
                return;
            }

            var isHovered = false;
            // Indexed, not foreach: HandGrabbers is an IReadOnlyList, foreach would box an enumerator every frame.
            var hands = rig.HandGrabbers;
            for (var i = 0; i < hands.Count; i++)
            {
                var hand = hands[i];
                // A hand holding something (sword, rune) doesn't grab the bar.
                if (hand == null || hand.Held != null || !IsInReach(GetHandPosition(hand.HandSide)))
                    continue;
                isHovered = true;
                if (_input.GetButton(hand.HandSide, VrButton.Grip).IsJustActivated)
                {
                    Grab(hand.HandSide);
                    return;
                }
            }
            if (_isHovered != isHovered)
            {
                _isHovered = isHovered;
                _image.color = isHovered ? _hoverColor : _color;
            }

            if (_isFollowing)
                UpdateHeadFollow();
        }

        private Vector3 GetHandPosition(VrHandSide side)
        {
            var visual = _rig.GetHandVisual(side);
            return visual != null ? visual.position : GetController(side).position;
        }

        private Transform GetController(VrHandSide side)
        {
            return side == VrHandSide.Left ? _rig.LeftHand : _rig.RightHand;
        }

        /// <summary>
        /// The tracked controller, not the physics hand - it lags behind and would shake the target.
        /// </summary>
        private void Grab(VrHandSide side)
        {
            _onBeforeGrab?.Invoke();
            _isFollowing = false;
            _grabSide = side;
            _grabTransform = GetController(side);
            _grabRotationOffset = Quaternion.Inverse(_grabTransform.rotation) * _target.rotation;
            _grabPositionOffset = Quaternion.Inverse(_grabTransform.rotation) *
                                  (_target.position - _grabTransform.position);
            _image.color = _grabbedColor;
            if (_dockAnchor != null && _onDock != null && CreateDockSphere())
            {
                _dockSphere.SetActive(true);
                UpdateDockSphere();
            }
            _haptics?.Vibrate(side, VrHapticPreset.Info);
            Logger.Log($"[VRGrabBar] {_target.name} grabbed ({side})", LogCat.Ui);
        }

        private bool IsInReach(Vector3 position)
        {
            var local = _rect.InverseTransformPoint(position);
            var scale = _rect.lossyScale;
            var reachX = _reach / Mathf.Max(scale.x, 0.0001f);
            var reachY = _reach / Mathf.Max(scale.y, 0.0001f);
            var rect = _rect.rect;
            return local.x >= rect.xMin - reachX && local.x <= rect.xMax + reachX &&
                   local.y >= rect.yMin - reachY && local.y <= rect.yMax + reachY &&
                   Mathf.Abs(Vector3.Dot(position - _rect.position, _rect.forward)) <= _reach;
        }

        private void Release()
        {
            var isDocked = _dockSphere != null && _dockSphere.activeSelf && _grabTransform != null && IsInDock();
            Logger.Log($"[VRGrabBar] {(_target != null ? _target.name : "target")} released" +
                       (isDocked ? " in the dock" : ""), LogCat.Ui);
            _grabTransform = null;
            _image.color = _color;
            _isHovered = false;
            if (_dockSphere != null)
                _dockSphere.SetActive(false);
            if (isDocked)
            {
                _onDock();
                return;
            }

            if (_isHeadFollow && _target != null && _rig.Head != null)
                StartHeadFollow();
        }

        /// <summary>
        /// Keeps the target's pose relative to the head's yaw (no pitch/roll: it doesn't swing with nodding).
        /// </summary>
        private void StartHeadFollow()
        {
            var head = _rig.Head;
            _followYaw = head.eulerAngles.y;
            var yaw = Quaternion.Euler(0f, _followYaw, 0f);
            _followPositionOffset = Quaternion.Inverse(yaw) * (_target.position - head.position);
            _followRotationOffset = Quaternion.Inverse(yaw) * _target.rotation;
            _isFollowing = true;
            _isFollowTurning = false;
        }

        private void UpdateHeadFollow()
        {
            var head = _rig.Head;
            if (_target == null || head == null)
            {
                _isFollowing = false;
                return;
            }

            var headYaw = head.eulerAngles.y;
            var angle = Mathf.Abs(Mathf.DeltaAngle(_followYaw, headYaw));
            if (angle > _followStartAngle)
                _isFollowTurning = true;
            else if (angle < _followSettleAngle)
                _isFollowTurning = false;

            var blend = 1f - Mathf.Exp(-_followSharpness * Time.deltaTime);
            if (_isFollowTurning)
                _followYaw = Mathf.LerpAngle(_followYaw, headYaw, blend);

            var yaw = Quaternion.Euler(0f, _followYaw, 0f);
            var position = head.position + yaw * _followPositionOffset;
            var rotation = yaw * _followRotationOffset;
            _target.SetPositionAndRotation(Vector3.Lerp(_target.position, position, blend),
                Quaternion.Slerp(_target.rotation, rotation, blend));
        }

        /// <summary>
        /// Above the anchor in world space - the hand model's own axes differ per hand.
        /// </summary>
        private void UpdateDockSphere()
        {
            if (_dockSphere == null || _dockAnchor == null)
                return;
            _dockSphere.transform.position = _dockAnchor.position + Vector3.up * _dockHeight;
            _dockSphere.transform.localScale = Vector3.one * (_dockRadius * (IsInDock() ? 2.6f : 2f));
        }

        private bool IsInDock()
        {
            return Vector3.Distance(_grabTransform.position, _dockSphere.transform.position) <= _dockRadius * 2f;
        }

        /// <returns>False without the unlit shader (not in the build) - the target then can't be docked.</returns>
        private bool CreateDockSphere()
        {
            if (_dockSphere != null)
                return true;
            if (Constants.ShaderUnlit == null)
            {
                Logger.LogWarning("[VRGrabBar] No unlit shader for the dock sphere - docking unavailable.", LogCat.Ui);
                return false;
            }

            _dockSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _dockSphere.name = "GrabBarDock";
            Destroy(_dockSphere.GetComponent<Collider>());
            _dockMaterial = new Material(Constants.ShaderUnlit);
            _dockMaterial.SetColor("_BaseColor", _dockColor);
            _dockSphere.GetComponent<MeshRenderer>().sharedMaterial = _dockMaterial;
            return true;
        }

        private void OnDestroy()
        {
            if (_dockSphere != null)
                Destroy(_dockSphere);
            if (_dockMaterial != null)
                Destroy(_dockMaterial);
        }

        /// <summary>
        /// Hidden while held (e.g. Marvin mode turned off): let go without docking - reparenting an object
        /// that is being deactivated throws.
        /// </summary>
        private void OnDisable()
        {
            if (_grabTransform == null)
                return;
            _grabTransform = null;
            _image.color = _color;
            _isHovered = false;
            if (_dockSphere != null)
                _dockSphere.SetActive(false);
        }
    }
}
