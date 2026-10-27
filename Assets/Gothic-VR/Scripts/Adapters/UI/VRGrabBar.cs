using System;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using UnityEngine;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// DeveloperConfig.EnableUiGrabBars: a bar at a world canvas like SteamVR's windows - reach it with a hand, hold the
    /// grip and the target moves (and turns) with that hand until the grip is released.
    /// Created at runtime under the canvas, outside its rect and ignored by its layout group.
    /// Optional dock: while grabbed a sphere floats above a dock anchor (the left hand) - released in it, onDock is
    /// called (put the target back there).
    /// Optional head follow: released outside the dock, the target floats in the air at its spot relative to the head
    /// and lazily follows it (turns after it only once the head looked away further, then glides) - not glued to it.
    /// Any VR backend (IVrRig, IVrInput), see SetPorts.
    /// </summary>
    public class VRGrabBar : MonoBehaviour
    {
        private static readonly Color _color = new(1f, 1f, 1f, 0.35f);
        private static readonly Color _hoverColor = new(1f, 1f, 1f, 0.7f);
        private static readonly Color _grabbedColor = new(1f, 0.85f, 0.4f, 0.9f);

        // Bar length relative to the canvas side, thickness relative to the canvas width.
        private const float _barLength = 0.4f;
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

        private static IVrRig _rig;
        private static IVrInput _input;
        private static VrHapticsService _haptics;

        private RectTransform _rect;
        private Transform _target;
        private Action _onBeforeGrab;
        private Transform _dockAnchor;
        private Action _onDock;
        private bool _isHeadFollow;
        private GameObject _dockSphere;
        private Image _image;
        private Transform _grabTransform;
        private VrHandSide _grabSide;
        private Vector3 _grabPositionOffset;
        private Quaternion _grabRotationOffset;

        private bool _isFollowing;
        private bool _isFollowTurning;
        private float _followYaw;
        private Vector3 _followPositionOffset;
        private Quaternion _followRotationOffset;

        public bool IsGrabbed => _grabTransform != null;

        /// <summary>
        /// The player's rig, input and haptics (VRContextInteractionService, once the player exists).
        /// </summary>
        public static void SetPorts(IVrRig rig, IVrInput input, VrHapticsService haptics)
        {
            _rig = rig;
            _input = input;
            _haptics = haptics;
        }

        /// <summary>
        /// target: what moves (the canvas itself or e.g. the anchor it follows). isVertical: along the canvas' right
        /// side instead of under it. onBeforeGrab: on every grab before the offset to the hand is taken (detach from a
        /// parent here). dockAnchor + onDock: see the class. isHeadFollow: see the class.
        /// </summary>
        public static VRGrabBar Create(RectTransform canvas, Transform target, bool isVertical = false,
            Action onBeforeGrab = null, Transform dockAnchor = null, Action onDock = null, bool isHeadFollow = false)
        {
            var size = canvas.rect.size;
            var thickness = size.x * _barThickness;

            var go = new GameObject("GrabBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(LayoutElement));
            go.layer = canvas.gameObject.layer;
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas, false);
            if (isVertical)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(thickness, size.y * _barLength);
                rect.anchoredPosition = new Vector2(thickness, 0f);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(size.x * _barLength, thickness);
                rect.anchoredPosition = new Vector2(0f, -thickness);
            }

            var bar = go.AddComponent<VRGrabBar>();
            bar._rect = rect;
            bar._target = target;
            bar._onBeforeGrab = onBeforeGrab;
            bar._dockAnchor = dockAnchor;
            bar._onDock = onDock;
            bar._isHeadFollow = isHeadFollow;
            bar._image = go.GetComponent<Image>();
            bar._image.color = _color;
            // Grabbed by the hand, not clicked by the UI laser.
            bar._image.raycastTarget = false;
            return bar;
        }

        /// <summary>
        /// Update, not LateUpdate: a VirtualKeyboard copies its follow target in LateUpdate.
        /// </summary>
        private void Update()
        {
            if (_rig == null || _input == null)
                return;

            if (_grabTransform != null)
            {
                if (_target == null || !_input.GetButton(_grabSide, VrButton.Grip).IsActive)
                {
                    Release();
                    return;
                }
                _target.SetPositionAndRotation(_grabTransform.position + _grabTransform.rotation * _grabPositionOffset,
                    _grabTransform.rotation * _grabRotationOffset);
                UpdateDockSphere();
                return;
            }

            var isHovered = false;
            foreach (var hand in _rig.HandGrabbers)
            {
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
            _image.color = isHovered ? _hoverColor : _color;

            if (_isFollowing)
                UpdateHeadFollow();
        }

        private static Vector3 GetHandPosition(VrHandSide side)
        {
            var visual = _rig.GetHandVisual(side);
            return visual != null ? visual.position : GetController(side).position;
        }

        private static Transform GetController(VrHandSide side)
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
            _grabPositionOffset = Quaternion.Inverse(_grabTransform.rotation) * (_target.position - _grabTransform.position);
            _image.color = _grabbedColor;
            if (_dockAnchor != null && _onDock != null)
            {
                CreateDockSphere();
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
            var isDocked = _dockSphere != null && _dockSphere.activeSelf && _grabTransform != null &&
                           Vector3.Distance(_grabTransform.position, _dockSphere.transform.position) <= _dockRadius * 2f;
            Logger.Log($"[VRGrabBar] {(_target != null ? _target.name : "target")} released" +
                       (isDocked ? " in the dock" : ""), LogCat.Ui);
            _grabTransform = null;
            _image.color = _color;
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
            var isInside = Vector3.Distance(_grabTransform.position, _dockSphere.transform.position) <= _dockRadius * 2f;
            _dockSphere.transform.localScale = Vector3.one * (_dockRadius * (isInside ? 2.6f : 2f));
        }

        private void CreateDockSphere()
        {
            if (_dockSphere != null)
                return;
            _dockSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _dockSphere.name = "GrabBarDock";
            Destroy(_dockSphere.GetComponent<Collider>());
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", _dockColor);
            _dockSphere.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void OnDestroy()
        {
            if (_dockSphere != null)
                Destroy(_dockSphere);
        }

        private void OnDisable()
        {
            if (_grabTransform != null)
                Release();
        }
    }
}
