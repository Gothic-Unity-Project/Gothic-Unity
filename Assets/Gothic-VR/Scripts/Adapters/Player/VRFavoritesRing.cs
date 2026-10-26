using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.UI;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Adapters.UI;
using Gothic.VR.Adapters.Vob.VobItem;
using Gothic.VR.Services;
using Gothic.VR.Interaction;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrSpellRing + the "spellRing" option (Immersion menu): the favorites ring - Gothic's spell
    /// selection (hold the magic key, the equipped runes/scrolls circle the hero) in VR, plus the equipped weapons,
    /// favorite items and the money.
    /// - Open/close: both hands empty and raised above the head, both triggers held for _gestureSeconds. Loading a game
    ///   closes it.
    /// - A carousel in front of the player (between chest and eyes) - with EnableRingAroundPlayer around him at eye
    ///   level, the slot looked at selected: at most _maxSpells (8) spells; with
    ///   DeveloperConfig.EnableRingWeaponSlots the equipped melee weapon and bow/crossbow (Gothic's one of each); with
    ///   EnableRingItemSlots up to _maxItems (3) favorite items (a torch, a key, potions, food) and the trade currency
    ///   (G1 ore, G2 gold: TRADE_CURRENCY_INSTANCE). The front spell shows its effect after _previewSeconds (like
    ///   Gothic), the slot a hand is near grows and shows its name and how many are left in the backpack.
    /// - Grip near a spell: it goes into that hand (ready to cast) and the ring closes. Weapons and items are the real
    ///   items: grabbed like out of a socket (not from afar) - one of them. Grip held at the slot of a stack for
    ///   _transferDelaySeconds takes more and more of it into the hand; moving the hand away closes the ring.
    /// - Trigger held + moving the hand sideways turns the carousel; a trigger click (released without moving) on a
    ///   spell or weapon unequips it, on an item takes it off the ring - it stays in the backpack.
    /// - Let go of a held item at a slot (within _putInGraceSeconds after it touched it): a rune/scroll into the empty
    ///   slot is equipped (like armor: it goes into the backpack with [E]), a weapon too (onto the equipped one of its
    ///   kind it replaces it), anything else becomes a favorite item; at its own slot it just goes back into the
    ///   backpack.
    /// Only what is in the backpack shows - an equipped weapon in a holster, in a hand or dropped doesn't.
    /// The spells are the hero's equipped runes/scrolls (ITEM_KAT_RUNE) in equip order, the weapons his equipped
    /// ITEM_KAT_NF / ITEM_KAT_FF (also from the start, a script or a save) - saved with the inventory; the favorite
    /// items are NpcProperties.FavoriteItems, saved in UNITYSAVE.json.
    /// </summary>
    public class VRFavoritesRing : MonoBehaviour
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
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly DynamicMaterialService _dynamicMaterialService;

        private const int _maxSpells = 8;
        private const int _maxWeapons = 2;
        private const int _maxItems = 3;
        private const string _g1Currency = "ItMiNugget";
        private const string _focusNameCanvas = "NameCanvas";
        private const float _gestureSeconds = 1f;
        // Meters the hands must be above the eyes for the gesture.
        private const float _gestureHeightAboveEyes = 0.1f;
        private const float _previewSeconds = 1f;
        private const float _ringDistance = 0.55f;
        private const float _ringRadius = 0.28f;
        private const float _ringHeightBelowEyes = 0.2f;
        private const float _spellSize = 0.09f;
        private const float _spellModelTilt = -90f;
        // DeveloperConfig.EnableSpellEffectGothicMotion: the effect's radius in the slot, a share of the rune's size.
        private const float _effectFitShare = 0.7f;
        // DeveloperConfig.EnableRingSpellPreviewAlpha: the rune behind its effect / the effect while a hand is at it.
        private const float _previewModelAlpha = 0.5f;
        private const float _previewEffectHoverAlpha = 0.5f;
        private static readonly int _baseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int _colorProperty = Shader.PropertyToID("_Color");
        // Weapons stand upright (blade up) and are longer than the runes.
        private const float _weaponSize = 0.16f;
        private const float _itemSize = 0.1f;
        private const float _hoverScale = 1.4f;
        private const float _hoverDistance = 0.09f;
        // A held item closer than this to a slot (any part of it - a sword's blade, not its grip in the hand).
        private const float _putInDistance = 0.1f;
        // Let go this long after the held item touched a slot still puts it in.
        private const float _putInGraceSeconds = 0.2f;
        private const float _dragStartDistance = 0.03f;
        private const float _snapSpeed = 360f;
        private const float _spinSpeed = 30f;
        private const float _labelWorldScale = 0.013f;
        // DeveloperConfig.EnableRingAroundPlayer: the ring circles the player at eye level, 2.2 times the size (radius
        // ~0.6 m) - the slot looked at is the selected one.
        private const float _aroundPlayerScale = 2.2f;
        // An item grabbed out of the ring that no hand ended up holding after this goes back.
        private const float _takeTimeoutSeconds = 1f;
        // Stacks: grip held at the slot this long takes more - first _transferStartRate per second, doubling every
        // second. The hand this far from the slot ends it.
        private const float _transferDelaySeconds = 1.5f;
        private const float _transferStartRate = 2f;
        private const float _transferMaxRate = 500f;
        private const float _transferEndDistance = 0.15f;
        // A weapon taken out of the ring and let go: after this it's in the backpack, a holster or on the ground.
        private const float _takenWeaponCheckSeconds = 0.5f;
        private static readonly Color _emptySlotColor = new(0.6f, 0.6f, 0.6f, 1f);

        private readonly List<Slot> _slots = new();
        // The direct grabbers of both hands (IVrRig.HandGrabbers).
        private readonly List<IVrGrabber> _hands = new();
        private IVrRig _rig;
        private bool _isTeleportLocked;
        private bool _isOpen;
        private float _gestureTime;
        private bool _isGestureLatched;
        private int _gestureHapticStep;

        private float _ringAngle;
        private float _targetAngle;
        private bool _isDragging;
        private bool _isTriggerDown;
        private VrHandSide _dragHand;
        private Vector3 _dragStartLocal;
        private float _dragStartAngle;
        private Slot _pressedSlot;

        private Slot _hovered;
        private Slot _front;
        private float _frontSince;
        private TextMeshPro _label;
        // The selected (front) slot's name - always shown.
        private TextMeshPro _frontLabel;
        private Material _emptySlotMaterial;
        private string _lastRejection;

        // Per hand (0 left, 1 right): the slot the held item touched last, and when.
        private readonly PutInCandidate[] _putInCandidates = new PutInCandidate[2];
        private PutInCandidate _pendingPutIn;
        private Transfer _transfer;
        private TakenWeapon _takenWeapon;

        private enum SlotKind
        {
            Empty,
            Spell,
            Melee,
            Ranged,
            Item,
            Currency
        }

        private class Slot
        {
            public SlotKind Kind;
            public ItemInstance Item; // null = the empty slot to put into
            public string InstanceName;
            // How many are in the backpack (not in a hand or holster).
            public int Amount;
            public GameObject Root;
            public GameObject Model;
            // VRFocus's hover name of a real item.
            public GameObject FocusName;
            public GameObject Effect;
            public bool HasNoEffect;
            // DeveloperConfig.EnableRingSpellPreviewAlpha: the materials of its effect with their own alpha, and the
            // look shown (-1 none yet, 0 rune only, 1 effect in front, 2 effect while hovered).
            public List<(Material Material, Color Color)> EffectMaterials;
            public int PreviewState = -1;
            public float Angle;

            // Weapons and items: the real item, shrunk into the slot until a hand grabs it.
            public VobContainer Container;
            public VrInteractable Grabbable;
            public Transform LoaderParent;
            public Vector3 LoaderScale;
            public int IgnoredColliderCount;
            public float TakenTime = -1f;

            public bool IsReal => Kind is SlotKind.Melee or SlotKind.Ranged or SlotKind.Item or SlotKind.Currency;
        }

        private class PutInCandidate
        {
            public VobContainer Container;
            public ItemInstance Item;
            public string InstanceName;
            public SlotKind Kind;
            public Slot Target;
            public string Action;
            public ItemInstance Replaced;
            public float Time;
            public VrHandSide Side;
        }

        /// <summary>
        /// DeveloperConfig.EnableRingWeaponLeavesOnDrop: a weapon taken out of the ring, until it was let go.
        /// </summary>
        private class TakenWeapon
        {
            public VrInteractable Grabbable;
            public ItemInstance Item;
            public string InstanceName;
            public SlotKind Kind;
            public float ReleasedTime = -1f;
        }

        private class Transfer
        {
            public Slot Slot;
            public VobContainer Held;
            public VrHandSide Side;
            public float StartTime;
            public float Carry;
        }

        public bool IsOpen => _isOpen;

        private void Awake()
        {
            this.Inject();
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);
        }

        private void Start()
        {
            _rig = _vrPlayerService.VRContextInteractionService?.Rig;
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
            // No Gothic.ini before a game was picked (profile menu).
            return _configService.Dev.EnableVrSpellRing && _configService.Gothic != null &&
                   _configService.Gothic.GetInt(VRConstants.IniNames.SpellRing, 1) == 1;
        }

        private bool IsWeaponSlotsEnabled => _configService.Dev.EnableRingWeaponSlots;
        private bool IsItemSlotsEnabled => _configService.Dev.EnableRingItemSlots;

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
        /// Both hands empty and above the eyes, both triggers held: after _gestureSeconds the ring opens/closes.
        /// The triggers have to be let go before the next toggle.
        /// </summary>
        private void UpdateGesture()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            var left = _vrPlayerService.GetHandModelGo(VrHandSide.Left);
            var right = _vrPlayerService.GetHandModelGo(VrHandSide.Right);
            var leftTrigger = VrControls.GetButton(VrHandSide.Left, VrButton.Trigger).IsActive;
            var rightTrigger = VrControls.GetButton(VrHandSide.Right, VrButton.Trigger).IsActive;

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
                _hapticsService.Vibrate(VrHandSide.Left, VrHapticPreset.Info);
                _hapticsService.Vibrate(VrHandSide.Right, VrHapticPreset.Info);
            }
            if (_gestureTime < _gestureSeconds)
                return;

            _isGestureLatched = true;
            _gestureTime = 0f;
            _gestureHapticStep = 0;
            _hapticsService.Vibrate(VrHandSide.Left, VrHapticPreset.Success);
            _hapticsService.Vibrate(VrHandSide.Right, VrHapticPreset.Success);
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
            if (IsAroundPlayer)
            {
                // The player stands in its middle, a little below the eyes (at eye level it hid the view); turned
                // around, the first slot is in front of him.
                transform.SetPositionAndRotation(head.position + Vector3.down * _aroundPlayerBelowEyes,
                    Quaternion.LookRotation(-forward, Vector3.up));
                _aroundPlayerRotation = transform.rotation;
            }
            else
            {
                transform.SetPositionAndRotation(
                    head.position + forward * _ringDistance + Vector3.down * _ringHeightBelowEyes,
                    Quaternion.LookRotation(forward, Vector3.up));
            }
            transform.localScale = Vector3.one * RingScale;

            _isOpen = true;
            _ringAngle = _targetAngle = 0f;
            LockTeleport(true);
            Rebuild();
            Logger.Log($"[FavoritesRing] Opened with {_slots.Count(s => s.Kind == SlotKind.Spell)} spell(s), " +
                       $"{_slots.Count(s => s.Kind is SlotKind.Melee or SlotKind.Ranged)} weapon(s), " +
                       $"{_slots.Count(s => s.Kind is SlotKind.Item or SlotKind.Currency)} item(s)", LogCat.VR);
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
        /// The hero's equipped spells (runes and scrolls) in equip order, his equipped melee weapon and bow/crossbow,
        /// his favorite items and money - those in the backpack - plus the empty slot while something is missing.
        /// </summary>
        private void Rebuild()
        {
            var previousFront = _front?.InstanceName;
            ClearSlots();

            var vm = _gameStateService.GothicVm;
            var hero = _playerService.HeroContainer;
            var equipped = hero.Props.EquippedItems;
            var spells = equipped.Where(i => GetKind(i) == SlotKind.Spell).Take(_maxSpells).ToList();
            foreach (var spell in spells)
                _slots.Add(CreateSlot(SlotKind.Spell, spell, vm.GetSymbolByIndex(spell.Index)?.Name));

            var weaponCount = 0;
            if (IsWeaponSlotsEnabled)
            {
                foreach (var kind in new[] { SlotKind.Melee, SlotKind.Ranged })
                {
                    // The last equipped one (Gothic: one of each). Several were left equipped before (the ring didn't
                    // take the old one off) - the others come off now.
                    var weapon = equipped.LastOrDefault(i => GetKind(i) == kind);
                    foreach (var extra in equipped.Where(i => GetKind(i) == kind && i != weapon).ToList())
                    {
                        Logger.Log($"[FavoritesRing] {extra.Name} was equipped next to {weapon?.Name} - taken off",
                            LogCat.VR);
                        _npcInventoryService.UnequipItemWithEffects(hero.Instance, extra);
                    }
                    var instanceName = weapon != null ? vm.GetSymbolByIndex(weapon.Index)?.Name : null;
                    if (instanceName == null || !IsInBackpack(instanceName))
                        continue;
                    _slots.Add(CreateSlot(kind, weapon, instanceName));
                    weaponCount++;
                }
            }

            var itemCount = 0;
            if (IsItemSlotsEnabled)
            {
                var currency = GetCurrency();
                // Used up, sold or dropped: no longer a favorite. The money has its own slot.
                hero.Props.FavoriteItems.RemoveAll(name => GetAmount(name) <= 0 || IsSame(name, currency));
                foreach (var instanceName in hero.Props.FavoriteItems.Take(_maxItems))
                {
                    var item = _vmCacheService.TryGetItemData(instanceName);
                    if (item == null)
                        continue;
                    itemCount++;
                    if (IsInBackpack(instanceName))
                        _slots.Add(CreateSlot(SlotKind.Item, item, instanceName));
                }

                var currencyItem = _vmCacheService.TryGetItemData(currency);
                if (currencyItem != null && GetBackpackAmount(currency) > 0)
                    _slots.Add(CreateSlot(SlotKind.Currency, currencyItem, currency));
            }

            if (spells.Count < _maxSpells || IsWeaponSlotsEnabled && weaponCount < _maxWeapons ||
                IsItemSlotsEnabled && itemCount < _maxItems)
                _slots.Add(CreateSlot(SlotKind.Empty, null, null));

            var step = 360f / _slots.Count;
            for (var i = 0; i < _slots.Count; i++)
                _slots[i].Angle = i * step;

            // Keep the same slot in front.
            var frontIndex = previousFront == null ? -1 : _slots.FindIndex(s => s.InstanceName == previousFront);
            _ringAngle = _targetAngle = frontIndex < 0 ? 0f : -frontIndex * step;
            _front = null;
            _hovered = null;
        }

        /// <summary>
        /// An equipped weapon taken out (held, in a holster, dropped) isn't in the backpack - showing it made a copy
        /// of it with every grab. Rockefeller's start (Mroczne Tajemnice) equips weapons he doesn't carry.
        /// </summary>
        private bool IsInBackpack(string instanceName)
        {
            var amount = GetBackpackAmount(instanceName);
            if (amount > 0)
                return true;
            Logger.Log($"[FavoritesRing] {instanceName} isn't in the backpack (inventory {GetAmount(instanceName)}, " +
                       $"backpack {amount}: held, in a holster or gone) - not shown", LogCat.VR);
            return false;
        }

        /// <summary>
        /// G2 (and mods): TRADE_CURRENCY_INSTANCE; G1 has none - ore (like TradeService).
        /// </summary>
        private string GetCurrency()
        {
            var currency = _gameStateService.GothicVm?.GetSymbolByName("TRADE_CURRENCY_INSTANCE")?.GetString(0);
            return string.IsNullOrEmpty(currency) ? _g1Currency : currency;
        }

        /// <summary>
        /// Runes are all called "Runa"/"Rune" - the spell is their description, like in the backpack.
        /// </summary>
        public static string GetSpellName(ItemInstance item)
        {
            return string.IsNullOrEmpty(item?.Description) ? item?.Name : item.Description;
        }

        private static string GetSlotName(Slot slot)
        {
            var name = slot.Kind == SlotKind.Spell ? GetSpellName(slot.Item) : slot.Item?.Name;
            return slot.Amount > 1 ? $"{name} ({slot.Amount})" : name;
        }

        /// <summary>
        /// Which ring slot an item belongs into: runes/scrolls, melee weapons (ITEM_KAT_NF), bows/crossbows
        /// (ITEM_KAT_FF), the money or any other item.
        /// </summary>
        private SlotKind GetKind(ItemInstance item, string instanceName = null)
        {
            if (item == null)
                return SlotKind.Empty;
            if (instanceName != null && IsSame(instanceName, GetCurrency()))
                return SlotKind.Currency;
            return (VmGothicEnums.ItemFlags)item.MainFlag switch
            {
                VmGothicEnums.ItemFlags.ItemKatRune => SlotKind.Spell,
                VmGothicEnums.ItemFlags.ItemKatNf => SlotKind.Melee,
                VmGothicEnums.ItemFlags.ItemKatFf => SlotKind.Ranged,
                _ => SlotKind.Item
            };
        }

        /// <summary>
        /// How many the hero has - held ones and those in a holster count too, like everywhere in VR.
        /// </summary>
        private int GetAmount(string instanceName)
        {
            var item = _vmCacheService.TryGetItemData(instanceName);
            if (item == null)
                return 0;
            var category = ((VmGothicEnums.ItemFlags)item.MainFlag).ToInventoryCategory();
            return _playerService.GetInventory(category)
                .Where(i => i.Name.EqualsIgnoreCase(instanceName))
                .Sum(i => i.Amount);
        }

        /// <summary>
        /// How many are in the backpack: the inventory without those in the hands and holsters (IsHeldByPlayer). The
        /// backpack's own sockets are player sockets too (the page shown) - those are in it.
        /// </summary>
        private int GetBackpackAmount(string instanceName)
        {
            var amount = GetAmount(instanceName);
            foreach (var container in _multiTypeCacheService.VobCache)
            {
                if (container is not { IsHeldByPlayer: true } || container.Vob is not IItem item ||
                    container.Go != null && container.Go.GetComponentInParent<VRBackpack>() != null)
                    continue;
                var name = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
                if (IsSame(name, instanceName))
                    amount -= Mathf.Max(1, item.Amount);
            }
            return amount;
        }

        private Slot CreateSlot(SlotKind kind, ItemInstance item, string instanceName)
        {
            var root = new GameObject(item != null ? $"{kind} {instanceName}" : "Favorite (empty)");
            root.transform.SetParent(transform, false);
            var slot = new Slot
            {
                Kind = kind,
                Item = item,
                InstanceName = instanceName,
                Amount = instanceName != null ? GetBackpackAmount(instanceName) : 0,
                Root = root
            };

            if (item == null)
            {
                slot.Model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                slot.Model.name = "Empty";
                Destroy(slot.Model.GetComponent<Collider>());
                slot.Model.transform.SetParent(root.transform, false);
                slot.Model.transform.localScale = Vector3.one * (_spellSize * 0.5f);
                _emptySlotMaterial ??= CreateEmptySlotMaterial();
                slot.Model.GetComponent<MeshRenderer>().sharedMaterial = _emptySlotMaterial;
                return slot;
            }

            if (slot.IsReal && TryCreateRealItem(slot))
                return slot;

            // Spells (an invisible rune goes into the hand, VRFavoritesRingSpell) - and a real item that failed.
            slot.Model = new GameObject("Model");
            slot.Model.transform.SetParent(root.transform, false);
            _vobService.CreateItemMesh(item.Index, slot.Model);
            foreach (var collider in slot.Model.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            // Runes and scrolls lie flat - stood up, their picture faces the player.
            slot.Model.transform.localRotation = Quaternion.Euler(_spellModelTilt, 0f, 0f);
            FitSize(slot.Model, _spellSize);
            return slot;
        }

        /// <summary>
        /// The weapon/item itself, kinematic and shrunk into the slot: a hand grabs it like out of a backpack socket
        /// (the backend's own grab, at the grip for weapons) - not from afar, without VRFocus's name (the ring shows its
        /// own).
        /// It doesn't touch the hands or the hero's body until grabbed.
        /// </summary>
        private bool TryCreateRealItem(Slot slot)
        {
            var container = CreateItem(slot.InstanceName);
            var grabbable = container?.Go != null ? container.Go.GetComponentInChildren<VrInteractable>() : null;
            if (grabbable == null)
            {
                if (container != null)
                    _vobService.RemoveWorldItem(container);
                Logger.LogWarning($"[FavoritesRing] {slot.InstanceName}: no grabbable item created", LogCat.VR);
                return false;
            }

            var rigidbody = grabbable.GetComponent<Rigidbody>();
            if (rigidbody != null)
                rigidbody.isKinematic = true;
            grabbable.IsDistanceGrabbable = false;
            slot.FocusName = container.Go.FindChildRecursively(_focusNameCanvas);

            var loader = container.Go.transform;
            slot.LoaderParent = loader.parent;
            slot.LoaderScale = loader.localScale;
            loader.SetParent(slot.Root.transform, false);
            loader.localPosition = Vector3.zero;
            loader.localRotation = Quaternion.identity;
            if (slot.Kind is SlotKind.Melee or SlotKind.Ranged)
            {
                StandUpright(container.Go);
                FitSize(container.Go, _weaponSize);
            }
            else
            {
                FitSize(container.Go, _itemSize);
            }

            slot.Container = container;
            slot.Grabbable = grabbable;
            slot.Model = container.Go;
            UpdateBodyCollisions(slot);
            return true;
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

        /// <summary>
        /// The hands' and the hero's solid colliders ignore a ring item - also the colliders a weapon builds after
        /// being created (its fitted hitboxes). Once grabbed the backend handles the hands (and restores them on
        /// release), VRVobItem the body.
        /// </summary>
        private void UpdateBodyCollisions(Slot slot)
        {
            var itemColliders = slot.Container.Go.GetComponentsInChildren<Collider>(true);
            if (itemColliders.Length == slot.IgnoredColliderCount)
                return;
            slot.IgnoredColliderCount = itemColliders.Length;

            if (_rig == null)
                return;
            var bodyColliders = new List<Collider> { _rig.CharacterController };
            // Not the hands' triggers - the backend finds what a hand can grab with them.
            bodyColliders.AddRange(_rig.GetHandColliders(VrHandSide.Left));
            bodyColliders.AddRange(_rig.GetHandColliders(VrHandSide.Right));
            foreach (var bodyCollider in bodyColliders)
            {
                if (bodyCollider == null)
                    continue;
                foreach (var itemCollider in itemColliders)
                    Physics.IgnoreCollision(bodyCollider, itemCollider, true);
            }
        }

        private static Material CreateEmptySlotMaterial()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", _emptySlotColor);
            return material;
        }

        /// <summary>
        /// A weapon's longest axis points up, its grip (the mesh's origin) down - spinning, it doesn't sweep through
        /// the neighbouring slots.
        /// </summary>
        private static void StandUpright(GameObject model)
        {
            model.transform.rotation = Quaternion.identity;
            if (!TryGetBounds(model, out var bounds))
                return;

            var size = bounds.size;
            var rotation = size.x >= size.y && size.x >= size.z ? Quaternion.Euler(0f, 0f, 90f)
                : size.z >= size.y ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
            model.transform.localRotation = rotation;

            if (TryGetBounds(model, out bounds) && model.transform.position.y > bounds.center.y)
                model.transform.localRotation = Quaternion.Euler(180f, 0f, 0f) * rotation;
        }

        /// <summary>
        /// Runes, scrolls, weapons and items have different sizes - each fills its slot size.
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

        /// <summary>
        /// The meshes only - a weapon's swing trail or a bow's string reach to the world's origin.
        /// </summary>
        private static bool TryGetBounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            var renderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer or SkinnedMeshRenderer)
                .ToArray();
            if (renderers.Length == 0)
                return false;
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return true;
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
            {
                if (slot.Container != null)
                    _vobService.RemoveWorldItem(slot.Container);
                if (slot.Root != null)
                    Destroy(slot.Root);
            }
            _slots.Clear();
        }

        private bool IsAroundPlayer => _configService.Dev.EnableRingAroundPlayer;
        private Quaternion _aroundPlayerRotation = Quaternion.identity;
        private const float _aroundPlayerBelowEyes = 0.25f;
        private float RingScale => IsAroundPlayer ? _aroundPlayerScale : 1f;

        // DeveloperConfig.EnableRingZoom: the ring's radius - the slots come closer or move away, keeping their size
        // (scaling the whole ring grew the slots too).
        private float RingRadius => _ringRadius * (_configService.Dev.EnableRingZoom ? _ringZoom : 1f);

        // DeveloperConfig.EnableRingZoom: the ring's radius, changed with the right stick (simulator: mouse wheel) like
        // the menu's distance - kept for the session.
        private static float _ringZoom = 1f;
        private const float _minRingZoom = 0.5f;
        private const float _maxRingZoom = 2f;
        private const float _ringZoomSpeed = 1f;
        private const float _ringZoomPerWheelNotch = 0.05f;
        private const float _zoomStickDeadZone = 0.2f;

        // DeveloperConfig.EnableRingFreeSpin: a carousel - let go while turning it, it spins on and slows down; a
        // trigger press stops it. Degrees per second, the drag's last speed.
        private float _spinVelocity;
        private float _lastDragAngle;
        private bool _isSpinStoppedByPress;
        private float _dragStartHandAngle;
        private const float _spinDamping = 1.5f;
        private const float _minSpinVelocity = 5f;
        private const float _stopSpinVelocity = 20f;
        private const float _minDragAxisDistance = 0.1f;

        private bool IsFreeSpin => _configService.Dev.EnableRingFreeSpin && IsAroundPlayer;

        /// <summary>
        /// DeveloperConfig.EnableRingZoom: the right stick up/down (mouse wheel) makes the ring bigger/smaller - it moves
        /// away from or towards the eyes with its slots.
        /// </summary>
        private void UpdateZoom()
        {
            if (!_configService.Dev.EnableRingZoom)
                return;

            var change = 0f;
            if (_vrPlayerService.IsSimulator)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                // A notch is 120 or 1 depending on the Input System's scroll setting - the direction counts.
                var scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
                if (!Mathf.Approximately(scroll, 0f))
                    change = Mathf.Sign(scroll) * _ringZoomPerWheelNotch;
            }
            else
            {
                var stick = VrControls.GetThumbstick(VrHandSide.Right);
                if (Mathf.Abs(stick.y) >= _zoomStickDeadZone)
                    change = stick.y * _ringZoomSpeed * Time.deltaTime;
            }
            if (Mathf.Approximately(change, 0f))
                return;

            _ringZoom = Mathf.Clamp(_ringZoom + change, _minRingZoom, _maxRingZoom);
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
        /// The front slot (angle 0) is closest to the player; positive angles go to the right. Around the player
        /// (EnableRingAroundPlayer) the selected slot is the one the head looks at - turning the head selects too.
        /// </summary>
        private void UpdateLayout()
        {
            // Around the player it keeps its direction in the world: it hangs at the player controller, which turns
            // with a turn (simulator: the mouse look) - the head couldn't look at another slot.
            if (IsAroundPlayer)
                transform.rotation = _aroundPlayerRotation;

            if (!_isDragging && IsFreeSpin)
            {
                _ringAngle += _spinVelocity * Time.deltaTime;
                _spinVelocity *= Mathf.Exp(-_spinDamping * Time.deltaTime);
                if (Mathf.Abs(_spinVelocity) < _minSpinVelocity)
                    _spinVelocity = 0f;
            }
            else if (!_isDragging)
            {
                _ringAngle = Mathf.MoveTowardsAngle(_ringAngle, _targetAngle, _snapSpeed * Time.deltaTime);
            }

            var putInTarget = _putInCandidates.FirstOrDefault(c => c != null)?.Target;
            Slot front = null;
            var frontDistance = float.MaxValue;
            foreach (var slot in _slots)
            {
                var angle = (slot.Angle + _ringAngle) * Mathf.Deg2Rad;
                slot.Root.transform.localPosition = new Vector3(Mathf.Sin(angle) * RingRadius, 0f,
                    -Mathf.Cos(angle) * RingRadius);
                var isHighlighted = slot == _hovered || slot == putInTarget;
                slot.Root.transform.localScale = Vector3.one * (isHighlighted ? _hoverScale : 1f);
                if (slot.Item != null)
                    slot.Root.transform.localRotation = Quaternion.Euler(0f, Time.time * _spinSpeed, 0f);
                if (slot.Container != null && slot.TakenTime < 0f)
                    UpdateBodyCollisions(slot);

                var distance = IsAroundPlayer
                    ? GetHeadAngle(slot)
                    : Mathf.Abs(Mathf.DeltaAngle(0f, slot.Angle + _ringAngle));
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
        private float GetHeadAngle(Slot slot)
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
            Slot hovered = null;
            var hoveredSide = VrHandSide.Right;
            var best = _hoverDistance * RingScale;
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
            if (_hovered != null && _hovered.Kind == SlotKind.Spell && !_isDragging &&
                VrControls.GetButton(hoveredSide, VrButton.Grip).IsJustActivated)
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
        /// is near shows its name too. Both grow with the ring (EnableRingAroundPlayer: twice and more).
        /// </summary>
        private void UpdateLabels()
        {
            ShowLabel(ref _frontLabel, "FavoriteSelectedName", _front?.Item != null ? _front : null);
            ShowLabel(ref _label, "FavoriteName", _hovered != _front ? _hovered : null);
        }

        private void ShowLabel(ref TextMeshPro label, string labelName, Slot slot)
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
            var height = slot.Kind is SlotKind.Melee or SlotKind.Ranged ? _weaponSize * 0.6f : _spellSize;
            label.transform.localPosition = Vector3.up * (height * 0.9f);
            // Its world size grows with the ring, not with the slot's hover scale.
            label.transform.localScale = Vector3.one * (_labelWorldScale * RingScale /
                                                        Mathf.Max(0.0001f, slot.Root.transform.lossyScale.x));
        }

        /// <summary>
        /// Like Gothic: the front spell shows its rune/scroll first, after _previewSeconds its effect too. With
        /// DeveloperConfig.EnableRingSpellPreviewAlpha the rune stays, half transparent behind its effect (some
        /// effects are huge and hid which rune it is); a hand at it turns it around: the rune whole, the effect half.
        /// Without the flag the effect replaces the rune. Weapons and items stay as they are.
        /// </summary>
        private void UpdateFrontPreview()
        {
            foreach (var slot in _slots)
            {
                if (slot.Kind != SlotKind.Spell || slot.Item == null || slot.Root == null)
                    continue;
                var isEffect = slot == _front && !_isDragging && Time.time - _frontSince >= _previewSeconds;
                var state = !isEffect ? 0 : slot == _hovered ? 2 : 1;
                if (!_configService.Dev.EnableRingSpellPreviewAlpha && state == 2)
                    state = 0;
                if (state != slot.PreviewState)
                    SetPreview(slot, state);
            }
        }

        private void SetPreview(Slot slot, int state)
        {
            slot.PreviewState = state;
            if (state > 0 && slot.Effect == null && !slot.HasNoEffect)
            {
                slot.Effect = CreateSpellEffect(slot.Item, slot.Root);
                slot.HasNoEffect = slot.Effect == null;
            }

            var isEffect = state > 0 && slot.Effect != null;
            if (slot.Effect != null)
                slot.Effect.SetActive(isEffect);
            if (slot.Model == null)
                return;

            if (!_configService.Dev.EnableRingSpellPreviewAlpha)
            {
                slot.Model.SetActive(!isEffect);
                return;
            }

            slot.Model.SetActive(true);
            if (isEffect && state == 1)
                _dynamicMaterialService.SetDynamicValue(slot.Model, Constants.ShaderPropertyTransparency,
                    _previewModelAlpha);
            else
                _dynamicMaterialService.ResetDynamicValue(slot.Model, Constants.ShaderPropertyTransparency,
                    Constants.ShaderPropertyTransparencyDefault);
            if (isEffect)
                SetEffectAlpha(slot, state == 2 ? _previewEffectHoverAlpha : 1f);
        }

        /// <summary>
        /// The effect's own materials (instanced once) with their alpha scaled - particles blend by it.
        /// </summary>
        private static void SetEffectAlpha(Slot slot, float alpha)
        {
            if (slot.EffectMaterials == null)
            {
                slot.EffectMaterials = new List<(Material, Color)>();
                foreach (var renderer in slot.Effect.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.materials)
                    {
                        var property = material.HasProperty(_baseColorProperty) ? _baseColorProperty
                            : material.HasProperty(_colorProperty) ? _colorProperty : -1;
                        if (property >= 0)
                            slot.EffectMaterials.Add((material, material.GetColor(property)));
                    }
                }
            }

            foreach (var (material, color) in slot.EffectMaterials)
            {
                if (material == null)
                    continue;
                var property = material.HasProperty(_baseColorProperty) ? _baseColorProperty : _colorProperty;
                material.SetColor(property, new Color(color.r, color.g, color.b, color.a * alpha));
            }
        }

        private GameObject CreateSpellEffect(ItemInstance item, GameObject parent)
        {
            // DeveloperConfig.EnableSpellEffectGothicMotion: Gothic's motion, the whole effect fitted into the slot.
            return VRSpellEffect.Create(item, parent, _gameStateService.GothicVm, _vmCacheService, _meshService,
                _configService.Dev.EnableSpellEffectGothicMotion, _spellSize * _effectFitShare);
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
                    if (!IsHandFree(side) || !VrControls.GetButton(side, VrButton.Trigger).IsJustActivated)
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
                    _isSpinStoppedByPress = Mathf.Abs(_spinVelocity) >= _stopSpinVelocity;
                    _spinVelocity = 0f;
                    return;
                }
                return;
            }

            var dragHand = _vrPlayerService.GetHandModelGo(_dragHand);
            var isHeld = VrControls.GetButton(_dragHand, VrButton.Trigger).IsActive;
            if (dragHand == null || !isHeld)
            {
                _isTriggerDown = false;
                if (_isDragging)
                {
                    _isDragging = false;
                    if (!IsFreeSpin)
                        SnapToNearest();
                }
                else if (_pressedSlot != null && _pressedSlot == _hovered && !_isSpinStoppedByPress)
                {
                    Unequip(_pressedSlot, _dragHand);
                }
                _pressedSlot = null;
                return;
            }

            var local = transform.InverseTransformPoint(dragHand.transform.position);
            if (IsFreeSpin)
            {
                // Around the player the hand turns the ring around its axis - the slot under the hand goes with it,
                // whichever way the player faces (sideways in the ring's own space was mirrored behind him).
                var axisDistance = new Vector2(local.x, local.z).magnitude * RingScale;
                if (axisDistance < _minDragAxisDistance)
                    return;
                var deltaAngle = Mathf.DeltaAngle(_dragStartHandAngle, GetHandAngle(local));
                if (!_isDragging && Mathf.Abs(deltaAngle) * Mathf.Deg2Rad * axisDistance >= _dragStartDistance)
                    _isDragging = true;
                if (!_isDragging)
                    return;
                _ringAngle = _dragStartAngle + deltaAngle;
                if (Time.deltaTime > 0f)
                    _spinVelocity = Mathf.Lerp(_spinVelocity,
                        Mathf.DeltaAngle(_lastDragAngle, _ringAngle) / Time.deltaTime, 0.5f);
                _lastDragAngle = _ringAngle;
                return;
            }

            var deltaX = local.x - _dragStartLocal.x;
            if (!_isDragging && Mathf.Abs(deltaX) >= _dragStartDistance)
                _isDragging = true;
            if (_isDragging)
                _ringAngle = _dragStartAngle + deltaX / RingRadius * Mathf.Rad2Deg;
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
        /// the hand - not grabbed (force-grabbing it without colliders flung the physics hand away), so the grip
        /// does nothing - with a VRRuneCaster casting with that hand only. The spell stays in the inventory (equipped),
        /// like Gothic's readied spell. A trigger click of the other hand puts it away (VRFavoritesRingSpell).
        /// </summary>
        private void TakeSpell(Slot slot, VrHandSide side)
        {
            var handModel = _vrPlayerService.GetHandModelGo(side);
            if (handModel == null || string.IsNullOrEmpty(slot.InstanceName))
                return;

            // DeveloperConfig.EnableRingSpellReplacesHandSpell: the hand's spell taken before stayed with its own
            // VRRuneCaster - both charged and cast at once (a waiting transformation cast again on transforming back).
            if (_configService.Dev.EnableRingSpellReplacesHandSpell)
            {
                foreach (var oldSpell in handModel.GetComponentsInChildren<VRFavoritesRingSpell>())
                    oldSpell.Replace();
            }

            var container = CreateItem(slot.InstanceName);
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
            _saveGameService.CurrentWorldData.Vobs.Add(container.Vob);
            _saveGameService.TrackLooseItem(container);
            _playerService.RemoveItem(slot.InstanceName, 1);
            slot.Amount--;

            Logger.Log($"[FavoritesRing] {slot.Item.Name} taken into the hand ({slot.Amount} left)", LogCat.VR);
            if (slot.Kind is SlotKind.Melee or SlotKind.Ranged)
            {
                _takenWeapon = new TakenWeapon
                {
                    Grabbable = grabbable,
                    Item = slot.Item,
                    InstanceName = slot.InstanceName,
                    Kind = slot.Kind
                };
            }
            if (slot.Kind is SlotKind.Item or SlotKind.Currency && slot.Amount > 0 && args.IsHandOrDistance)
            {
                _transfer = new Transfer
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
        /// _transferDelaySeconds moves more of the stack into the hand - faster and faster. The held stack counts in
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
                _transferEndDistance * RingScale)
            {
                Logger.Log($"[FavoritesRing] {_transfer.Slot.Item.Name}: {held?.VobAs<IItem>()?.Amount ?? 0} taken " +
                           $"({_transfer.Slot.Amount} left)", LogCat.VR);
                Close("item taken");
                return true;
            }

            // The rest of the ring waits meanwhile.
            var heldTime = Time.time - _transfer.StartTime - _transferDelaySeconds;
            if (heldTime < 0f || _transfer.Slot.Amount <= 0)
                return true;

            var rate = Mathf.Min(_transferMaxRate, _transferStartRate * Mathf.Pow(2f, heldTime));
            _transfer.Carry += rate * Time.deltaTime;
            var count = Mathf.Min(Mathf.FloorToInt(_transfer.Carry), _transfer.Slot.Amount);
            if (count <= 0)
                return true;

            _transfer.Carry -= count;
            held.VobAs<IItem>().Amount += count;
            _transfer.Slot.Amount -= count;
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
                    Time.time - slot.TakenTime < _takeTimeoutSeconds || slot.Grabbable.IsHeld)
                    continue;
                Logger.LogWarning($"[FavoritesRing] {slot.InstanceName}: grab didn't finish - back into the ring",
                    LogCat.VR);
                Rebuild();
                return;
            }
        }

        private VobContainer CreateItem(string instanceName)
        {
            return _vobService.CreateItem(new Item
            {
                Name = instanceName,
                Visual = new VisualMesh(),
                Instance = instanceName,
                Amount = 1
            });
        }

        private void Unequip(Slot slot, VrHandSide side)
        {
            if (slot.Item == null || slot.Kind == SlotKind.Currency)
                return;
            if (slot.Kind == SlotKind.Item)
                _playerService.HeroContainer.Props.FavoriteItems.Remove(slot.InstanceName);
            else
                _npcInventoryService.UnequipItemWithEffects(_playerService.HeroContainer.Instance, slot.Item);
            _hapticsService.Vibrate(side, VrHapticPreset.Warning);
            Logger.Log($"[FavoritesRing] {GetSlotName(slot)} " +
                       (slot.Kind == SlotKind.Item ? "taken off the ring" : "unequipped"), LogCat.VR);
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
                var kind = GetKind(item, instanceName);
                var target = _slots
                    .Where(s => s.TakenTime < 0f && IsNear(held, s, _putInDistance * RingScale))
                    .OrderBy(s => IsSame(s.InstanceName, instanceName) ? 0 : s.Kind == kind ? 1 : 2)
                    .FirstOrDefault();
                if (target == null || !TryPutIn(item, instanceName, kind, target, out var action, out var replaced))
                {
                    ExpirePutInCandidate(index);
                    continue;
                }

                if (_putInCandidates[index]?.Target != target)
                    _hapticsService.Vibrate(side, VrHapticPreset.Info);
                _putInCandidates[index] = new PutInCandidate
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
            if (_putInCandidates[index] != null && Time.time - _putInCandidates[index].Time > _putInGraceSeconds)
                _putInCandidates[index] = null;
        }

        /// <summary>
        /// DeveloperConfig.EnableRingWeaponLeavesOnDrop: a weapon taken out of the ring stays the equipped one if it
        /// went back into the backpack (opened, or over the shoulder - then it's gone from the world) - dropped or put
        /// into another quick slot it comes off (Gothic: an equipped weapon is in the inventory). Back into the ring,
        /// the ring equipped it already.
        /// </summary>
        private void UpdateTakenWeapon()
        {
            var taken = _takenWeapon;
            if (taken == null || taken.ReleasedTime < 0f || Time.time - taken.ReleasedTime < _takenWeaponCheckSeconds)
                return;
            if (taken.Grabbable != null && taken.Grabbable.IsHeld)
            {
                // In a hand again.
                taken.ReleasedTime = -1f;
                return;
            }

            _takenWeapon = null;
            if (!_configService.Dev.EnableRingWeaponLeavesOnDrop)
                return;

            var hero = _playerService.HeroContainer;
            var isGone = taken.Grabbable == null || taken.Grabbable.GetComponentInParent<VRBackpack>() != null;
            var isInBackpack = isGone && GetBackpackAmount(taken.InstanceName) > 0;
            var isEquipped = _npcInventoryService.IsEquipped(hero.Instance, taken.InstanceName);
            if (isInBackpack && !isEquipped)
            {
                foreach (var other in hero.Props.EquippedItems.Where(i => GetKind(i) == taken.Kind).ToList())
                    _npcInventoryService.UnequipItemWithEffects(hero.Instance, other);
                _npcInventoryService.EquipItemWithEffects(hero.Instance, taken.Item);
            }
            else if (!isInBackpack && isEquipped)
            {
                _npcInventoryService.UnequipItemWithEffects(hero.Instance, taken.Item);
            }
            Logger.Log($"[FavoritesRing] {taken.Item.Name} " + (isInBackpack
                ? "back in the backpack - stays equipped"
                : "dropped or in another quick slot - no longer equipped"), LogCat.VR);
        }

        /// <summary>
        /// Let go of an item at a slot (within _putInGraceSeconds): it goes in next frame - after the backend and
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
            if (candidate == null || Time.time - candidate.Time > _putInGraceSeconds ||
                grabbable.GetComponentInParent<VobLoader>()?.Container != candidate.Container)
                return;
            _pendingPutIn = candidate;
        }

        /// <summary>
        /// The let go item into the backpack - like VRItemUser.Equip:
        /// - At its own slot: just back into the backpack.
        /// - A rune/scroll into the empty slot (below 8): equipped.
        /// - A weapon into the empty slot or onto the equipped one of its kind, which it replaces (Gothic: one melee
        ///   weapon, one bow/crossbow): equipped - if the hero meets its requirements, else the engine's message.
        /// - Anything else into the empty slot (below 3): a favorite item.
        /// </summary>
        private void UpdatePendingPutIn()
        {
            var putIn = _pendingPutIn;
            _pendingPutIn = null;
            if (putIn?.Container?.Go == null || !_slots.Contains(putIn.Target))
                return;

            var vobItem = putIn.Container.VobAs<IItem>();
            _saveGameService.UntrackLooseItem(putIn.Container);
            _playerService.AddItem(putIn.InstanceName, Mathf.Max(1, vobItem.Amount));
            var hero = _playerService.HeroContainer;
            if (putIn.Action == "equipped" && putIn.Kind is SlotKind.Melee or SlotKind.Ranged)
            {
                // Gothic: one melee weapon, one bow/crossbow - all others of the kind come off (the ring's one, and
                // any left from before: several bows stayed equipped, the backpack showed [E] on each in turn).
                foreach (var other in hero.Props.EquippedItems.Where(i => GetKind(i) == putIn.Kind).ToList())
                    _npcInventoryService.UnequipItemWithEffects(hero.Instance, other);
            }
            else if (putIn.Replaced != null)
            {
                _npcInventoryService.UnequipItemWithEffects(hero.Instance, putIn.Replaced);
            }
            if (putIn.Action == "equipped")
                _npcInventoryService.EquipItemWithEffects(hero.Instance, putIn.Item);
            else if (putIn.Action == "a favorite")
                hero.Props.FavoriteItems.Add(putIn.InstanceName);
            _vobService.RemoveWorldItem(putIn.Container);

            _hapticsService.Vibrate(putIn.Side, VrHapticPreset.Success);
            var name = putIn.Kind == SlotKind.Spell ? GetSpellName(putIn.Item) : putIn.Item.Name;
            var instead = putIn.Replaced != null ? $" instead of {putIn.Replaced.Name}" : "";
            Logger.Log($"[FavoritesRing] {name} {putIn.Action}{instead}", LogCat.VR);
            Rebuild();
        }

        /// <summary>
        /// What letting go of the held item at the target slot does - false (with the reason logged once) if it
        /// can't go in there.
        /// </summary>
        private bool TryPutIn(ItemInstance item, string instanceName, SlotKind kind, Slot target, out string action,
            out ItemInstance replaced)
        {
            action = null;
            replaced = null;
            string rejection = null;
            var hero = _playerService.HeroContainer;
            var isInRing = _slots.Any(s => IsSame(s.InstanceName, instanceName)) ||
                           hero.Props.FavoriteItems.Any(i => IsSame(i, instanceName));
            if (item == null)
                rejection = "unknown item";
            else if (IsSame(target.InstanceName, instanceName) ||
                     target.Kind == SlotKind.Empty && (isInRing || kind == SlotKind.Currency))
                // Its own slot - or another one of it (its slot shows already), or the money.
                action = "back into the backpack";
            else if (target.Kind != SlotKind.Empty &&
                     !(target.Kind == kind && kind is SlotKind.Melee or SlotKind.Ranged))
                rejection = $"a {kind} doesn't go onto a {target.Kind}";

            if (rejection == null && action == null)
            {
                switch (kind)
                {
                    case SlotKind.Spell when _slots.Count(s => s.Kind == SlotKind.Spell) >= _maxSpells:
                        rejection = "8 spells already";
                        break;
                    case SlotKind.Spell when !_fightService.CheckHeroRuneCircle(item, true):
                        rejection = "magic circle too low (EnableRuneCircleRequirement)";
                        break;
                    case SlotKind.Spell:
                        action = "equipped";
                        break;
                    case SlotKind.Melee or SlotKind.Ranged when !IsWeaponSlotsEnabled:
                        rejection = "weapon slots off (EnableRingWeaponSlots)";
                        break;
                    case SlotKind.Melee or SlotKind.Ranged when !_fightService.CheckHeroWeaponRequirement(item, true):
                        rejection = "requirements not met";
                        break;
                    case SlotKind.Melee or SlotKind.Ranged:
                        action = "equipped";
                        replaced = _slots.FirstOrDefault(s => s.Kind == kind)?.Item;
                        break;
                    case SlotKind.Item when !IsItemSlotsEnabled:
                        rejection = "item slots off (EnableRingItemSlots)";
                        break;
                    case SlotKind.Item when hero.Props.FavoriteItems.Count >= _maxItems:
                        rejection = "3 favorite items already";
                        break;
                    case SlotKind.Item:
                        action = "a favorite";
                        break;
                }
            }

            if (rejection == null)
            {
                _lastRejection = null;
                return action != null;
            }
            var message = $"{instanceName} into {target.Kind} {target.InstanceName}: {rejection}";
            if (message != _lastRejection)
            {
                _lastRejection = message;
                Logger.Log($"[FavoritesRing] Not put in - {message}", LogCat.VR);
            }
            return false;
        }

        private static bool IsSame(string instanceName, string other)
        {
            return string.Equals(instanceName, other, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Any part of the held item at the slot - a sword is put in with its blade, its grip stays in the hand.
        /// </summary>
        private static bool IsNear(GameObject held, Slot slot, float distance)
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
