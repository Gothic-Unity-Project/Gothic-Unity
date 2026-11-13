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
    /// their lower outer corner (left hand: lower left, right hand: lower right), facing the player's eyes whatever
    /// way the item itself is turned in the hand. Added by VRVobItem on the first hand grab, stays on the item.
    /// </summary>
    public class VRDocReader : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        [Tooltip("World size of the pages' longer side (m).")]
        [SerializeField] private float _pagesSize = 0.6f;
        [Tooltip("How far the reading hand reaches into the pages from their lower outer corner (m).")]
        [SerializeField] private float _cornerInset = 0.06f;
        [Tooltip("Distance of the pages behind the reading hand, seen from the eyes (m).")]
        [SerializeField] private float _behindHand = 0.03f;

        private VRVobItem _vobItem;
        private VrHandSide _readingHand;
        private Transform _canvas;
        private Camera _camera;

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
            if (_vobItem.ShowDocument())
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

            foreach (Transform child in transform)
            {
                if (child.name == VRDocViewer.CanvasName)
                {
                    _canvas = child;
                    break;
                }
            }
            return _canvas;
        }

        private void LateUpdate()
        {
            var canvas = FindCanvas();
            if (canvas == null)
                return;

            if (_camera == null)
                _camera = Camera.main;
            var hand = _vrPlayerService.GetHandModelGo(_readingHand);
            if (_camera == null || hand == null)
                return;

            PlaceAtHand((RectTransform)canvas, _camera.transform, hand.transform.position);
        }

        /// <summary>
        /// The pages face the eyes upright, their lower outer corner in the reading hand.
        /// </summary>
        private void PlaceAtHand(RectTransform canvas, Transform head, Vector3 hand)
        {
            var forward = hand - head.position;
            if (forward.sqrMagnitude < 0.0001f)
                forward = head.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            right = right.sqrMagnitude > 0.0001f ? right.normalized : head.right;
            var up = Vector3.Cross(forward, right);

            var size = canvas.rect.size;
            var worldScale = _pagesSize / Mathf.Max(size.x, size.y, 1f);
            var parentScale = canvas.parent.lossyScale;
            canvas.localScale = new Vector3(worldScale / parentScale.x, worldScale / parentScale.y,
                worldScale / parentScale.z);

            var width = size.x * worldScale;
            var height = size.y * worldScale;
            // Left hand: the pages reach to the right and up; right hand: to the left and up.
            var side = _readingHand == VrHandSide.Left ? 1f : -1f;
            var position = hand +
                           right * (side * (width / 2f - _cornerInset)) +
                           up * (height / 2f - _cornerInset) +
                           forward * _behindHand;

            // A world space canvas is read looking along its +Z.
            canvas.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
        }
    }
}
