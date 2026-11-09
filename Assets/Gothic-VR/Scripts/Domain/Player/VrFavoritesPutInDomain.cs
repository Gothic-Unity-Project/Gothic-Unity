using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.VR.Models.FavoritesRing;
using Reflex.Attributes;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Domain.Player
{
    /// <summary>
    /// The favorites ring's put-in rules, owned by VrFavoritesService: what letting go of a held item at a slot does.
    /// - At its own slot (or the empty slot while it shows already, or the money): back into the backpack.
    /// - A rune/scroll into the empty slot: equipped - below 8 spells and if the hero's magic circle is high enough.
    /// - A weapon into the empty slot or onto the equipped one of its kind (which it replaces): equipped - if the hero
    ///   meets its requirements.
    /// - Anything else into the empty slot: a favorite - below 3 favorites.
    /// </summary>
    public class VrFavoritesPutInDomain
    {
        [Inject] private readonly FightService _fightService;
        [Inject] private readonly ConfigService _configService;

        // The item whose "can't use" message was shown - TryPutIn runs every frame while it's held near the ring.
        private ItemInstance _lastMessageItem;
        private string _lastRejection;

        /// <summary>
        /// What letting go of the held item at the target slot does - false (with the reason logged once) if it
        /// can't go in there.
        /// </summary>
        public bool TryPutIn(ItemInstance item, string instanceName, FavoriteSlotKind kind, FavoriteSlot target,
            IReadOnlyList<FavoriteSlot> slots, IReadOnlyList<string> favoriteItems, int maxSpells, int maxItems,
            out FavoritesPutInAction action, out ItemInstance replaced)
        {
            action = FavoritesPutInAction.None;
            replaced = null;
            string rejection = null;
            var isMessageShown = !ReferenceEquals(item, _lastMessageItem);
            var isInRing = slots.Any(s => IsSame(s.InstanceName, instanceName)) ||
                           favoriteItems.Any(i => IsSame(i, instanceName));
            if (item == null)
                rejection = "unknown item";
            else if (IsSame(target.InstanceName, instanceName) ||
                     target.Kind == FavoriteSlotKind.Empty && (isInRing || kind == FavoriteSlotKind.Currency))
                // Its own slot - or another one of it (its slot shows already), or the money.
                action = FavoritesPutInAction.BackIntoBackpack;
            else if (target.Kind != FavoriteSlotKind.Empty &&
                     !(target.Kind == kind && kind is FavoriteSlotKind.Melee or FavoriteSlotKind.Ranged))
                rejection = $"a {kind} doesn't go onto a {target.Kind}";

            if (rejection == null && action == FavoritesPutInAction.None)
            {
                switch (kind)
                {
                    case FavoriteSlotKind.Spell when slots.Count(s => s.Kind == FavoriteSlotKind.Spell) >= maxSpells:
                        rejection = "8 spells already";
                        break;
                    case FavoriteSlotKind.Spell when
                        !CheckOnce(_fightService.CheckHeroRuneCircle(item, isMessageShown), item):
                        rejection = "magic circle too low (EnableRuneCircleRequirement)";
                        break;
                    case FavoriteSlotKind.Spell:
                        action = FavoritesPutInAction.Equipped;
                        break;
                    case FavoriteSlotKind.Melee or FavoriteSlotKind.Ranged when
                        !CheckOnce(_fightService.CheckHeroWeaponRequirement(item, isMessageShown), item):
                        rejection = "requirements not met";
                        break;
                    case FavoriteSlotKind.Melee or FavoriteSlotKind.Ranged:
                        action = FavoritesPutInAction.Equipped;
                        replaced = slots.FirstOrDefault(s => s.Kind == kind)?.Item;
                        break;
                    case FavoriteSlotKind.Item when !_configService.Dev.EnableRingItemSlots:
                        rejection = "item slots off (EnableRingItemSlots)";
                        break;
                    case FavoriteSlotKind.Item when favoriteItems.Count >= maxItems:
                        rejection = "3 favorite items already";
                        break;
                    case FavoriteSlotKind.Item:
                        action = FavoritesPutInAction.Favorite;
                        break;
                }
            }

            if (rejection == null)
            {
                _lastRejection = null;
                return action != FavoritesPutInAction.None;
            }
            var message = $"{instanceName} into {target.Kind} {target.InstanceName}: {rejection}";
            if (message != _lastRejection)
            {
                _lastRejection = message;
                Logger.Log($"[FavoritesRing] Not put in - {message}", LogCat.VR);
            }
            return false;
        }

        private bool CheckOnce(bool isAllowed, ItemInstance item)
        {
            if (!isAllowed)
                _lastMessageItem = item;
            return isAllowed;
        }

        private static bool IsSame(string instanceName, string other)
        {
            return string.Equals(instanceName, other, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
