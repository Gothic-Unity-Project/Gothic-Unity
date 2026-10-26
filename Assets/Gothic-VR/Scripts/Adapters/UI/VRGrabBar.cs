using System;
using Gothic.Core.Logging;
#if GOTHIC_HVR_INSTALLED
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Player;
#endif
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

        private RectTransform _rect;
        private Transform _target;
        private Action _onBeforeGrab;
        private Transform _dockAnchor;
        private Action _onDock;
        private GameObject _dockSphere;
        private Image _image;
        private Transform _grabTransform;
        private Vector3 _grabPositionOffset;
        private Quaternion _grabRotationOffset;
#if GOTHIC_HVR_INSTALLED
        private static HVRPlayerController _player;
        private HVRHandGrabber _hand;
#endif

        public bool IsGrabbed => _grabTransform != null;

        /// <summary>
        /// target: what moves (the canvas itself or e.g. the anchor it follows). isVertical: along the canvas' right
        /// side instead of under it. onBeforeGrab: on every grab before the offset to the hand is taken (detach from a
        /// parent here). dockAnchor + onDock: see the class.
        /// </summary>
        public static VRGrabBar Create(RectTransform canvas, Transform target, bool isVertical = false,
            Action onBeforeGrab = null, Transform dockAnchor = null, Action onDock = null)
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
#if GOTHIC_HVR_INSTALLED
            if (_grabTransform != null)
            {
                if (_target == null || _hand == null || !_hand.Controller.GripButtonState.Active)
                {
                    Release();
                    return;
                }
                _target.SetPositionAndRotation(_grabTransform.position + _grabTransform.rotation * _grabPositionOffset,
                    _grabTransform.rotation * _grabRotationOffset);
                UpdateDockSphere();
                return;
            }

            if (_player == null)
                _player = FindAnyObjectByType<HVRPlayerController>();
            if (_player == null)
                return;

            var isHovered = false;
            foreach (var hand in new[] { _player.LeftHand, _player.RightHand })
            {
                // A hand holding something (sword, rune) doesn't grab the bar.
                if (hand == null || hand.GrabbedTarget != null || !IsInReach(GetHandPosition(hand)))
                    continue;
                isHovered = true;
                if (hand.Controller.GripButtonState.JustActivated)
                {
                    Grab(hand);
                    return;
                }
            }
            _image.color = isHovered ? _hoverColor : _color;
#endif
        }

#if GOTHIC_HVR_INSTALLED
        private static Vector3 GetHandPosition(HVRHandGrabber hand)
        {
            return hand.Palm != null ? hand.Palm.position : hand.transform.position;
        }

        /// <summary>
        /// The tracked controller, not the physics hand - it lags behind and would shake the target.
        /// </summary>
        private void Grab(HVRHandGrabber hand)
        {
            _onBeforeGrab?.Invoke();
            _hand = hand;
            _grabTransform = hand.TrackedController != null ? hand.TrackedController : hand.transform;
            _grabRotationOffset = Quaternion.Inverse(_grabTransform.rotation) * _target.rotation;
            _grabPositionOffset = Quaternion.Inverse(_grabTransform.rotation) * (_target.position - _grabTransform.position);
            _image.color = _grabbedColor;
            if (_dockAnchor != null && _onDock != null)
            {
                CreateDockSphere();
                _dockSphere.SetActive(true);
                UpdateDockSphere();
            }
            hand.Controller.Vibrate(0.3f, 0.05f);
            Logger.Log($"[VRGrabBar] {_target.name} grabbed ({hand.HandSide})", LogCat.Ui);
        }
#endif

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
#if GOTHIC_HVR_INSTALLED
            _hand = null;
#endif
            _image.color = _color;
            if (_dockSphere != null)
                _dockSphere.SetActive(false);
            if (isDocked)
                _onDock();
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
