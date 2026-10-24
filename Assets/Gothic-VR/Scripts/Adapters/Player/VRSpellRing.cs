#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.UI;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Adapters.UI;
using Gothic.VR.Adapters.Vob.VobItem;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrSpellRing + the "spellRing" option (Immersion menu): Gothic's spell selection (hold the
    /// magic key, the equipped runes/scrolls circle the hero) in VR.
    /// - Open/close: both hands empty and raised above the head, both triggers held for _gestureSeconds.
    /// - A carousel in front of the player (between chest and eyes), at most _maxSpells (8) spells. The front one shows
    ///   its spell effect after _previewSeconds (like Gothic), the one a hand is near grows and shows its name.
    /// - Grip near a spell: it goes into that hand (ready to cast) and the ring closes.
    /// - Trigger held + moving the hand sideways turns the carousel; a trigger click (released without moving) on a
    ///   spell unequips it - it stays in the backpack without [E].
    /// - With fewer than 8 spells an empty slot shows: a rune/scroll held into it gets equipped (like armor: it goes
    ///   into the backpack with [E]). Taking it out of the backpack unequips it (PlayerInventoryOwner).
    /// The spells are the hero's equipped runes/scrolls (ITEM_KAT_RUNE) in equip order - saved with the inventory.
    /// </summary>
    public class VRSpellRing : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly SaveGameService _saveGameService;

        private const int _maxSpells = 8;
        private const float _gestureSeconds = 1f;
        // Meters the hands must be above the eyes for the gesture.
        private const float _gestureHeightAboveEyes = 0.1f;
        private const float _previewSeconds = 2f;
        private const float _ringDistance = 0.55f;
        private const float _ringRadius = 0.28f;
        private const float _ringHeightBelowEyes = 0.2f;
        private const float _itemSize = 0.09f;
        private const float _hoverScale = 1.4f;
        private const float _hoverDistance = 0.09f;
        private const float _equipDistance = 0.1f;
        private const float _dragStartDistance = 0.03f;
        private const float _snapSpeed = 360f;
        private const float _spinSpeed = 30f;
        private const float _labelWorldScale = 0.013f;
        private static readonly Color _emptySlotColor = new(0.6f, 0.6f, 0.6f, 1f);

        private readonly List<Slot> _slots = new();
        private bool _isOpen;
        private float _gestureTime;
        private bool _isGestureLatched;
        private int _gestureHapticStep;

        private float _ringAngle;
        private float _targetAngle;
        private bool _isDragging;
        private bool _isTriggerDown;
        private HVRHandSide _dragHand;
        private Vector3 _dragStartLocal;
        private float _dragStartAngle;
        private Slot _pressedSlot;

        private Slot _hovered;
        private Slot _front;
        private float _frontSince;
        private TextMeshPro _label;
        private Material _emptySlotMaterial;

        private class Slot
        {
            public ItemInstance Item; // null = the empty slot to equip into
            public string InstanceName;
            public GameObject Root;
            public GameObject Model;
            public GameObject Effect;
            public bool HasNoEffect;
            public float Angle;
        }

        public bool IsOpen => _isOpen;

        private void Awake()
        {
            this.Inject();
        }

        private bool IsEnabled()
        {
            // No Gothic.ini before a game was picked (profile menu).
            return _configService.Dev.EnableVrSpellRing && _configService.Gothic != null &&
                   _configService.Gothic.GetInt(VRConstants.IniNames.SpellRing, 1) == 1;
        }

        private void Update()
        {
            if (!IsEnabled() || _playerService.HeroContainer == null)
            {
                if (_isOpen)
                    Close("disabled");
                return;
            }

            UpdateGesture();
            if (!_isOpen)
                return;

            UpdateDrag();
            UpdateLayout();
            UpdateHover();
            UpdateFrontPreview();
            UpdateEquipSocket();
        }

        /// <summary>
        /// Both hands empty and above the eyes, both triggers held: after _gestureSeconds the ring opens/closes.
        /// The triggers have to be let go before the next toggle.
        /// </summary>
        private void UpdateGesture()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            var left = _vrPlayerService.GetHandModelGo(HVRHandSide.Left);
            var right = _vrPlayerService.GetHandModelGo(HVRHandSide.Right);
            var leftTrigger = HVRController.GetButtonState(HVRHandSide.Left, HVRButtons.Trigger).Active;
            var rightTrigger = HVRController.GetButtonState(HVRHandSide.Right, HVRButtons.Trigger).Active;

            if (!leftTrigger || !rightTrigger)
                _isGestureLatched = false;

            var isGesture = head != null && left != null && right != null && leftTrigger && rightTrigger &&
                            _vrPlayerService.GrabbedItemLeft == null && _vrPlayerService.GrabbedItemRight == null &&
                            left.transform.position.y > head.position.y + _gestureHeightAboveEyes &&
                            right.transform.position.y > head.position.y + _gestureHeightAboveEyes &&
                            !IsHeroBusy();
            if (!isGesture || _isGestureLatched)
            {
                _gestureTime = 0f;
                _gestureHapticStep = 0;
                return;
            }

            _gestureTime += Time.deltaTime;
            var step = Mathf.FloorToInt(_gestureTime / _gestureSeconds * 3f);
            if (step > _gestureHapticStep && step < 3)
            {
                _gestureHapticStep = step;
                _hapticsService.Vibrate(HVRHandSide.Left, VrHapticsService.VibrationType.Info);
                _hapticsService.Vibrate(HVRHandSide.Right, VrHapticsService.VibrationType.Info);
            }
            if (_gestureTime < _gestureSeconds)
                return;

            _isGestureLatched = true;
            _gestureTime = 0f;
            _gestureHapticStep = 0;
            _hapticsService.Vibrate(HVRHandSide.Left, VrHapticsService.VibrationType.Success);
            _hapticsService.Vibrate(HVRHandSide.Right, VrHapticsService.VibrationType.Success);
            if (_isOpen)
                Close("gesture");
            else
                Open(head);
        }

        private bool IsHeroBusy()
        {
            var bodyState = _playerService.HeroContainer.Props.BodyState;
            return bodyState is VmGothicEnums.BodyState.BsUnconscious or VmGothicEnums.BodyState.BsDead ||
                   _gameStateService.Dialogs.IsInDialog;
        }

        private void Open(Transform head)
        {
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
            transform.SetPositionAndRotation(
                head.position + forward * _ringDistance + Vector3.down * _ringHeightBelowEyes,
                Quaternion.LookRotation(forward, Vector3.up));

            _isOpen = true;
            _ringAngle = _targetAngle = 0f;
            Rebuild();
            Logger.Log($"[VRSpellRing] Opened with {_slots.Count(s => s.Item != null)} spell(s)", LogCat.VR);
        }

        private void Close(string reason)
        {
            _isOpen = false;
            _isDragging = false;
            _isTriggerDown = false;
            _hovered = null;
            _front = null;
            ClearSlots();
            if (_label != null)
                _label.gameObject.SetActive(false);
            Logger.Log($"[VRSpellRing] Closed ({reason})", LogCat.VR);
        }

        /// <summary>
        /// The hero's equipped spells (runes and scrolls) in equip order, plus the empty slot below the limit.
        /// </summary>
        private void Rebuild()
        {
            var previousFront = _front?.Item;
            ClearSlots();

            var vm = _gameStateService.GothicVm;
            var spells = _playerService.HeroContainer.Props.EquippedItems
                .Where(IsSpell)
                .Take(_maxSpells)
                .ToList();
            foreach (var spell in spells)
                _slots.Add(CreateSlot(spell, vm.GetSymbolByIndex(spell.Index)?.Name));
            if (spells.Count < _maxSpells)
                _slots.Add(CreateSlot(null, null));

            var step = 360f / _slots.Count;
            for (var i = 0; i < _slots.Count; i++)
                _slots[i].Angle = i * step;

            // Keep the same spell in front.
            var frontIndex = previousFront == null ? -1 : _slots.FindIndex(s => s.Item == previousFront);
            _ringAngle = _targetAngle = frontIndex < 0 ? 0f : -frontIndex * step;
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

        private static bool IsSpell(ItemInstance item)
        {
            return item != null && (VmGothicEnums.ItemFlags)item.MainFlag == VmGothicEnums.ItemFlags.ItemKatRune;
        }

        private Slot CreateSlot(ItemInstance item, string instanceName)
        {
            var root = new GameObject(item != null ? $"Spell {instanceName}" : "Spell (empty)");
            root.transform.SetParent(transform, false);
            var slot = new Slot { Item = item, InstanceName = instanceName, Root = root };

            if (item != null)
            {
                slot.Model = new GameObject("Model");
                slot.Model.transform.SetParent(root.transform, false);
                _vobService.CreateItemMesh(item.Index, slot.Model);
                foreach (var collider in slot.Model.GetComponentsInChildren<Collider>(true))
                    Destroy(collider);
                FitSize(slot.Model, _itemSize);
            }
            else
            {
                slot.Model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                slot.Model.name = "Empty";
                Destroy(slot.Model.GetComponent<Collider>());
                slot.Model.transform.SetParent(root.transform, false);
                slot.Model.transform.localScale = Vector3.one * (_itemSize * 0.5f);
                _emptySlotMaterial ??= CreateEmptySlotMaterial();
                slot.Model.GetComponent<MeshRenderer>().sharedMaterial = _emptySlotMaterial;
            }
            return slot;
        }

        private static Material CreateEmptySlotMaterial()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", _emptySlotColor);
            return material;
        }

        /// <summary>
        /// Runes and scrolls have different sizes - each fills _itemSize.
        /// </summary>
        private static void FitSize(GameObject model, float size)
        {
            if (!TryGetBounds(model, out var bounds))
                return;
            var largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest > 0.0001f)
                model.transform.localScale *= size / largest;

            // Centered on the slot (the mesh pivot isn't its center).
            if (TryGetBounds(model, out bounds))
                model.transform.position += model.transform.parent.position - bounds.center;
        }

        private static bool TryGetBounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return false;
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return true;
        }

        private void ClearSlots()
        {
            foreach (var slot in _slots)
            {
                if (slot.Root != null)
                    Destroy(slot.Root);
            }
            _slots.Clear();
        }

        /// <summary>
        /// The front spell (angle 0) is closest to the player; positive angles go to the right.
        /// </summary>
        private void UpdateLayout()
        {
            if (!_isDragging)
                _ringAngle = Mathf.MoveTowardsAngle(_ringAngle, _targetAngle, _snapSpeed * Time.deltaTime);

            Slot front = null;
            var frontDistance = float.MaxValue;
            foreach (var slot in _slots)
            {
                var angle = (slot.Angle + _ringAngle) * Mathf.Deg2Rad;
                slot.Root.transform.localPosition = new Vector3(Mathf.Sin(angle) * _ringRadius, 0f,
                    -Mathf.Cos(angle) * _ringRadius);
                slot.Root.transform.localScale = Vector3.one * (slot == _hovered ? _hoverScale : 1f);
                if (slot.Item != null)
                    slot.Root.transform.localRotation = Quaternion.Euler(0f, Time.time * _spinSpeed, 0f);

                var distance = Mathf.Abs(Mathf.DeltaAngle(0f, slot.Angle + _ringAngle));
                if (distance < frontDistance)
                {
                    frontDistance = distance;
                    front = slot;
                }
            }

            if (front != _front)
            {
                SetPreview(_front, false);
                _front = front;
                _frontSince = Time.time;
            }
        }

        private void UpdateHover()
        {
            Slot hovered = null;
            var hoveredSide = HVRHandSide.Right;
            var best = _hoverDistance;
            foreach (var side in new[] { HVRHandSide.Left, HVRHandSide.Right })
            {
                if (!IsHandFree(side))
                    continue;
                var hand = _vrPlayerService.GetHandModelGo(side);
                if (hand == null)
                    continue;
                foreach (var slot in _slots)
                {
                    if (slot.Item == null)
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
                {
                    SetPreview(hovered, false);
                    _hapticsService.Vibrate(hoveredSide, VrHapticsService.VibrationType.Info);
                }
                UpdateLabel();
            }

            if (_hovered != null && !_isDragging &&
                HVRController.GetButtonState(hoveredSide, HVRButtons.Grip).JustActivated)
                TakeSpell(_hovered, hoveredSide);
        }

        private bool IsHandFree(HVRHandSide side)
        {
            return GetHeld(side) == null;
        }

        private GameObject GetHeld(HVRHandSide side)
        {
            return side == HVRHandSide.Left ? _vrPlayerService.GrabbedItemLeft : _vrPlayerService.GrabbedItemRight;
        }

        private void UpdateLabel()
        {
            if (_hovered == null)
            {
                if (_label != null)
                    _label.gameObject.SetActive(false);
                return;
            }

            if (_label == null)
            {
                var labelGo = new GameObject("SpellName");
                _label = labelGo.AddComponent<TextMeshPro>();
                GothicText.Apply(_label);
                _label.fontSize = 12;
                _label.alignment = TextAlignmentOptions.Center;
                _label.textWrappingMode = TextWrappingModes.NoWrap;
                labelGo.AddComponent<VRBillboard>();
            }

            _label.gameObject.SetActive(true);
            _label.text = GetSpellName(_hovered.Item);
            _label.transform.SetParent(_hovered.Root.transform, false);
            _label.transform.localPosition = Vector3.up * (_itemSize * 0.9f);
            _label.transform.localScale = Vector3.one * (_labelWorldScale / Mathf.Max(0.0001f,
                _hovered.Root.transform.lossyScale.x));
        }

        /// <summary>
        /// Like Gothic: the front spell shows its rune/scroll first, after _previewSeconds its effect instead.
        /// </summary>
        private void UpdateFrontPreview()
        {
            if (_front == null || _front.Item == null || _front == _hovered || _isDragging)
                return;
            var isEffectShown = _front.Effect != null && _front.Effect.activeSelf;
            if (!isEffectShown && Time.time - _frontSince >= _previewSeconds)
                SetPreview(_front, true);
        }

        private void SetPreview(Slot slot, bool isEffect)
        {
            if (slot?.Item == null || slot.Root == null)
                return;

            if (isEffect && slot.Effect == null)
            {
                if (slot.HasNoEffect)
                    return;
                slot.Effect = CreateSpellEffect(slot.Item, slot.Root);
                if (slot.Effect == null)
                {
                    slot.HasNoEffect = true;
                    return;
                }
            }
            if (slot.Effect != null)
                slot.Effect.SetActive(isEffect);
            if (slot.Model != null)
                slot.Model.SetActive(!isEffect || slot.Effect == null);
        }

        private GameObject CreateSpellEffect(ItemInstance item, GameObject parent)
        {
            return VRSpellEffect.Create(item, parent, _gameStateService.GothicVm, _vmCacheService, _meshService);
        }

        /// <summary>
        /// Trigger held + the hand moving sideways turns the carousel (it snaps to the nearest spell on release).
        /// A trigger click without moving on a spell unequips it.
        /// </summary>
        private void UpdateDrag()
        {
            if (!_isTriggerDown)
            {
                foreach (var side in new[] { HVRHandSide.Left, HVRHandSide.Right })
                {
                    if (!IsHandFree(side) || !HVRController.GetButtonState(side, HVRButtons.Trigger).JustActivated)
                        continue;
                    var hand = _vrPlayerService.GetHandModelGo(side);
                    if (hand == null || IsAboveHead(hand.transform.position))
                        continue;

                    _isTriggerDown = true;
                    _dragHand = side;
                    _dragStartLocal = transform.InverseTransformPoint(hand.transform.position);
                    _dragStartAngle = _ringAngle;
                    _pressedSlot = _hovered;
                    return;
                }
                return;
            }

            var dragHand = _vrPlayerService.GetHandModelGo(_dragHand);
            var isHeld = HVRController.GetButtonState(_dragHand, HVRButtons.Trigger).Active;
            if (dragHand == null || !isHeld)
            {
                _isTriggerDown = false;
                if (_isDragging)
                {
                    _isDragging = false;
                    SnapToNearest();
                }
                else if (_pressedSlot != null && _pressedSlot == _hovered)
                {
                    Unequip(_pressedSlot, _dragHand);
                }
                _pressedSlot = null;
                return;
            }

            var local = transform.InverseTransformPoint(dragHand.transform.position);
            var deltaX = local.x - _dragStartLocal.x;
            if (!_isDragging && Mathf.Abs(deltaX) >= _dragStartDistance)
                _isDragging = true;
            if (_isDragging)
                _ringAngle = _dragStartAngle + deltaX / _ringRadius * Mathf.Rad2Deg;
        }

        private bool IsAboveHead(Vector3 position)
        {
            var head = Camera.main;
            return head != null && position.y > head.transform.position.y + _gestureHeightAboveEyes;
        }

        private void SnapToNearest()
        {
            if (_slots.Count == 0)
                return;
            var step = 360f / _slots.Count;
            _targetAngle = Mathf.Round(_ringAngle / step) * step;
        }

        /// <summary>
        /// Like Gothic's readied spell: the hand shows the spell's effect, no rune. It's an invisible rune attached to
        /// the hand - not grabbed (HVR force-grabbing it without colliders flung the physics hand away), so the grip does
        /// nothing - with a VRRuneCaster casting with that hand only. The spell stays in the inventory (equipped), like
        /// Gothic's readied spell. A trigger click of the other hand puts it away (VRSpellRingItem).
        /// </summary>
        private void TakeSpell(Slot slot, HVRHandSide side)
        {
            var handModel = _vrPlayerService.GetHandModelGo(side);
            if (handModel == null || string.IsNullOrEmpty(slot.InstanceName))
                return;

            var container = _vobService.CreateItem(new Item
            {
                Name = slot.InstanceName,
                Visual = new VisualMesh(),
                Instance = slot.InstanceName,
                Amount = 1
            });
            var grabbable = container?.Go != null ? container.Go.GetComponentInChildren<HVRGrabbable>() : null;
            if (grabbable == null)
            {
                Logger.LogWarning($"[VRSpellRing] {slot.InstanceName}: no grabbable item created", LogCat.VR);
                return;
            }

            // Invisible, not grabbable, no physics - it rides on the hand.
            grabbable.enabled = false;
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
            grabbable.gameObject.AddComponent<VRSpellRingItem>().Init(slot.Item, side, container, caster);

            _hapticsService.Vibrate(side, VrHapticsService.VibrationType.Success);
            Logger.Log($"[VRSpellRing] {GetSpellName(slot.Item)} taken into the {side} hand", LogCat.VR);
            Close("spell taken");
        }

        private void Unequip(Slot slot, HVRHandSide side)
        {
            if (slot.Item == null)
                return;
            _npcInventoryService.UnequipItemWithEffects(_playerService.HeroContainer.Instance, slot.Item);
            _hapticsService.Vibrate(side, VrHapticsService.VibrationType.Warning);
            Logger.Log($"[VRSpellRing] {GetSpellName(slot.Item)} unequipped", LogCat.VR);
            Rebuild();
        }

        /// <summary>
        /// A held rune/scroll put into the empty slot is equipped - like VRItemUser.Equip: out of the hand into the
        /// inventory, then equipped.
        /// </summary>
        private void UpdateEquipSocket()
        {
            var socket = _slots.FirstOrDefault(s => s.Item == null);
            if (socket == null)
                return;

            foreach (var side in new[] { HVRHandSide.Left, HVRHandSide.Right })
            {
                var held = GetHeld(side);
                if (held == null ||
                    Vector3.Distance(held.transform.position, socket.Root.transform.position) > _equipDistance)
                    continue;

                var container = held.GetComponentInParent<VobLoader>()?.Container;
                var vobItem = container?.VobAs<IItem>();
                if (vobItem == null)
                    continue;
                var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
                var item = _vmCacheService.TryGetItemData(instanceName);
                var hero = _playerService.HeroContainer.Instance;
                if (!IsSpell(item) || _npcInventoryService.IsEquipped(hero, instanceName))
                    continue;

                var grabbable = held.GetComponent<HVRGrabbable>();
                if (grabbable != null && grabbable.IsBeingHeld)
                    grabbable.ForceRelease();
                _saveGameService.UntrackLooseItem(container);
                _playerService.AddItem(instanceName, Mathf.Max(1, vobItem.Amount));
                _npcInventoryService.EquipItemWithEffects(hero, item);
                _vobService.RemoveWorldItem(container);

                _hapticsService.Vibrate(side, VrHapticsService.VibrationType.Success);
                Logger.Log($"[VRSpellRing] {GetSpellName(item)} equipped", LogCat.VR);
                Rebuild();
                return;
            }
        }

        private void OnDestroy()
        {
            ClearSlots();
            if (_label != null)
                Destroy(_label.gameObject);
        }
    }
}
#endif
