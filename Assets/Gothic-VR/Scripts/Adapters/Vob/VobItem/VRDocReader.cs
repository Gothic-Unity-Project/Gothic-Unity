using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// A document (book, map, letter) is opened / closed with the trigger of the hand holding it (R in the simulator) -
    /// one hand is enough, like a rune. The pages (VRDocViewer's canvas) are held like a book: the reading hand at
    /// their lower outer corner (left hand: lower left, right hand: lower right). Opened, they start upright facing
    /// the player's eyes, then move and turn with the reading hand like a held book. While open, the item's own mesh
    /// is hidden. Added by VRVobItem on the first hand grab, stays on the item.
    /// </summary>
    public class VRDocReader : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private VRVobItem _vobItem;
        private VrHandSide _readingHand;
        private Transform _canvas;
        private Camera _camera;
        // The pages' pose relative to the reading hand, captured when they opened or the hand changed.
        private Transform _capturedCanvas;
        private Transform _capturedHand;
        private Vector3 _localPosition;
        private Quaternion _localRotation;
        private readonly List<Renderer> _hiddenRenderers = new();

        private void Awake()
        {
            this.Inject();
            _vobItem = GetComponent<VRVobItem>();
        }

        private void Update()
        {
            var isLeft = _vrPlayerService.GrabbedItemLeft == gameObject;
            var isRight = _vrPlayerService.GrabbedItemRight == gameObject;
            if (!isLeft && !isRight)
                return;

            // The reading hand let go, the other one still holds the pages.
            if (_readingHand == VrHandSide.Left && !isLeft)
                _readingHand = VrHandSide.Right;
            else if (_readingHand == VrHandSide.Right && !isRight)
                _readingHand = VrHandSide.Left;

            if (!TryGetPressedHand(isLeft, isRight, out var pressedHand))
                return;

            if (FindCanvas() != null)
            {
                Logger.Log($"[VRDocReader] {name} closed", LogCat.VR);
                _canvas = null;
                _vobItem.CloseDocument();
                return;
            }

            _readingHand = pressedHand;
            if (!_vobItem.ShowDocument())
                return;

            // The pages exist now - LateUpdate follows the cached canvas, nothing scans the children per frame.
            FindCanvas();
            Logger.Log($"[VRDocReader] {name} opened, reading hand={_readingHand}", LogCat.VR);
        }

        private bool TryGetPressedHand(bool isLeft, bool isRight, out VrHandSide hand)
        {
            hand = isRight ? VrHandSide.Right : VrHandSide.Left;
            if (_vrInput.IsSimulator)
                return Keyboard.current != null && Keyboard.current[Key.R].wasPressedThisFrame;

            if (isLeft && _vrInput.GetButton(VrHandSide.Left, VrButton.Trigger).IsJustActivated)
            {
                hand = VrHandSide.Left;
                return true;
            }
            return isRight && _vrInput.GetButton(VrHandSide.Right, VrButton.Trigger).IsJustActivated;
        }

        private Transform FindCanvas()
        {
            if (_canvas != null)
                return _canvas;

            if (TryGetComponent<VRDocViewer>(out var viewer) && viewer.Canvas != null &&
                viewer.Canvas.gameObject.activeSelf)
                _canvas = viewer.Canvas;
            return _canvas;
        }

        private void LateUpdate()
        {
            // Nothing to do while no pages are open and no mesh is hidden (item lying around, in a socket, ...).
            if (_canvas == null && _hiddenRenderers.Count == 0)
                return;

            var canvas = _canvas;
            if (canvas == null)
            {
                _capturedCanvas = null;
                _capturedHand = null;
                ShowItemMesh();
                return;
            }

            if (_camera == null)
                _camera = Camera.main;
            var handGo = _vrPlayerService.GetHandModelGo(_readingHand);
            if (_camera == null || handGo == null)
                return;

            var hand = handGo.transform;
            var rect = (RectTransform)canvas;
            UpdateScale(rect);

            if (canvas != _capturedCanvas)
                HideItemMesh(canvas);

            if (canvas != _capturedCanvas || hand != _capturedHand)
            {
                // Upright and facing the eyes once - from then on the pages move and turn with the hand.
                GetUprightPose(rect, _camera.transform, hand.position, out var position, out var rotation);
                var toLocal = Quaternion.Inverse(hand.rotation);
                _localPosition = toLocal * (position - hand.position);
                _localRotation = toLocal * rotation;
                _capturedCanvas = canvas;
                _capturedHand = hand;
            }

            canvas.SetPositionAndRotation(hand.position + hand.rotation * _localPosition,
                hand.rotation * _localRotation);
        }

        private void OnDisable()
        {
            ShowItemMesh();
        }

        private void OnDestroy()
        {
            ShowItemMesh();
        }

        /// <summary>
        /// The open pages replace the item - its own mesh hides (all renderers but the pages').
        /// </summary>
        private void HideItemMesh(Transform canvas)
        {
            ShowItemMesh();
            foreach (var itemRenderer in GetComponentsInChildren<Renderer>())
            {
                if (!itemRenderer.enabled || itemRenderer.transform.IsChildOf(canvas))
                    continue;
                itemRenderer.enabled = false;
                _hiddenRenderers.Add(itemRenderer);
            }
            Logger.Log($"[VRDocReader] {name}: item mesh hidden ({_hiddenRenderers.Count} renderers)", LogCat.VR);
        }

        private void ShowItemMesh()
        {
            if (_hiddenRenderers.Count == 0)
                return;
            foreach (var itemRenderer in _hiddenRenderers)
            {
                if (itemRenderer != null)
                    itemRenderer.enabled = true;
            }
            Logger.Log($"[VRDocReader] {name}: item mesh shown again", LogCat.VR);
            _hiddenRenderers.Clear();
        }

        /// <summary>
        /// The pages' longer side is VRConstants.DocReader.PagesSize in the world, whatever the item's scale.
        /// </summary>
        private void UpdateScale(RectTransform canvas)
        {
            var size = canvas.rect.size;
            var worldScale = VRConstants.DocReader.PagesSize / Mathf.Max(size.x, size.y, 1f);
            var parentScale = canvas.parent.lossyScale;
            canvas.localScale = new Vector3(worldScale / parentScale.x, worldScale / parentScale.y,
                worldScale / parentScale.z);
        }

        /// <summary>
        /// The pages face the eyes upright, their lower outer corner in the reading hand.
        /// </summary>
        private void GetUprightPose(RectTransform canvas, Transform head, Vector3 hand, out Vector3 position,
            out Quaternion rotation)
        {
            var forward = hand - head.position;
            if (forward.sqrMagnitude < 0.0001f)
                forward = head.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            right = right.sqrMagnitude > 0.0001f ? right.normalized : head.right;
            var up = Vector3.Cross(forward, right);

            var size = canvas.rect.size;
            var worldScale = VRConstants.DocReader.PagesSize / Mathf.Max(size.x, size.y, 1f);
            var width = size.x * worldScale;
            var height = size.y * worldScale;
            // Left hand: the pages reach to the right and up; right hand: to the left and up.
            var side = _readingHand == VrHandSide.Left ? 1f : -1f;
            position = hand +
                       right * (side * (width / 2f - VRConstants.DocReader.CornerInset)) +
                       up * (height / 2f - VRConstants.DocReader.CornerInset) +
                       forward * VRConstants.DocReader.BehindHand;

            // A world space canvas is read looking along its +Z.
            rotation = Quaternion.LookRotation(forward, up);
        }
    }
}
