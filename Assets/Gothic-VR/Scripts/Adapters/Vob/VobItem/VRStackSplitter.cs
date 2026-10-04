#if GOTHIC_HVR_INSTALLED
using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Inventory;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Adapters.Trade;
using Gothic.VR.Services;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Sockets;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableStackSplit), the split button is the trigger (T in the simulator):
    /// - empty hand near a stack held by the other hand, its button held: pieces jump into it, faster and faster.
    ///   They lie in the hand without holding grip (kinematic at the palm, not grabbed - a forced HVR grab flung the
    ///   physics hand away): grip grabs them normally, letting go drops them.
    /// - both hands hold the same item: this hand's stack joins the other hand's stack (this hand lets go).
    /// - a hand holds a stack near an empty socket (backpack, trade counter, loot), its button held: pieces gather at
    ///   the socket and go into it when the button is let go (letting go of the whole stack still socket it all).
    /// The stacks show their name with the amount meanwhile. The hero's inventory count stays right: held items count
    /// as inventory (VRPlayerService.SetGrab/UnsetGrab).
    /// </summary>
    public class VRStackSplitter : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly StackSplitService _stackSplitService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly Gothic.Core.Services.Meshes.DynamicMaterialService _dynamicMaterialService;

        private const float _maxHandDistance = 0.3f;
        private const float _maxSocketDistance = 0.3f;
        private const float _mergeLabelSeconds = 1.5f;

        private HVRHandGrabber _leftHand;
        private HVRHandGrabber _rightHand;

        // The running split/feed (button held).
        private HVRHandSide _buttonSide;
        private VobContainer _source;
        private VobContainer _piece;
        private HVRSocket _feedSocket;
        private bool _isSourceGrabbed;
        private int _piecesSplit;
        private float _nextPieceTime;

        private bool IsRunning => _source != null;

        /// <summary>
        /// Name + amount over the stacks being split/merged/fed - the hover label (VRFocus) isn't shown while held.
        /// </summary>
        private readonly Dictionary<VobContainer, TextMeshPro> _labels = new();

        /// <summary>
        /// Pieces lying in a hand without grip: not grabbed, so not counted as inventory (a world item).
        /// </summary>
        private readonly Dictionary<HVRHandGrabber, (VobContainer Piece, bool IsGripPressed)> _palmPieces = new();

        private void Awake()
        {
            gameObject.Inject();
        }

        private void Update()
        {
            if (!_stackSplitService.IsEnabled)
                return;

            if (_leftHand == null || _rightHand == null)
                FindHands();
            if (_leftHand == null || _rightHand == null)
                return;

            UpdatePalmPiece(_leftHand, HVRHandSide.Left);
            UpdatePalmPiece(_rightHand, HVRHandSide.Right);

            if (IsRunning)
            {
                Continue();
                return;
            }

            // The simulator's T: the left hand takes first. Hands near each other before a socket near a stack.
            if (TryStart(_leftHand, HVRHandSide.Left, _rightHand) || TryStart(_rightHand, HVRHandSide.Right, _leftHand))
                return;
            if (!TryStartFeed(_leftHand, HVRHandSide.Left))
                TryStartFeed(_rightHand, HVRHandSide.Right);
        }

        private void FindHands()
        {
            foreach (var hand in FindObjectsByType<HVRHandGrabber>(FindObjectsSortMode.None))
            {
                if (hand.IsLeftHand)
                    _leftHand = hand;
                else
                    _rightHand = hand;
            }
        }

        private bool IsSplitPressed(HVRHandSide side)
        {
            if (_vrPlayerService.VRPlayerInputs.UseWASD)
                return Keyboard.current[Key.T].wasPressedThisFrame;
            return HVRController.GetButtonState(side, HVRButtons.Trigger).JustActivated;
        }

        private bool IsSplitHeld(HVRHandSide side)
        {
            if (_vrPlayerService.VRPlayerInputs.UseWASD)
                return Keyboard.current[Key.T].isPressed;
            return HVRController.GetButtonState(side, HVRButtons.Trigger).Active;
        }

        /// <summary>
        /// What the hand holds: grabbed (counted as inventory) or a piece lying in it (not counted).
        /// </summary>
        private VobContainer GetHeld(HVRHandGrabber hand, out bool isGrabbed)
        {
            if (hand.GrabbedTarget != null)
            {
                isGrabbed = true;
                return hand.GrabbedTarget.GetComponentInParent<VobLoader>()?.Container;
            }
            isGrabbed = false;
            return _palmPieces.TryGetValue(hand, out var entry) && entry.Piece?.Go != null ? entry.Piece : null;
        }

        private static Vector3 GetItemPosition(VobContainer container)
        {
            var grabbable = container.Go.GetComponentInChildren<HVRGrabbable>();
            return grabbable != null ? grabbable.transform.position : container.Go.transform.position;
        }

        private bool IsInHand(VobContainer container)
        {
            if (container?.Go == null)
                return false;
            if (IsHeld(container))
                return true;
            foreach (var entry in _palmPieces.Values)
            {
                if (entry.Piece == container)
                    return true;
            }
            return false;
        }

        private bool TryStart(HVRHandGrabber hand, HVRHandSide side, HVRHandGrabber otherHand)
        {
            if (!IsSplitPressed(side))
                return false;

            var otherContainer = GetHeld(otherHand, out var isOtherGrabbed);
            var otherItem = otherContainer?.VobAs<IItem>();
            if (otherItem == null || otherContainer.Go.GetComponentInChildren<VRRuneCaster>() != null)
                return false;
            // The grabbed item moves, not its VobLoader root.
            if (Vector3.Distance(hand.transform.position, GetItemPosition(otherContainer)) > _maxHandDistance)
                return false;

            var held = GetHeld(hand, out var isGrabbed);
            if (held == null && hand.GrabbedTarget == null)
                return TryStartSplit(hand, side, otherContainer, otherItem, isOtherGrabbed);

            return held != null && TryMerge(hand, held, isGrabbed, otherContainer, otherItem, isOtherGrabbed);
        }

        /// <summary>
        /// The trader whose unpaid goods this is (trade counter), null for the hero's own items.
        /// </summary>
        private static NpcContainer GetUnpaidTrader(VobContainer container)
        {
            return container.Go.GetComponentInChildren<VRTradeGoods>() is { IsSettled: false } goods
                ? goods.Trader
                : null;
        }

        private bool TryStartSplit(HVRHandGrabber emptyHand, HVRHandSide side, VobContainer source, IItem item,
            bool isSourceGrabbed)
        {
            if (!_stackSplitService.CanSplit(item.Amount))
                return false;

            // A piece of unpaid trader goods is unpaid trader goods too (buy 3 of his 10 arrows).
            var palm = emptyHand.Palm != null ? emptyHand.Palm : emptyHand.transform;
            var piece = SpawnPiece(item, GetUnpaidTrader(source), palm.position, palm.rotation);
            if (piece == null)
                return false;
            PutIntoPalm(emptyHand, piece);

            item.Amount--;
            TakeFromSource(item, isSourceGrabbed);

            Begin(side, source, isSourceGrabbed, piece, null);
            Logger.Log($"[StackSplit] {GetInstanceName(item)}: 1 piece taken, {item.Amount} left", LogCat.VR);
            return true;
        }

        /// <summary>
        /// The hand holds a stack near an empty socket and presses its button: the first piece waits at the socket.
        /// </summary>
        private bool TryStartFeed(HVRHandGrabber hand, HVRHandSide side)
        {
            var stackGrabbable = hand.GrabbedTarget;
            if (stackGrabbable == null || !IsSplitPressed(side) ||
                stackGrabbable.GetComponentInParent<VRRuneCaster>() != null)
                return false;

            var source = stackGrabbable.GetComponentInParent<VobLoader>()?.Container;
            var item = source?.VobAs<IItem>();
            if (item == null || !_stackSplitService.CanSplit(item.Amount))
                return false;

            var socket = FindEmptySocket(stackGrabbable);
            if (socket == null)
                return false;

            var piece = SpawnPiece(item, GetUnpaidTrader(source), socket.transform.position,
                socket.transform.rotation);
            if (piece == null)
                return false;
            SetKinematic(piece, true);

            item.Amount--;
            TakeFromSource(item, true);

            Begin(side, source, true, piece, socket);
            Logger.Log($"[StackSplit] {GetInstanceName(item)}: feeding {socket.name}", LogCat.VR);
            return true;
        }

        private HVRSocket FindEmptySocket(HVRGrabbable stackGrabbable)
        {
            HVRSocket nearest = null;
            var nearestDistance = _maxSocketDistance;
            foreach (var socket in FindObjectsByType<HVRSocket>(FindObjectsSortMode.None))
            {
                if (socket.IsGrabbing || !socket.isActiveAndEnabled || !socket.CanHover(stackGrabbable))
                    continue;
                var distance = Vector3.Distance(socket.transform.position, stackGrabbable.transform.position);
                if (distance > nearestDistance)
                    continue;
                nearest = socket;
                nearestDistance = distance;
            }
            return nearest;
        }

        /// <summary>
        /// A piece taken off a grabbed stack leaves the inventory - the pieces lie in the world (palm, socket) until
        /// grabbed or socketed. A stack that isn't grabbed itself (lying in the palm) isn't counted anyway.
        /// </summary>
        private void TakeFromSource(IItem sourceItem, bool isSourceGrabbed)
        {
            if (isSourceGrabbed)
                _playerService.RemoveItem(GetInstanceName(sourceItem), 1);
        }

        private void Begin(HVRHandSide side, VobContainer source, bool isSourceGrabbed, VobContainer piece,
            HVRSocket feedSocket)
        {
            _buttonSide = side;
            _isSourceGrabbed = isSourceGrabbed;
            _source = source;
            _piece = piece;
            _feedSocket = feedSocket;
            _piecesSplit = 1;
            _nextPieceTime = Time.time + _stackSplitService.GetRepeatDelay(_piecesSplit, _feedSocket != null);
            SetLabelPinned(_source, true);
            SetLabelPinned(_piece, true);
        }

        /// <summary>
        /// Button held: one more piece from the stack to the piece.
        /// </summary>
        private void Continue()
        {
            var sourceItem = _source?.Go != null ? _source.VobAs<IItem>() : null;
            var pieceItem = _piece?.Go != null ? _piece.VobAs<IItem>() : null;
            var isPieceInPlace = _feedSocket != null ? !_feedSocket.IsGrabbing : IsInHand(_piece);
            if (!IsSplitHeld(_buttonSide) || sourceItem == null || pieceItem == null || !IsInHand(_source) ||
                !isPieceInPlace)
            {
                End();
                return;
            }

            if (Time.time < _nextPieceTime || !_stackSplitService.CanSplit(sourceItem.Amount))
                return;

            sourceItem.Amount--;
            pieceItem.Amount++;
            TakeFromSource(sourceItem, _isSourceGrabbed);
            _piecesSplit++;
            _nextPieceTime = Time.time + _stackSplitService.GetRepeatDelay(_piecesSplit, _feedSocket != null);
            RefreshLabel(_source);
            RefreshLabel(_piece);
        }

        private void End()
        {
            SetLabelPinned(_source, false);
            SetLabelPinned(_piece, false);

            if (_feedSocket != null && _piece?.Go != null)
            {
                // Into the socket in one go - its owner (backpack, counter) counts the whole amount once.
                SetKinematic(_piece, false);
                var grabbable = _piece.Go.GetComponentInChildren<HVRGrabbable>();
                if (grabbable == null || _feedSocket.IsGrabbing || !_feedSocket.TryGrab(grabbable, true))
                    Logger.LogWarning($"[StackSplit] {_piece.Go.name} couldn't go into {_feedSocket.name}", LogCat.VR);
            }

            if (_piece?.Go != null && _source?.Go != null)
                Logger.Log($"[StackSplit] {_piece.VobAs<IItem>()?.Amount} piece(s) taken, " +
                           $"{_source.VobAs<IItem>()?.Amount} left", LogCat.VR);
            _source = null;
            _piece = null;
            _feedSocket = null;
            _piecesSplit = 0;
        }

        /// <summary>
        /// This hand's stack of the same item joins the other hand's stack, this hand is empty afterwards.
        /// </summary>
        private bool TryMerge(HVRHandGrabber hand, VobContainer source, bool isSourceGrabbed, VobContainer target,
            IItem targetItem, bool isTargetGrabbed)
        {
            var sourceItem = source?.VobAs<IItem>();
            if (sourceItem == null || source == target || source.Go.GetComponentInChildren<VRRuneCaster>() != null)
                return false;

            // Own items join own items, a trader's unpaid goods only his unpaid goods.
            if (GetUnpaidTrader(source) != GetUnpaidTrader(target))
                return false;

            var instanceName = GetInstanceName(sourceItem);
            if (!instanceName.EqualsIgnoreCase(GetInstanceName(targetItem)))
                return false;

            // A grabbed stack leaves the inventory when let go; a grabbed target takes the amount in again.
            var amount = Mathf.Max(1, sourceItem.Amount);
            _palmPieces.Remove(hand);
            if (isSourceGrabbed)
                hand.GrabbedTarget.ForceRelease();
            targetItem.Amount += amount;
            if (isTargetGrabbed)
                _playerService.AddItem(instanceName, amount);

            _saveGameService.UntrackLooseItem(source);
            _vobService.RemoveWorldItem(source);
            StartCoroutine(ShowLabelBriefly(target));
            Logger.Log($"[StackSplit] {instanceName}: {amount} joined, now {targetItem.Amount}", LogCat.VR);
            return true;
        }

        private IEnumerator ShowLabelBriefly(VobContainer container)
        {
            SetLabelPinned(container, true);
            yield return new WaitForSeconds(_mergeLabelSeconds);
            SetLabelPinned(container, false);
        }

        private void SetLabelPinned(VobContainer container, bool isPinned)
        {
            if (container?.Go == null)
                return;

            if (!isPinned)
            {
                if (_labels.Remove(container, out var oldLabel) && oldLabel != null)
                    Destroy(oldLabel.gameObject);
                _dynamicMaterialService.ResetDynamicValue(container.Go, Constants.ShaderPropertyFocusBrightness,
                    Constants.ShaderPropertyFocusBrightnessDefault);
                return;
            }

            if (_labels.ContainsKey(container))
                return;

            // Highlighted like hovered items, for the whole split.
            _dynamicMaterialService.SetDynamicValue(container.Go, Constants.ShaderPropertyFocusBrightness,
                Constants.ShaderPropertyFocusBrightnessValue);

            var labelGo = new GameObject("StackLabel");
            // About the size of the hover name (VRFocus).
            labelGo.transform.localScale = Vector3.one * 0.06f;
            var label = labelGo.AddComponent<TextMeshPro>();
            Gothic.VR.Adapters.UI.VRGothicText.Apply(label);
            label.fontSize = 12;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = Color.white;
            _labels[container] = label;
            RefreshLabel(container);
        }

        private void RefreshLabel(VobContainer container)
        {
            if (container?.Go != null && _labels.TryGetValue(container, out var label) && label != null)
                label.text = container.Props.GetFocusName();
        }

        /// <summary>
        /// The labels float over their stacks, facing the camera.
        /// </summary>
        private void LateUpdate()
        {
            if (_labels.Count == 0)
                return;

            var camera = Camera.main;
            foreach (var (container, label) in new List<KeyValuePair<VobContainer, TextMeshPro>>(_labels))
            {
                if (container?.Go == null || label == null)
                {
                    if (label != null)
                        Destroy(label.gameObject);
                    _labels.Remove(container);
                    continue;
                }

                // The hover name shows the amount already (VRFocus refreshes it) - no second label.
                var focus = container.Go.GetComponentInChildren<VRFocus>();
                label.gameObject.SetActive(focus == null || !focus.IsNameShown);

                var renderer = container.Go.GetComponentInChildren<Renderer>();
                var top = renderer != null
                    ? new Vector3(renderer.bounds.center.x, renderer.bounds.max.y, renderer.bounds.center.z)
                    : container.Go.transform.position;
                label.transform.position = top + Vector3.up * 0.1f;
                if (camera != null)
                    label.transform.rotation = Quaternion.LookRotation(label.transform.position - camera.transform.position);
            }
        }

        private void OnDestroy()
        {
            foreach (var label in _labels.Values)
            {
                if (label != null)
                    Destroy(label.gameObject);
            }
            _labels.Clear();
        }

        private static bool IsHeld(VobContainer container)
        {
            var grabbable = container.Go.GetComponentInChildren<HVRGrabbable>();
            return grabbable != null && grabbable.IsBeingHeld;
        }

        private static void SetKinematic(VobContainer container, bool isKinematic)
        {
            var rigidbody = container.Go.GetComponentInChildren<Rigidbody>();
            if (rigidbody != null)
                rigidbody.isKinematic = isKinematic;
        }

        private VobContainer SpawnPiece(IItem item, NpcContainer unpaidTrader, Vector3 position, Quaternion rotation)
        {
            var instanceName = GetInstanceName(item);
            var piece = _vobService.CreateItem(new Item
            {
                Name = instanceName,
                Visual = new VisualMesh(),
                Instance = instanceName,
                Amount = 1
            });
            if (piece?.Go == null)
                return null;

            if (unpaidTrader != null)
                piece.Go.AddComponent<VRTradeGoods>().Init(unpaidTrader);

            piece.Go.transform.SetPositionAndRotation(position, rotation);
            SetKinematic(piece, false);

            // A loose world item like one taken out of the backpack - culled and saved when dropped.
            _vobMeshCullingService.AddCullingEntry(piece);
            _saveGameService.CurrentWorldData.Vobs.Add(piece.Vob);
            _saveGameService.TrackLooseItem(piece);
            return piece;
        }

        /// <summary>
        /// The piece lies in the palm, kinematic and following it, its collision with the hands off - a forced HVR grab
        /// flung the physics hand away (the piece spawned inside the hand's colliders).
        /// </summary>
        private void PutIntoPalm(HVRHandGrabber hand, VobContainer piece)
        {
            var palm = hand.Palm != null ? hand.Palm : hand.transform;
            SetKinematic(piece, true);
            piece.Go.transform.SetParent(palm, true);
            SetHandCollisions(piece, false);
            _palmPieces[hand] = (piece, false);

            var grabbable = piece.Go.GetComponentInChildren<HVRGrabbable>();
            if (grabbable != null)
                grabbable.Grabbed.AddListener(OnPalmPieceGrabbed);
        }

        /// <summary>
        /// Grip on the piece: HVR takes over, it is a normal item again.
        /// </summary>
        private void OnPalmPieceGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            grabbable.Grabbed.RemoveListener(OnPalmPieceGrabbed);
            foreach (var hand in new List<HVRHandGrabber>(_palmPieces.Keys))
            {
                var piece = _palmPieces[hand].Piece;
                if (piece?.Go == null || piece.Go.GetComponentInChildren<HVRGrabbable>() != grabbable)
                    continue;
                _palmPieces.Remove(hand);
                // HVR ignores the hand collisions of what it holds and restores them on release - not us.
                LetGoOfPalm(piece, isCollisionRestored: false);
            }
        }

        private void LetGoOfPalm(VobContainer piece, bool isCollisionRestored)
        {
            if (piece?.Go == null)
                return;
            piece.Go.transform.SetParent(null, true);
            SetKinematic(piece, false);
            if (isCollisionRestored)
                StartCoroutine(RestoreHandCollisionsLater(piece));
        }

        private IEnumerator RestoreHandCollisionsLater(VobContainer piece)
        {
            // Out of the hand first, or it is pushed out of it.
            yield return new WaitForSeconds(0.5f);
            SetHandCollisions(piece, true);
        }

        private void SetHandCollisions(VobContainer piece, bool isEnabled)
        {
            if (piece?.Go == null)
                return;
            var pieceColliders = piece.Go.GetComponentsInChildren<Collider>(true);
            foreach (var hand in new[] { _leftHand, _rightHand })
            {
                if (hand == null)
                    continue;
                foreach (var handCollider in hand.GetComponentsInChildren<Collider>(true))
                {
                    foreach (var pieceCollider in pieceColliders)
                        Physics.IgnoreCollision(handCollider, pieceCollider, !isEnabled);
                }
            }
        }

        /// <summary>
        /// A piece lying in the palm: grip grabs it (OnPalmPieceGrabbed); a grip press + release that didn't grab it
        /// drops it.
        /// </summary>
        private void UpdatePalmPiece(HVRHandGrabber hand, HVRHandSide side)
        {
            if (!_palmPieces.TryGetValue(hand, out var entry))
                return;

            if (entry.Piece?.Go == null)
            {
                _palmPieces.Remove(hand);
                return;
            }

            var grip = HVRController.GetButtonState(side, HVRButtons.Grip);
            if (grip.JustActivated)
            {
                _palmPieces[hand] = (entry.Piece, true);
                return;
            }

            if (entry.IsGripPressed && !grip.Active)
            {
                _palmPieces.Remove(hand);
                LetGoOfPalm(entry.Piece, isCollisionRestored: true);
            }
        }

        private static string GetInstanceName(IItem item)
        {
            return !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
        }
    }
}
#endif
