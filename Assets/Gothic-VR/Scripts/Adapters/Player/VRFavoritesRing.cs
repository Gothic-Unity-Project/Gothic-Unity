using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.UI;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.UI;
using Gothic.VR.Adapters.Vob.VobItem;
using Gothic.VR.Domain.Player;
using Gothic.VR.Models.FavoritesRing;
using Gothic.VR.Services;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using RingConstants = Gothic.VR.VRConstants.FavoritesRing;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrSpellRing + the "spellRing" option (Immersion menu): the favorites ring - Gothic's spell
    /// selection (hold the magic key, the equipped runes/scrolls circle the hero) in VR, plus the equipped weapons,
    /// favorite items and the money. What it holds and the put-in rules are VrFavoritesService's; this shows it and
    /// handles the hands. Its slot visuals are VRFavoritesRingSlotViews.
    /// - Open/close: both hands empty and raised above the head, both triggers held (VrFavoritesGestureDomain).
    ///   Loading a game closes it.
    /// - A carousel around the player at eye level, the slot looked at selected: at most 8 spells; with
    ///   DeveloperConfig.EnableRingWeaponSlots the equipped melee weapon and bow/crossbow (Gothic's one of each); with
    ///   EnableRingItemSlots up to 3 favorite items (a torch, a key, potions, food) and the trade currency
    ///   (G1 ore, G2 gold: TRADE_CURRENCY_INSTANCE). The front spell shows its effect after PreviewSeconds (like
    ///   Gothic), the slot a hand is near grows and shows its name and how many are left in the backpack.
    /// - Grip near a spell: it goes into that hand (ready to cast) and the ring closes. Weapons and items are the real
    ///   items: grabbed like out of a socket (not from afar) - one of them. Grip held at the slot of a stack for
    ///   TransferDelaySeconds takes more and more of it into the hand; moving the hand away closes the ring.
    /// - Trigger held + moving the hand sideways turns the carousel; a trigger click (released without moving) on a
    ///   spell or weapon unequips it, on an item takes it off the ring - it stays in the backpack.
    /// - Let go of a held item at a slot (within PutInGraceSeconds after it touched it): a rune/scroll into the empty
    ///   slot is equipped (like armor: it goes into the backpack with [E]), a weapon too (onto the equipped one of its
    ///   kind it replaces it), anything else becomes a favorite item; at its own slot it just goes back into the
    ///   backpack.
    /// Only what is in the backpack shows - an equipped weapon in a holster, in a hand or dropped doesn't.
    /// </summary>
    public class VRFavoritesRing : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly VrFavoritesService _favoritesService;


        private readonly List<FavoritesRingSlotView> _slots = new();
        // The direct grabbers of both hands (IVrRig.HandGrabbers).
        private readonly List<IVrGrabber> _hands = new();
        private VRFavoritesRingSlotViews _slotViews;
        private IVrRig _rig;
        private bool _isTeleportLocked;
        private bool _isOpen;

        private float _ringAngle;
        private bool _isDragging;
        private bool _isTriggerDown;
        private VrHandSide _dragHand;
        private Vector3 _dragStartLocal;
        private float _dragStartAngle;
        private FavoritesRingSlotView _pressedSlot;

        private FavoritesRingSlotView _hovered;
        private FavoritesRingSlotView _front;
        private float _frontSince;
        private TextMeshPro _label;
        // The selected (front) slot's name - always shown.
        private TextMeshPro _frontLabel;

        // Per hand (0 left, 1 right): the slot the held item touched last, and when.
        private readonly FavoritesRingPutInCandidate[] _putInCandidates = new FavoritesRingPutInCandidate[2];
        private FavoritesRingPutInCandidate _pendingPutIn;
        private FavoritesRingTransfer _transfer;
        private FavoritesRingTakenWeapon _takenWeapon;

        public bool IsOpen => _isOpen;

        private void Awake()
        {
            this.Inject();
            _slotViews = gameObject.AddComponent<VRFavoritesRingSlotViews>();
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);
        }

        private void Start()
        {
            _rig = _vrPlayerService.VRContextInteractionService?.Rig;
            _slotViews.Rig = _rig;
            if (_rig == null)
            {
                Logger.LogWarning("[FavoritesRing] No VR rig - ring items can't be grabbed.", LogCat.VR);
                return;
            }
            foreach (var hand in _rig.HandGrabbers)
            {
                if (hand.Kind != VrGrabberKind.Hand)
                    continue;
                hand.BeforeGrabbed += OnBeforeGrabbed;
                hand.Grabbed += OnGrabbed;
                hand.Released += OnReleased;
                _hands.Add(hand);
            }
        }

        private void OnLoadGameStart()
        {
            if (_isOpen)
                Close("game loading");
        }

        private bool IsEnabled()
        {
            if (Time.unscaledTime >= _nextOptionCheckTime)
            {
                _nextOptionCheckTime = Time.unscaledTime + _optionCheckSeconds;
                // No Gothic.ini before a game was picked (profile menu).
                _isOptionOn = _configService.Gothic != null &&
                              _configService.Gothic.GetInt(VRConstants.IniNames.SpellRing, 1) == 1;
            }
            return _configService.Dev.EnableVrSpellRing && _isOptionOn;
        }

        // The "spellRing" option, read once a second.
        private const float _optionCheckSeconds = 1f;
        private float _nextOptionCheckTime;
        private bool _isOptionOn;

        private void Update()
        {
            if (!IsEnabled() || _playerService.HeroContainer == null)
            {
                if (_isOpen)
                    Close("disabled");
                return;
            }

            UpdateTakenWeapon();
            UpdateGesture();
            if (!_isOpen)
                return;
            // A dialog, a knockout or a transformation while it's open: it stayed open with teleport locked.
            if (_favoritesService.IsHeroBusy())
            {
                Close("the hero is busy");
                return;
            }

            if (UpdateTransfer())
                return;
            UpdateTakenTimeout();
            UpdatePendingPutIn();
            if (!_isOpen)
                return;
            UpdateZoom();
            UpdateDrag();
            UpdateLayout();
            UpdateHover();
            UpdateFrontPreview();
            UpdatePutInCandidates();
        }

        /// <summary>
        /// The open/close gesture (VrFavoritesService.TickGesture) toggles the ring.
        /// </summary>
        private void UpdateGesture()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (!_favoritesService.TickGesture(head))
                return;
            if (_isOpen)
                Close("gesture");
            else
                Open(head);
        }

        private void Open(Transform head)
        {
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
            // The player stands in its middle, a little below the eyes (at eye level it hid the view); turned around,
            // the first slot is in front of him.
            transform.SetPositionAndRotation(head.position + Vector3.down * RingConstants.AroundPlayerBelowEyes,
                Quaternion.LookRotation(-forward, Vector3.up));
            _aroundPlayerRotation = transform.rotation;
            transform.localScale = Vector3.one * RingScale;

            _isOpen = true;
            _ringAngle = 0f;
            LockTeleport(true);
            Rebuild();
            Logger.Log($"[FavoritesRing] Opened with {_slots.Count(s => s.Kind == FavoriteSlotKind.Spell)} spell(s), " +
                       $"{_slots.Count(s => s.IsWeapon)} weapon(s), " +
                       $"{_slots.Count(s => s.Kind is FavoriteSlotKind.Item or FavoriteSlotKind.Currency)} item(s)",
                LogCat.VR);
        }

        /// <summary>
        /// DeveloperConfig.EnableRingZoom: the right stick zooms the ring - pushed forward it aimed the thumbstick
        /// teleport as well (XRI). A teleport enabled by others meanwhile stays enabled.
        /// </summary>
        private void LockTeleport(bool isLocked)
        {
            if (_rig == null)
                return;
            if (isLocked)
            {
                if (!_configService.Dev.EnableRingZoom || !_rig.TeleportEnabled)
                    return;
                _rig.TeleportEnabled = false;
                _isTeleportLocked = true;
            }
            else if (_isTeleportLocked)
            {
                _rig.TeleportEnabled = true;
                _isTeleportLocked = false;
            }
        }

        private void Close(string reason)
        {
            _isOpen = false;
            LockTeleport(false);
            _isDragging = false;
            _isTriggerDown = false;
            _hovered = null;
            _front = null;
            _transfer = null;
            _pendingPutIn = null;
            _putInCandidates[0] = _putInCandidates[1] = null;
            ClearSlots();
            if (_label != null)
                _label.gameObject.SetActive(false);
            if (_frontLabel != null)
                _frontLabel.gameObject.SetActive(false);
            Logger.Log($"[FavoritesRing] Closed ({reason})", LogCat.VR);
        }

        /// <summary>
        /// The ring's contents (VrFavoritesService.BuildSlots), each shown in its slot.
        /// </summary>
        private void Rebuild()
        {
            var previousFront = _front?.InstanceName;
            ClearSlots();
            foreach (var content in _favoritesService.BuildSlots())
                _slots.Add(_slotViews.Create(content, transform));

            var step = 360f / _slots.Count;
            for (var i = 0; i < _slots.Count; i++)
                _slots[i].Angle = i * step;

            // Keep the same slot in front.
            var frontIndex = previousFront == null ? -1 : _slots.FindIndex(s => s.InstanceName == previousFront);
            _ringAngle = frontIndex < 0 ? 0f : -frontIndex * step;
            _front = null;
            _hovered = null;
        }

        /// <summary>
        /// Runes are all called "Runa"/"Rune" - the spell is their description, like in the backpack.
        /// </summary>
        public static string GetSpellName(ItemInstance item)
        {
            return string.IsNullOrEmpty(item?.Description) ? item?.Name : item.Description;
        }

        private static string GetSlotName(FavoritesRingSlotView slot)
        {
            var name = slot.Kind == FavoriteSlotKind.Spell ? GetSpellName(slot.Item) : slot.Item?.Name;
            return slot.Amount > 1 ? $"{name} ({slot.Amount})" : name;
        }

        /// <summary>
        /// The ring shows its own name (with the amount in the backpack) - VRFocus's hover name above a ring item
        /// hides. It shows it on hover (in Update); this runs after.
        /// </summary>
        private void LateUpdate()
        {
            if (!_isOpen)
                return;
            foreach (var slot in _slots)
            {
                if (slot.Container != null && slot.FocusName != null && slot.FocusName.activeSelf)
                    slot.FocusName.SetActive(false);
            }
        }

        private void ClearSlots()
        {
            // The labels hang at a slot - they'd go with it.
            foreach (var label in new[] { _label, _frontLabel })
            {
                if (label == null)
                    continue;
                label.transform.SetParent(transform, false);
                label.gameObject.SetActive(false);
            }
            _hovered = null;
            _front = null;
            foreach (var slot in _slots)
                _slotViews.Remove(slot);
            _slots.Clear();
            _favoritesService.ClearSlots();
        }

        private Quaternion _aroundPlayerRotation = Quaternion.identity;
        private const float RingScale = RingConstants.AroundPlayerScale;

        // DeveloperConfig.EnableRingZoom: the ring's radius - the slots come closer or move away, keeping their size
        // (scaling the whole ring grew the slots too).
        private float RingRadius => RingConstants.RingRadius * (_configService.Dev.EnableRingZoom ? _ringZoom : 1f);

        // DeveloperConfig.EnableRingZoom: the ring's radius, changed with the right stick (simulator: mouse wheel) like
        // the menu's distance - kept for the session.
        private static float _ringZoom = 1f;

        // A carousel - let go while turning it, it spins on and slows down; a
        // trigger press stops it. Degrees per second, the drag's last speed.
        private float _spinVelocity;
        private float _lastDragAngle;
        private bool _isSpinStoppedByPress;
        private float _dragStartHandAngle;

        /// <summary>
        /// DeveloperConfig.EnableRingZoom: the right stick up/down (mouse wheel) makes the ring bigger/smaller - it moves
        /// away from or towards the eyes with its slots.
        /// </summary>
        private void UpdateZoom()
        {
            if (!_configService.Dev.EnableRingZoom)
                return;

            var change = 0f;
            if (_vrInput.IsSimulator)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                // A notch is 120 or 1 depending on the Input System's scroll setting - the direction counts.
                var scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
                if (!Mathf.Approximately(scroll, 0f))
                    change = Mathf.Sign(scroll) * RingConstants.RingZoomPerWheelNotch;
            }
            else
            {
                var stick = _vrInput.GetThumbstick(VrHandSide.Right);
                if (Mathf.Abs(stick.y) >= RingConstants.ZoomStickDeadZone)
                    change = stick.y * RingConstants.RingZoomSpeed * Time.deltaTime;
            }
            if (Mathf.Approximately(change, 0f))
                return;

            _ringZoom = Mathf.Clamp(_ringZoom + change, RingConstants.MinRingZoom, RingConstants.MaxRingZoom);
        }

        /// <summary>
        /// The hand's angle around the ring's axis (0 = the front slot's direction, like the slots' angles) - turning the
        /// ring around the player follows the hand from any side.
        /// </summary>
        private static float GetHandAngle(Vector3 local)
        {
            return Mathf.Atan2(local.x, -local.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The selected slot is the one the head looks at - turning the head selects too. Positive angles go to the
        /// right.
        /// </summary>
        private void UpdateLayout()
        {
            // Around the player it keeps its direction in the world: it hangs at the player controller, which turns
            // with a turn (simulator: the mouse look) - the head couldn't look at another slot.
            transform.rotation = _aroundPlayerRotation;

            // A carousel: let go while turning it, it spins on and slows down.
            if (!_isDragging)
            {
                _ringAngle += _spinVelocity * Time.deltaTime;
                _spinVelocity *= Mathf.Exp(-RingConstants.SpinDamping * Time.deltaTime);
                if (Mathf.Abs(_spinVelocity) < RingConstants.MinSpinVelocity)
                    _spinVelocity = 0f;
            }

            var putInTarget = _putInCandidates.FirstOrDefault(c => c != null)?.Target;
            FavoritesRingSlotView front = null;
            var frontDistance = float.MaxValue;
            foreach (var slot in _slots)
            {
                var angle = (slot.Angle + _ringAngle) * Mathf.Deg2Rad;
                slot.Root.transform.localPosition = new Vector3(Mathf.Sin(angle) * RingRadius, 0f,
                    -Mathf.Cos(angle) * RingRadius);
                var isHighlighted = slot == _hovered || slot == putInTarget;
                slot.Root.transform.localScale = Vector3.one * (isHighlighted ? RingConstants.HoverScale : 1f);
                if (slot.Item != null)
                    slot.Root.transform.localRotation = Quaternion.Euler(0f, Time.time * RingConstants.SpinSpeed, 0f);
                if (slot.Container != null && slot.TakenTime < 0f)
                    _slotViews.UpdateBodyCollisions(slot);

                var distance = GetHeadAngle(slot);
                if (distance < frontDistance)
                {
                    frontDistance = distance;
                    front = slot;
                }
            }

            if (front != _front)
            {
                _front = front;
                _frontSince = Time.time;
            }
            UpdateLabels();
        }

        /// <summary>
        /// Degrees between where the head looks and the slot (flat, seen from the head).
        /// </summary>
        private float GetHeadAngle(FavoritesRingSlotView slot)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null)
                return float.MaxValue;
            var look = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var toSlot = Vector3.ProjectOnPlane(slot.Root.transform.position - head.position, Vector3.up);
            return look.sqrMagnitude < 0.0001f || toSlot.sqrMagnitude < 0.0001f
                ? float.MaxValue
                : Vector3.Angle(look, toSlot);
        }

        private void UpdateHover()
        {
            FavoritesRingSlotView hovered = null;
            var hoveredSide = VrHandSide.Right;
            var best = RingConstants.HoverDistance * RingScale;
            foreach (var side in new[] { VrHandSide.Left, VrHandSide.Right })
            {
                if (!IsHandFree(side))
                    continue;
                var hand = _vrPlayerService.GetHandModelGo(side);
                if (hand == null)
                    continue;
                foreach (var slot in _slots)
                {
                    if (slot.Item == null || slot.TakenTime >= 0f)
                        continue;
                    var distance = Vector3.Distance(hand.transform.position, slot.Root.transform.position);
                    if (distance >= best)
                        continue;
                    best = distance;
                    hovered = slot;
                    hoveredSide = side;
                }
            }

            if (hovered != _hovered)
            {
                _hovered = hovered;
                if (hovered != null)
                    _hapticsService.Vibrate(hoveredSide, VrHapticPreset.Info);
                UpdateLabels();
            }

            // Weapons and items are grabbed by the backend itself (OnBeforeGrabbed).
            if (_hovered != null && _hovered.Kind == FavoriteSlotKind.Spell && !_isDragging &&
                _vrInput.GetButton(hoveredSide, VrButton.Grip).IsJustActivated)
                TakeSpell(_hovered, hoveredSide);
        }

        private bool IsHandFree(VrHandSide side)
        {
            return GetHeld(side) == null;
        }

        private GameObject GetHeld(VrHandSide side)
        {
            return side == VrHandSide.Left ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight;
        }

        /// <summary>
        /// The selected (front) slot always shows its name - its effect alone doesn't tell the spell; the slot a hand
        /// is near shows its name too. Both grow with the ring (twice and more).
        /// </summary>
        private void UpdateLabels()
        {
            ShowLabel(ref _frontLabel, "FavoriteSelectedName", _front?.Item != null ? _front : null);
            ShowLabel(ref _label, "FavoriteName", _hovered != _front ? _hovered : null);
        }

        private void ShowLabel(ref TextMeshPro label, string labelName, FavoritesRingSlotView slot)
        {
            if (slot == null)
            {
                if (label != null)
                    label.gameObject.SetActive(false);
                return;
            }

            if (label == null)
            {
                var labelGo = new GameObject(labelName);
                label = labelGo.AddComponent<TextMeshPro>();
                GothicText.Apply(label);
                label.fontSize = 12;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                labelGo.AddComponent<VRBillboard>();
            }

            label.gameObject.SetActive(true);
            label.text = GetSlotName(slot);
            label.transform.SetParent(slot.Root.transform, false);
            var height = slot.IsWeapon ? RingConstants.WeaponSize * 0.6f
                : RingConstants.SpellSize;
            label.transform.localPosition = Vector3.up * (height * 0.9f);
            // Its world size grows with the ring, not with the slot's hover scale.
            label.transform.localScale = Vector3.one * (RingConstants.LabelWorldScale * RingScale /
                                                        Mathf.Max(0.0001f, slot.Root.transform.lossyScale.x));
        }

        /// <summary>
        /// Like Gothic: the front spell shows its rune/scroll first, after PreviewSeconds its effect too. The rune
        /// stays, half transparent behind its effect (some effects are huge and hid which rune it is); a hand at it
        /// turns it around: the rune whole, the effect half. Weapons and items stay as they are.
        /// </summary>
        private void UpdateFrontPreview()
        {
            foreach (var slot in _slots)
            {
                if (slot.Kind != FavoriteSlotKind.Spell || slot.Item == null || slot.Root == null)
                    continue;
                var isEffect = slot == _front && !_isDragging &&
                               Time.time - _frontSince >= RingConstants.PreviewSeconds;
                var state = !isEffect ? 0 : slot == _hovered ? 2 : 1;
                if (state != slot.PreviewState)
                    _slotViews.SetPreview(slot, state);
            }
        }

        /// <summary>
        /// Trigger held + the hand moving sideways turns the carousel (it snaps to the nearest slot on release).
        /// A trigger click without moving on a spell or weapon unequips it, on an item takes it off the ring.
        /// </summary>
        private void UpdateDrag()
        {
            if (!_isTriggerDown)
            {
                foreach (var side in new[] { VrHandSide.Left, VrHandSide.Right })
                {
                    if (!IsHandFree(side) || !_vrInput.GetButton(side, VrButton.Trigger).IsJustActivated)
                        continue;
                    var hand = _vrPlayerService.GetHandModelGo(side);
                    if (hand == null || IsAboveHead(hand.transform.position))
                        continue;

                    _isTriggerDown = true;
                    _dragHand = side;
                    _dragStartLocal = transform.InverseTransformPoint(hand.transform.position);
                    _dragStartAngle = _ringAngle;
                    _dragStartHandAngle = GetHandAngle(_dragStartLocal);
                    _lastDragAngle = _ringAngle;
                    _pressedSlot = _hovered;
                    // A press grabs the spinning carousel - it stops, nothing is unequipped by that click.
                    _isSpinStoppedByPress = Mathf.Abs(_spinVelocity) >= RingConstants.StopSpinVelocity;
                    _spinVelocity = 0f;
                    return;
                }
                return;
            }

            var dragHand = _vrPlayerService.GetHandModelGo(_dragHand);
            var isHeld = _vrInput.GetButton(_dragHand, VrButton.Trigger).IsActive;
            if (dragHand == null || !isHeld)
            {
                _isTriggerDown = false;
                if (_isDragging)
                {
                    _isDragging = false;
                }
                else if (_pressedSlot != null && _pressedSlot == _hovered && !_isSpinStoppedByPress)
                {
                    Unequip(_pressedSlot, _dragHand);
                }
                _pressedSlot = null;
                return;
            }

            var local = transform.InverseTransformPoint(dragHand.transform.position);
            // The hand turns the ring around its axis - the slot under the hand goes with it, whichever way the player
            // faces (sideways in the ring's own space was mirrored behind him).
            var axisDistance = new Vector2(local.x, local.z).magnitude * RingScale;
            if (axisDistance < RingConstants.MinDragAxisDistance)
                return;
            var deltaAngle = Mathf.DeltaAngle(_dragStartHandAngle, GetHandAngle(local));
            if (!_isDragging && Mathf.Abs(deltaAngle) * Mathf.Deg2Rad * axisDistance >= RingConstants.DragStartDistance)
                _isDragging = true;
            if (!_isDragging)
                return;
            _ringAngle = _dragStartAngle + deltaAngle;
            if (Time.deltaTime > 0f)
                _spinVelocity = Mathf.Lerp(_spinVelocity,
                    Mathf.DeltaAngle(_lastDragAngle, _ringAngle) / Time.deltaTime, 0.5f);
            _lastDragAngle = _ringAngle;
        }

        private bool IsAboveHead(Vector3 position)
        {
            var head = Camera.main;
            return head != null &&
                   position.y > head.transform.position.y + RingConstants.GestureHeightAboveEyes;
        }

        /// <summary>
        /// Like Gothic's readied spell: the hand shows the spell's effect, no rune. It's an invisible rune attached to
        /// the hand - not grabbed (force-grabbing it without colliders flung the physics hand away), so the grip
        /// does nothing - with a VRRuneCaster casting with that hand only. The spell stays in the inventory (equipped),
        /// like Gothic's readied spell. A trigger click of the other hand puts it away (VRFavoritesRingSpell).
        /// </summary>
        private void TakeSpell(FavoritesRingSlotView slot, VrHandSide side)
        {
            var handModel = _vrPlayerService.GetHandModelGo(side);
            if (handModel == null || string.IsNullOrEmpty(slot.InstanceName))
                return;

            // The hand's spell taken before stayed with its own
            // VRRuneCaster - both charged and cast at once (a waiting transformation cast again on transforming back).
            foreach (var oldSpell in handModel.GetComponentsInChildren<VRFavoritesRingSpell>())
                oldSpell.Replace();

            var container = _vobService.CreateItem(slot.InstanceName, 1);
            var grabbable = container?.Go != null ? container.Go.GetComponentInChildren<VrInteractable>() : null;
            if (grabbable == null)
            {
                Logger.LogWarning($"[FavoritesRing] {slot.InstanceName}: no grabbable item created", LogCat.VR);
                return;
            }

            // Invisible, not grabbable, no physics - it rides on the hand.
            grabbable.IsGrabEnabled = false;
            foreach (var renderer in grabbable.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (var collider in grabbable.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            var rigidbody = grabbable.GetComponent<Rigidbody>();
            if (rigidbody != null)
                rigidbody.isKinematic = true;
            container.Go.transform.SetParent(handModel.transform, false);
            container.Go.transform.localPosition = Vector3.zero;
            grabbable.transform.localPosition = Vector3.zero;

            var caster = grabbable.gameObject.AddComponent<VRRuneCaster>();
            caster.RingHand = side;
            grabbable.gameObject.AddComponent<VRFavoritesRingSpell>().Init(slot.Item, side, container, caster);

            _hapticsService.Vibrate(side, VrHapticPreset.Success);
            Logger.Log($"[FavoritesRing] {GetSpellName(slot.Item)} taken into the {side} hand", LogCat.VR);
            Close("spell taken");
        }

        /// <summary>
        /// A hand grabs a ring weapon/item: full size again, out of the ring, physical - before the backend poses the
        /// hand (VRWeaponGripGrab picks the grip after this).
        /// </summary>
        private void OnBeforeGrabbed(VrGrabArgs args)
        {
            var grabbable = args.Interactable;
            var slot = _slots.FirstOrDefault(s => s.Grabbable == grabbable && s.Container != null);
            if (slot == null)
                return;

            slot.TakenTime = Time.time;
            var loader = slot.Container.Go.transform;
            loader.SetParent(slot.LoaderParent, true);
            loader.localScale = slot.LoaderScale;
            var rigidbody = grabbable.GetComponent<Rigidbody>();
            if (rigidbody != null)
                rigidbody.isKinematic = false;
            grabbable.IsDistanceGrabbable = true;
        }

        /// <summary>
        /// One of them out of the backpack into the hand: a loose world item like one taken out of the backpack. The
        /// grab counted the held one into the inventory (VRPlayerService.SetGrab) - one less in the backpack, so the
        /// count stays and an equipped weapon stays equipped (Gothic: drawn, still in the inventory). A stack keeps the
        /// ring open for more (UpdateTransfer).
        /// </summary>
        private void OnGrabbed(VrGrabArgs args)
        {
            var grabbable = args.Interactable;
            var slot = _slots.FirstOrDefault(s => s.Grabbable == grabbable && s.Container != null);
            if (slot == null)
                return;

            var container = slot.Container;
            slot.Container = null;
            _vobMeshCullingService.AddCullingEntry(container);
            // Not into the world's VOB list: a held item isn't part of it (PlayerService.PickUpItem/DropItem). Added
            // here, it depended on the backend's event order whether it was saved twice (world + inventory).
            _favoritesService.TakeOutOne(slot.Content);

            Logger.Log($"[FavoritesRing] {slot.Item.Name} taken into the hand ({slot.Amount} left)", LogCat.VR);
            if (slot.IsWeapon)
            {
                _takenWeapon = new FavoritesRingTakenWeapon
                {
                    Grabbable = grabbable,
                    Item = slot.Item,
                    InstanceName = slot.InstanceName,
                    Kind = slot.Kind
                };
            }
            if (slot.Kind is FavoriteSlotKind.Item or FavoriteSlotKind.Currency && slot.Amount > 0 &&
                args.IsHandOrDistance)
            {
                _transfer = new FavoritesRingTransfer
                {
                    Slot = slot,
                    Held = container,
                    Side = args.HandSide,
                    StartTime = Time.time
                };
                _hovered = slot;
                UpdateLabels();
                return;
            }
            Close("item taken");
        }

        /// <summary>
        /// True while a stack is being taken (the rest of the ring waits).
        /// A stack's first one is in the hand: the ring stays while the hand stays at the slot. Grip held for
        /// TransferDelaySeconds moves more of the stack into the hand - faster and faster. The held stack counts in
        /// the inventory already (held items do), so only its amount grows. Moving the hand away (or letting go) ends
        /// it.
        /// </summary>
        private bool UpdateTransfer()
        {
            if (_transfer == null)
                return false;

            var hand = _vrPlayerService.GetHandModelGo(_transfer.Side);
            var held = _transfer.Held;
            var isHeld = held?.Go != null && GetHeld(_transfer.Side) != null &&
                         GetHeld(_transfer.Side).GetComponentInParent<VobLoader>()?.Container == held;
            if (hand == null || !isHeld || _transfer.Slot.Root == null ||
                Vector3.Distance(hand.transform.position, _transfer.Slot.Root.transform.position) >
                RingConstants.TransferEndDistance * RingScale)
            {
                Logger.Log($"[FavoritesRing] {_transfer.Slot.Item.Name}: {held?.VobAs<IItem>()?.Amount ?? 0} taken " +
                           $"({_transfer.Slot.Amount} left)", LogCat.VR);
                Close("item taken");
                return true;
            }

            // The rest of the ring waits meanwhile.
            var heldTime = Time.time - _transfer.StartTime - RingConstants.TransferDelaySeconds;
            if (heldTime < 0f || _transfer.Slot.Amount <= 0)
                return true;

            var rate = Mathf.Min(RingConstants.TransferMaxRate,
                RingConstants.TransferStartRate * Mathf.Pow(2f, heldTime));
            _transfer.Carry += rate * Time.deltaTime;
            var count = Mathf.Min(Mathf.FloorToInt(_transfer.Carry), _transfer.Slot.Amount);
            if (count <= 0)
                return true;

            _transfer.Carry -= count;
            held.VobAs<IItem>().Amount += count;
            _transfer.Slot.Content.Amount -= count;
            _hapticsService.Vibrate(_transfer.Side, VrHapticPreset.Info);
            UpdateLabels();
            return true;
        }

        /// <summary>
        /// A grab that never finished: the item goes back (it was never taken out of the inventory).
        /// </summary>
        private void UpdateTakenTimeout()
        {
            foreach (var slot in _slots)
            {
                if (slot.Container == null || slot.TakenTime < 0f ||
                    Time.time - slot.TakenTime < RingConstants.TakeTimeoutSeconds || slot.Grabbable.IsHeld)
                    continue;
                Logger.LogWarning($"[FavoritesRing] {slot.InstanceName}: grab didn't finish - back into the ring",
                    LogCat.VR);
                Rebuild();
                return;
            }
        }

        private void Unequip(FavoritesRingSlotView slot, VrHandSide side)
        {
            if (!_favoritesService.TakeOff(slot.Content))
                return;
            _hapticsService.Vibrate(side, VrHapticPreset.Warning);
            Logger.Log($"[FavoritesRing] {GetSlotName(slot)} " +
                       (slot.Kind == FavoriteSlotKind.Item ? "taken off the ring" : "unequipped"), LogCat.VR);
            Rebuild();
        }

        /// <summary>
        /// While a hand holds an item: the slot it touches (any part of it) and what letting go there would do - the
        /// slot grows. Nothing happens until the grip is let go (OnReleased): grabbing something out of the backpack
        /// next to the ring doesn't throw it in.
        /// </summary>
        private void UpdatePutInCandidates()
        {
            foreach (var side in new[] { VrHandSide.Left, VrHandSide.Right })
            {
                var index = side == VrHandSide.Left ? 0 : 1;
                var held = GetHeld(side);
                var container = held != null ? held.GetComponentInParent<VobLoader>()?.Container : null;
                var vobItem = container?.VobAs<IItem>();
                if (vobItem == null)
                {
                    ExpirePutInCandidate(index);
                    continue;
                }

                var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
                var item = _vmCacheService.TryGetItemData(instanceName);
                var kind = _favoritesService.GetKind(item, instanceName);
                var target = _slots
                    .Where(s => s.TakenTime < 0f && IsNear(held, s, RingConstants.PutInDistance * RingScale))
                    .OrderBy(s => VrFavoritesService.IsSame(s.InstanceName, instanceName) ? 0
                        : s.Kind == kind ? 1 : 2)
                    .FirstOrDefault();
                if (target == null || !_favoritesService.TryPutIn(item, instanceName, kind, target.Content,
                        out var action, out var replaced))
                {
                    ExpirePutInCandidate(index);
                    continue;
                }

                if (_putInCandidates[index]?.Target != target)
                    _hapticsService.Vibrate(side, VrHapticPreset.Info);
                _putInCandidates[index] = new FavoritesRingPutInCandidate
                {
                    Container = container,
                    Item = item,
                    InstanceName = instanceName,
                    Kind = kind,
                    Target = target,
                    Action = action,
                    Replaced = replaced,
                    Time = Time.time,
                    Side = side
                };
            }
        }

        private void ExpirePutInCandidate(int index)
        {
            if (_putInCandidates[index] != null &&
                Time.time - _putInCandidates[index].Time > RingConstants.PutInGraceSeconds)
                _putInCandidates[index] = null;
        }

        /// <summary>
        /// A weapon taken out of the ring stays the equipped one while it's in the inventory (Gothic): in a hand, a
        /// holster or back in the backpack. Dropped into the world it comes off (PlayerService.DropItem). Back into the
        /// backpack it's equipped again if it wasn't any more. Back into the ring, the ring equipped it already.
        /// </summary>
        private void UpdateTakenWeapon()
        {
            var taken = _takenWeapon;
            if (taken == null || taken.ReleasedTime < 0f ||
                Time.time - taken.ReleasedTime < RingConstants.TakenWeaponCheckSeconds)
                return;
            if (taken.Grabbable != null && taken.Grabbable.IsHeld)
            {
                // In a hand again.
                taken.ReleasedTime = -1f;
                return;
            }

            _takenWeapon = null;
            var isGone = taken.Grabbable == null || taken.Grabbable.GetComponentInParent<VRBackpack>() != null;
            _favoritesService.KeepTakenWeaponEquipped(taken.Item, taken.InstanceName, taken.Kind, isGone);
        }

        /// <summary>
        /// Let go of an item at a slot (within PutInGraceSeconds): it goes in next frame - after the backend and
        /// VRVobItem handled the release (the held item left the inventory count).
        /// </summary>
        private void OnReleased(VrGrabArgs args)
        {
            var grabbable = args.Interactable;
            if (_takenWeapon != null && _takenWeapon.Grabbable == grabbable)
                _takenWeapon.ReleasedTime = Time.time;

            if (!_isOpen || !args.IsHandOrDistance || grabbable == null)
                return;
            var index = args.HandSide == VrHandSide.Left ? 0 : 1;
            var candidate = _putInCandidates[index];
            _putInCandidates[index] = null;
            if (candidate == null || Time.time - candidate.Time > RingConstants.PutInGraceSeconds ||
                grabbable.GetComponentInParent<VobLoader>()?.Container != candidate.Container)
                return;
            candidate.Grabbable = grabbable;
            _pendingPutIn = candidate;
        }

        /// <summary>
        /// The let go item into the backpack, as VrFavoritesService.TryPutIn decided when it touched the slot
        /// (VrFavoritesService.PutIn).
        /// </summary>
        private void UpdatePendingPutIn()
        {
            var putIn = _pendingPutIn;
            _pendingPutIn = null;
            if (putIn?.Container?.Go == null || !_slots.Contains(putIn.Target))
                return;
            if (putIn.Grabbable != null && putIn.Grabbable.IsHeld)
            {
                // Two hands or a holster still hold it: counted twice, and the held object destroyed.
                Logger.Log($"[FavoritesRing] {putIn.Item.Name} still held - not put in", LogCat.VR);
                return;
            }

            var vobItem = putIn.Container.VobAs<IItem>();
            _favoritesService.PutIn(putIn.Item, putIn.InstanceName, putIn.Kind, Mathf.Max(1, vobItem.Amount),
                putIn.Action, putIn.Replaced);
            _vobService.RemoveWorldItem(putIn.Container);

            _hapticsService.Vibrate(putIn.Side, VrHapticPreset.Success);
            var name = putIn.Kind == FavoriteSlotKind.Spell ? GetSpellName(putIn.Item) : putIn.Item.Name;
            var instead = putIn.Replaced != null ? $" instead of {putIn.Replaced.Name}" : "";
            Logger.Log($"[FavoritesRing] {name} {putIn.Action.ToLogText()}{instead}", LogCat.VR);
            Rebuild();
        }

        /// <summary>
        /// Any part of the held item at the slot - a sword is put in with its blade, its grip stays in the hand.
        /// </summary>
        private static bool IsNear(GameObject held, FavoritesRingSlotView slot, float distance)
        {
            if (slot.Root == null)
                return false;
            var position = slot.Root.transform.position;
            foreach (var collider in held.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger)
                    continue;
                if ((collider.ClosestPoint(position) - position).sqrMagnitude <= distance * distance)
                    return true;
            }
            return Vector3.Distance(held.transform.position, position) <= distance;
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.LoadGameStart.RemoveListener(OnLoadGameStart);
            LockTeleport(false);
            foreach (var hand in _hands)
            {
                hand.BeforeGrabbed -= OnBeforeGrabbed;
                hand.Grabbed -= OnGrabbed;
                hand.Released -= OnReleased;
            }
            ClearSlots();
            if (_label != null)
                Destroy(_label.gameObject);
            if (_frontLabel != null)
                Destroy(_frontLabel.gameObject);
        }
    }
}
