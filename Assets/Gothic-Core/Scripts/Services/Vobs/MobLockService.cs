using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Reflex.Attributes;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// Platform-neutral rules of a locked door or chest (oCMobLockable::CanOpen): the key in the hero's inventory
    /// opens it, otherwise the hero says what he misses. The platform layer only detects the touch.
    /// </summary>
    public class MobLockService
    {
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly DialogService _dialogService;

        // Fallbacks for G1's smaller SVM set: DialogService takes the first one the game has a line for.
        private static readonly string[] _keyOrPickMissingSvms = { "$PICKLOCKORKEYMISSING", "$NEEDKEY", "$DOESNTWORK" };
        private static readonly string[] _keyMissingSvms = { "$KEYMISSING", "$NEEDKEY", "$DOESNTWORK" };
        private static readonly string[] _pickMissingSvms = { "$PICKLOCKMISSING", "$NOMOREPICKS", "$DOESNTWORK" };
        private static readonly string[] _neverOpenSvms = { "$NEVEROPEN", "$DOESNTWORK" };

        /// <summary>
        /// Like the engine, the key in the inventory opens the lock: it doesn't have to be taken out of the backpack.
        /// </summary>
        public bool CanHeroOpenWithKey(IVirtualObject lockable)
        {
            if (!lockable.TryGetLock(out _, out var key, out _) || string.IsNullOrEmpty(key))
                return false;

            var hero = _playerService.HeroContainer;
            var keyItem = _vmCacheService.TryGetItemData(key);
            if (hero?.Instance == null || keyItem == null)
            {
                Logger.LogWarning($"[MobLock] Key '{key}' of '{lockable.Name}' can't be checked (hero or item " +
                                  "instance missing).", LogCat.Vob);
                return false;
            }

            if (_npcInventoryService.ExtNpcHasItems(hero.Instance, keyItem.Index) <= 0)
                return false;

            Logger.Log($"[MobLock] Key '{key}' in the inventory opens '{lockable.Name}'.", LogCat.Vob);
            return true;
        }

        /// <summary>
        /// Like oCMobLockable::CanOpen: the SVMs for what the hero misses to open it, best first.
        /// </summary>
        public string[] GetLockedSvms(IVirtualObject lockable)
        {
            lockable.TryGetLock(out _, out var key, out var pickString);
            var hasKey = !string.IsNullOrEmpty(key);
            var hasPick = !string.IsNullOrEmpty(pickString);

            if (hasKey && hasPick)
                return _keyOrPickMissingSvms;
            if (hasKey)
                return _keyMissingSvms;
            return hasPick ? _pickMissingSvms : _neverOpenSvms;
        }

        /// <summary>
        /// The hero tried the locked door/chest without key or lock pick: he says why it doesn't open.
        /// </summary>
        public void HeroSayLocked(IVirtualObject lockable)
        {
            _dialogService.HeroSaySvm(GetLockedSvms(lockable));
        }
    }
}
