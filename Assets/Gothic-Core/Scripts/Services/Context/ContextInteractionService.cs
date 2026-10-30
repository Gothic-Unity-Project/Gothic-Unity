using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Context;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Context
{
    public class ContextInteractionService : IContextInteractionService
    {
        private IContextInteractionService _impl;

        // The controls each owner blocks right now.
        private readonly Dictionary<PlayerLocks, PlayerControls> _playerLocks = new();

        public PlayerLocks ActivePlayerLocks { get; private set; }

        public void SetImpl(IContextInteractionService proxy)
        {
            _impl = proxy;
        }

        public T GetImpl<T>() where T : IContextInteractionService
        {
            return (T)_impl;
        }

        public string GetContextName()
        {
            return _impl.GetContextName();
        }

        public float GetFrameRate()
        {
            return _impl.GetFrameRate();
        }

        public void SetupPlayerController(DeveloperConfig developerConfig)
        {
            _impl.SetupPlayerController(developerConfig);
        }

        public GameObject GetCurrentPlayerController()
        {
            return _impl.GetCurrentPlayerController();
        }

        /// <summary>
        /// Keeps the player from using these controls until the same owner unlocks. Owners lock independently (a dialog
        /// while seated): ending the dialog doesn't stand the player up, the player is free once every owner unlocked.
        /// </summary>
        public void LockPlayer(PlayerLocks owner, PlayerControls controls = PlayerControls.All)
        {
            if (owner == PlayerLocks.None || ((int)owner & ((int)owner - 1)) != 0)
            {
                Logger.LogWarning($"LockPlayer needs exactly one owner, got {owner} - ignored.", LogCat.Misc);
                return;
            }

            _playerLocks[owner] = controls;
            ApplyPlayerLocks();
        }

        /// <summary>
        /// Releases the locks of these owners (several at once as flags). The locks of other owners stay.
        /// </summary>
        public void UnlockPlayer(PlayerLocks owners)
        {
            foreach (var owner in _playerLocks.Keys.Where(owner => (owners & owner) != 0).ToList())
                _playerLocks.Remove(owner);

            ApplyPlayerLocks();
        }

        private void ApplyPlayerLocks()
        {
            var blockedControls = PlayerControls.None;
            var activeLocks = PlayerLocks.None;
            foreach (var playerLock in _playerLocks)
            {
                activeLocks |= playerLock.Key;
                blockedControls |= playerLock.Value;
            }

            if (activeLocks != ActivePlayerLocks)
                Logger.Log($"Player locks: {activeLocks} (blocked: {blockedControls})", LogCat.Misc);

            ActivePlayerLocks = activeLocks;
            _impl.SetBlockedControls(blockedControls);
        }

        void IContextInteractionService.SetBlockedControls(PlayerControls blockedControls)
        {
            _impl.SetBlockedControls(blockedControls);
        }

        public void SetWalkingControls()
        {
            _impl.SetWalkingControls();
        }

        public void SetWaterWalkingControls()
        {
            _impl.SetWaterWalkingControls();
        }

        public void SetSwimmingControls()
        {
            _impl.SetSwimmingControls();
        }

        public void SetDivingControls()
        {
            _impl.SetDivingControls();
        }

        public void TeleportPlayerTo(Vector3 position, Quaternion rotation = default)
        {
            _impl.TeleportPlayerTo(position,rotation);
        }

        public void KeepPlayerWithin(Vector3 center, float softRadius, float radius)
        {
            _impl.KeepPlayerWithin(center, softRadius, radius);
        }

        public void InitUIInteraction()
        {
            _impl.InitUIInteraction();
        }

        public void IntroduceChapter(string chapter, string text, string texture, string wav, int time)
        {
            _impl.IntroduceChapter(chapter, text, texture, wav, time);
        }

        public void DisableMenus()
        {
            _impl.DisableMenus();
        }

        public void EnableMenus()
        {
            _impl.EnableMenus();
        }
    }
}
