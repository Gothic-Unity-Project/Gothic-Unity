using Gothic.Core.Adapters.UI;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// "-X HP" / "+X HP" above an NPC's head whenever its hit points change - from any source going through
    /// NpcService.SetAttribute (melee, spells, arrows, potions, scripts). The hero's are next to his bar
    /// (StatusBarAdapter). Option "hitNumbers" in our settings menu (Gothic.ini, off by default). One per world scene.
    /// </summary>
    public class NpcHitNumbers : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;

        private const float _aboveHead = 0.3f;
        private const float _fallbackHeight = 2f;

        private bool _isEnabled;
        private bool _isHeadMissingLogged;

        private void Awake()
        {
            this.Inject();
            _isEnabled = IsOptionOn();
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
            GlobalEventDispatcher.NpcAttributeAmountChanged.AddListener(OnNpcAttributeAmountChanged);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);
            GlobalEventDispatcher.NpcAttributeAmountChanged.RemoveListener(OnNpcAttributeAmountChanged);
        }

        private bool IsOptionOn()
        {
            return _configService.IsGothicOptionOn(Constants.IniNames.HitNumbers);
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == Constants.IniNames.HitNumbers)
                _isEnabled = IsOptionOn();
        }

        private void OnNpcAttributeAmountChanged(NpcContainer npc, NpcAttribute attribute, int amount)
        {
            // Not for the hero - you don't see your own head. Not for culled NPCs.
            if (!_isEnabled || attribute != NpcAttribute.HitPoints || npc?.Go == null || !npc.Go.activeInHierarchy ||
                npc.PrefabProps == null || npc.PrefabProps.IsHero())
                return;

            FloatingHitNumber.Spawn(GetSpawnPosition(npc), amount, "HP");
        }

        private Vector3 GetSpawnPosition(NpcContainer npc)
        {
            var head = npc.Go.transform.FindChildRecursively(Constants.Animations.HeadBoneName);
            if (head != null)
                return head.position + Vector3.up * _aboveHead;

            // Monsters can name their head bone differently.
            if (!_isHeadMissingLogged)
            {
                _isHeadMissingLogged = true;
                Logger.Log($"[NpcHitNumbers] No >{Constants.Animations.HeadBoneName}< on " +
                           $"'{npc.Instance?.GetName(NpcNameSlot.Slot0)}' - numbers start at a fixed height.",
                    LogCat.Ui);
            }
            return npc.Go.transform.position + Vector3.up * _fallbackHeight;
        }
    }
}
