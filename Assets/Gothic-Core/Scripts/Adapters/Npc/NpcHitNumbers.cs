using Gothic.Core.Adapters.UI;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Config;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// Shows "-X HP" / "+X HP" above the NPC's head whenever its HP changes (mana only at the hero's bar) - from any
    /// source (melee, spells, arrows, potions, scripts), because it compares values instead of hooking every caller.
    /// Option "hitNumbers" in our settings menu (Gothic.ini, off by default).
    /// Lives on the NPC's root, so NPC culling also pauses it.
    /// </summary>
    public class NpcHitNumbers : MonoBehaviour
    {
        private const string _headBoneName = "BIP01 HEAD";
        private const float _aboveHead = 0.3f;
        private const float _fallbackHeight = 2f;

        private NpcLoader _loader;
        private ConfigService _configService;
        private Transform _head;
        private int _lastHp = int.MinValue;
        private bool _isEnabled;
        private bool _isHeadMissingLogged;

        // Loading a save can give the NPC a new container - always ask the loader.
        private NpcContainer Container => _loader != null ? _loader.Container : null;

        public void Init(NpcLoader loader, ConfigService configService)
        {
            _loader = loader;
            _configService = configService;
            _isEnabled = _configService.Gothic.GetInt(Constants.IniNames.HitNumbers, 0) == 1;
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);
        }

        private void OnEnable()
        {
            // Changes while culled are not shown when the NPC comes back.
            _lastHp = int.MinValue;
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == Constants.IniNames.HitNumbers)
                _isEnabled = _configService.Gothic.GetInt(Constants.IniNames.HitNumbers, 0) == 1;
        }

        private void Update()
        {
            var container = Container;
            if (container?.Vob == null)
                return;

            var hp = container.Vob.GetAttribute((int)NpcAttribute.HitPoints);

            // First frame after spawn/enable: just remember the values.
            if (_lastHp == int.MinValue)
            {
                _lastHp = hp;
                return;
            }

            if (_isEnabled && hp != _lastHp)
                FloatingHitNumber.Spawn(GetSpawnPosition(container), hp - _lastHp, "HP");

            _lastHp = hp;
        }

        private Vector3 GetSpawnPosition(NpcContainer container)
        {
            if (_head == null && !_isHeadMissingLogged && container.Go != null)
                _head = container.Go.transform.FindChildRecursively(_headBoneName);

            if (_head != null)
                return _head.position + Vector3.up * _aboveHead;

            // Monsters can name their head bone differently.
            if (!_isHeadMissingLogged)
            {
                _isHeadMissingLogged = true;
                Logger.Log($"[NpcHitNumbers] No >{_headBoneName}< on '{container.Instance?.GetName(NpcNameSlot.Slot0)}'" +
                    " - numbers start at a fixed height.", LogCat.Ui);
            }
            var root = container.Go != null ? container.Go.transform : transform;
            return root.position + Vector3.up * _fallbackHeight;
        }
    }
}
