using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Domain.Player;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Models.FavoritesRing;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// DeveloperConfig.EnableVrSpellRing: what the VR favorites ring holds and what putting into / taking out of it
    /// does (VRFavoritesRing shows it).
    /// - The slots: the hero's equipped spells (runes/scrolls, ITEM_KAT_RUNE) in equip order, his equipped melee weapon
    ///   and bow/crossbow (EnableRingWeaponSlots), his favorite items and the trade currency (EnableRingItemSlots) -
    ///   those in the backpack - plus the empty slot while something is missing.
    /// - The favorite items: the hero's, as Daedalus symbol names. Not saved yet (a save section with main's
    ///   save/load); a new hero (new game, loaded game) starts without.
    /// - The put-in rules (VrFavoritesPutInDomain) and the open gesture (VrFavoritesGestureDomain).
    /// </summary>
    public class VrFavoritesService
    {
        public const int MaxSpells = 8;
        public const int MaxWeapons = 2;
        public const int MaxItems = 3;

        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly TradeService _tradeService;
        [Inject] private readonly VRSpellTransformService _vrSpellTransformService;

        private readonly List<FavoriteSlot> _slots = new();
        private readonly List<string> _favoriteItems = new();
        // The hero the favorite items belong to.
        private NpcContainer _favoritesOwner;
        // A readied ring spell per hand (left, right) - the ring's open gesture needs empty hands.
        private readonly bool[] _isReadied = new bool[2];
        private VrFavoritesPutInDomain _putInDomain;
        private VrFavoritesGestureDomain _gestureDomain;

        public bool IsWeaponSlotsEnabled => _configService.Dev.EnableRingWeaponSlots;
        public bool IsItemSlotsEnabled => _configService.Dev.EnableRingItemSlots;

        /// <summary>
        /// The trade currency's instance name (G1 ore, G2 gold: TRADE_CURRENCY_INSTANCE) - TradeService's.
        /// </summary>
        public string CurrencyName => _tradeService.CurrencyName;

        /// <summary>
        /// The hero's favorite items (Daedalus symbol names). They belong to the hero - another hero container (a new
        /// or loaded game) starts without.
        /// </summary>
        private List<string> FavoriteItems
        {
            get
            {
                var hero = _playerService.HeroContainer;
                if (hero != _favoritesOwner)
                {
                    _favoriteItems.Clear();
                    _favoritesOwner = hero;
                }
                return _favoriteItems;
            }
        }

        public bool IsReadiedIn(VrHandSide side) => _isReadied[(int)side];

        public void SetReadied(VrHandSide side, bool isReadied)
        {
            _isReadied[(int)side] = isReadied;
        }

        public void AddFavorite(string instanceName)
        {
            FavoriteItems.Add(instanceName);
        }

        public void RemoveFavorite(string instanceName)
        {
            FavoriteItems.Remove(instanceName);
        }

        /// <summary>
        /// A dialog, a knockout or a transformation: no ring.
        /// </summary>
        public bool IsHeroBusy()
        {
            var bodyState = _playerService.HeroContainer.Props.BodyState;
            // Transformed: a monster has no spells (only VRSpellTransformPuppet's attack).
            return bodyState is VmGothicEnums.BodyState.BsUnconscious or VmGothicEnums.BodyState.BsDead ||
                   _gameStateService.Dialogs.IsInDialog || _vrSpellTransformService.IsTransformed;
        }

        /// <summary>
        /// Every frame while the ring is enabled: true once the open/close gesture was held long enough.
        /// </summary>
        public bool TickGesture(Transform head)
        {
            _gestureDomain ??= new VrFavoritesGestureDomain().Inject();
            var isBlocked = IsReadiedIn(VrHandSide.Left) || IsReadiedIn(VrHandSide.Right) || IsHeroBusy();
            return _gestureDomain.Tick(head, isBlocked);
        }

        /// <summary>
        /// The ring's contents, built anew - kept until the next build (TryPutIn checks against them).
        /// </summary>
        public IReadOnlyList<FavoriteSlot> BuildSlots()
        {
            _slots.Clear();

            var vm = _gameStateService.GothicVm;
            var hero = _playerService.HeroContainer;
            var equipped = hero.Props.EquippedItems;
            var spells = equipped.Where(i => GetKind(i) == FavoriteSlotKind.Spell).Take(MaxSpells).ToList();
            foreach (var spell in spells)
                _slots.Add(CreateSlot(FavoriteSlotKind.Spell, spell, vm.GetSymbolByIndex(spell.Index)?.Name));

            var weaponCount = 0;
            if (IsWeaponSlotsEnabled)
            {
                foreach (var kind in new[] { FavoriteSlotKind.Melee, FavoriteSlotKind.Ranged })
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
                var currency = CurrencyName;
                // Used up, sold or dropped: no longer a favorite. The money has its own slot.
                FavoriteItems.RemoveAll(name => _playerService.GetItemAmount(name) <= 0 || IsSame(name, currency));
                foreach (var instanceName in FavoriteItems.Take(MaxItems))
                {
                    var item = _vmCacheService.TryGetItemData(instanceName);
                    if (item == null)
                        continue;
                    itemCount++;
                    if (IsInBackpack(instanceName))
                        _slots.Add(CreateSlot(FavoriteSlotKind.Item, item, instanceName));
                }

                var currencyItem = _vmCacheService.TryGetItemData(currency);
                if (currencyItem != null && GetBackpackAmount(currency) > 0)
                    _slots.Add(CreateSlot(FavoriteSlotKind.Currency, currencyItem, currency));
            }

            if (spells.Count < MaxSpells || IsWeaponSlotsEnabled && weaponCount < MaxWeapons ||
                IsItemSlotsEnabled && itemCount < MaxItems)
                _slots.Add(CreateSlot(FavoriteSlotKind.Empty, null, null));

            return _slots;
        }

        public void ClearSlots()
        {
            _slots.Clear();
        }

        private FavoriteSlot CreateSlot(FavoriteSlotKind kind, ItemInstance item, string instanceName)
        {
            return new FavoriteSlot
            {
                Kind = kind,
                Item = item,
                InstanceName = instanceName,
                Amount = instanceName != null ? GetBackpackAmount(instanceName) : 0
            };
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
            var inventoryAmount = _playerService.GetItemAmount(instanceName);
            Logger.Log($"[FavoritesRing] {instanceName} isn't in the backpack (inventory {inventoryAmount}, " +
                       $"backpack {amount}: held, in a holster or gone) - not shown", LogCat.VR);
            return false;
        }

        /// <summary>
        /// Which ring slot an item belongs into: runes/scrolls, melee weapons (ITEM_KAT_NF), bows/crossbows
        /// (ITEM_KAT_FF), the money or any other item.
        /// </summary>
        public FavoriteSlotKind GetKind(ItemInstance item, string instanceName = null)
        {
            if (item == null)
                return FavoriteSlotKind.Empty;
            if (instanceName != null && IsSame(instanceName, CurrencyName))
                return FavoriteSlotKind.Currency;
            return (VmGothicEnums.ItemFlags)item.MainFlag switch
            {
                VmGothicEnums.ItemFlags.ItemKatRune => FavoriteSlotKind.Spell,
                VmGothicEnums.ItemFlags.ItemKatNf => FavoriteSlotKind.Melee,
                VmGothicEnums.ItemFlags.ItemKatFf => FavoriteSlotKind.Ranged,
                _ => FavoriteSlotKind.Item
            };
        }

        /// <summary>
        /// How many are in the backpack: the inventory without those in the hands and holsters (IsHeldByPlayer). The
        /// backpack's own sockets are player sockets too (the page shown) - those are in it.
        /// </summary>
        public int GetBackpackAmount(string instanceName)
        {
            var amount = _playerService.GetItemAmount(instanceName);
            foreach (var container in _vrPlayerService.HeldItems)
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

        /// <summary>
        /// What letting go of the held item at the target slot does - false (with the reason logged once) if it
        /// can't go in there. Checked against the slots of the last BuildSlots.
        /// </summary>
        public bool TryPutIn(ItemInstance item, string instanceName, FavoriteSlotKind kind, FavoriteSlot target,
            out FavoritesPutInAction action, out ItemInstance replaced)
        {
            _putInDomain ??= new VrFavoritesPutInDomain().Inject();
            return _putInDomain.TryPutIn(item, instanceName, kind, target, _slots, FavoriteItems, MaxSpells, MaxItems,
                out action, out replaced);
        }

        /// <summary>
        /// The let go item into the backpack - like VRItemUser.Equip, as TryPutIn decided: a rune/scroll or weapon is
        /// equipped (a weapon replaces the equipped one of its kind), anything else becomes a favorite - or it just
        /// goes back.
        /// </summary>
        public void PutIn(ItemInstance item, string instanceName, FavoriteSlotKind kind, int amount,
            FavoritesPutInAction action, ItemInstance replaced)
        {
            _playerService.AddItem(instanceName, amount);
            var hero = _playerService.HeroContainer;
            if (action == FavoritesPutInAction.Equipped && kind is FavoriteSlotKind.Melee or FavoriteSlotKind.Ranged)
            {
                // Gothic: one melee weapon, one bow/crossbow - all others of the kind come off (the ring's one, and
                // any left from before: several bows stayed equipped, the backpack showed [E] on each in turn).
                foreach (var other in hero.Props.EquippedItems.Where(i => GetKind(i) == kind).ToList())
                    _npcInventoryService.UnequipItemWithEffects(hero.Instance, other);
            }
            else if (replaced != null)
            {
                _npcInventoryService.UnequipItemWithEffects(hero.Instance, replaced);
            }
            if (action == FavoritesPutInAction.Equipped)
                _npcInventoryService.EquipItemWithEffects(hero.Instance, item);
            else if (action == FavoritesPutInAction.Favorite)
                AddFavorite(instanceName);
        }

        /// <summary>
        /// A trigger click at a slot: a spell or weapon is unequipped, an item taken off the ring - it stays in the
        /// backpack. False for the money and the empty slot.
        /// </summary>
        public bool TakeOff(FavoriteSlot slot)
        {
            if (slot.Item == null || slot.Kind == FavoriteSlotKind.Currency)
                return false;
            if (slot.Kind == FavoriteSlotKind.Item)
                RemoveFavorite(slot.InstanceName);
            else
                _npcInventoryService.UnequipItemWithEffects(_playerService.HeroContainer.Instance, slot.Item);
            return true;
        }

        /// <summary>
        /// One of the slot's items into a hand: one less in the backpack. The grab counted the held one into the
        /// inventory (VRPlayerService.SetGrab) - so the count stays and an equipped weapon stays equipped.
        /// </summary>
        public void TakeOutOne(FavoriteSlot slot)
        {
            _playerService.RemoveItem(slot.InstanceName, 1);
            slot.Amount--;
        }

        /// <summary>
        /// A weapon taken out of the ring stays the equipped one while it's in the inventory (Gothic): in a hand, a
        /// holster or back in the backpack. Back into the backpack it's equipped again if it wasn't any more.
        /// </summary>
        /// <param name="isGone">Its object is gone or in the backpack.</param>
        public void KeepTakenWeaponEquipped(ItemInstance item, string instanceName, FavoriteSlotKind kind,
            bool isGone)
        {
            var hero = _playerService.HeroContainer;
            var isInBackpack = isGone && GetBackpackAmount(instanceName) > 0;
            var isEquipped = _npcInventoryService.IsEquipped(hero.Instance, instanceName);
            if (isInBackpack && !isEquipped)
            {
                foreach (var other in hero.Props.EquippedItems.Where(i => GetKind(i) == kind).ToList())
                    _npcInventoryService.UnequipItemWithEffects(hero.Instance, other);
                _npcInventoryService.EquipItemWithEffects(hero.Instance, item);
            }
            if (isInBackpack)
                Logger.Log($"[FavoritesRing] {item.Name} back in the backpack - stays equipped", LogCat.VR);
        }

        public static bool IsSame(string instanceName, string other)
        {
            return string.Equals(instanceName, other, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
