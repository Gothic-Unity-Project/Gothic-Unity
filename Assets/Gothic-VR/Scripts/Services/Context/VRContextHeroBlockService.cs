using System;
using System.Collections;
using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Manager;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services.Context
{
    /// <summary>
    /// Immersion option heroBlock (Gothic.ini, on by default): the hero blocks while the hero's props say so
    /// (IsBlocking, BlockWeaponMaterial - set by the VR block). Only a hit that touched the held weapon is blocked - a
    /// block held above the NPC's head blocked his hit too. The hit can reach the body first: it waits a moment for the
    /// touch.
    /// </summary>
    public class VRContextHeroBlockService : IContextHeroBlockService
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        private const float _blockContactSeconds = 0.5f;
        private readonly Dictionary<NpcContainer, float> _blockContacts = new();
        // A deferred hit resolved without a block lands again - without asking for a block twice.
        private bool _isBlockDecided;
        // Gothic.ini heroBlock, read on first use and again when the menu changes it.
        private bool? _isOptionOn;

        public VRContextHeroBlockService()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
        }

        public bool IsBlocking(NpcContainer hero)
        {
            _isOptionOn ??= _configService.Gothic.GetInt(VRConstants.IniNames.HeroBlock,
                VRConstants.HeroBlockDefaultValue) == 1;
            return _isOptionOn.Value && hero.Props.IsBlocking;
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == VRConstants.IniNames.HeroBlock)
                _isOptionOn = null;
        }

        public int GetBlockWeaponMaterial(NpcContainer hero)
        {
            return hero.Props.BlockWeaponMaterial;
        }

        public void ReportBlockContact(NpcContainer attacker)
        {
            if (attacker != null)
                _blockContacts[attacker] = Time.time;
        }

        public bool TryBlock(NpcContainer attacker, NpcContainer hero, Action onBlocked, Action onMissed)
        {
            if (!IsBlocking(hero) || _isBlockDecided)
                return false;

            if (HasBlockContact(attacker))
            {
                onBlocked();
                return true;
            }
            _unityMonoService.StartCoroutine(AwaitBlockContact(attacker, hero, onBlocked, onMissed));
            return true;
        }

        private bool HasBlockContact(NpcContainer attacker)
        {
            return _blockContacts.TryGetValue(attacker, out var time) && Time.time - time <= _blockContactSeconds;
        }

        /// <summary>
        /// The hit reached the hero before his weapon: blocked if the NPC's weapon touches it within
        /// _blockContactSeconds, else it hits now.
        /// </summary>
        private IEnumerator AwaitBlockContact(NpcContainer attacker, NpcContainer hero, Action onBlocked,
            Action onMissed)
        {
            var end = Time.time + _blockContactSeconds;
            while (Time.time < end)
            {
                if (HasBlockContact(attacker) && hero.Props.IsBlocking)
                {
                    onBlocked();
                    yield break;
                }
                yield return null;
            }

            Logger.Log($"[VRContextHeroBlockService] {attacker.Instance.GetName(NpcNameSlot.Slot0)}'s hit missed the " +
                       "blocking weapon", LogCat.Fight);
            _isBlockDecided = true;
            try
            {
                onMissed();
            }
            finally
            {
                _isBlockDecided = false;
            }
        }
    }
}
