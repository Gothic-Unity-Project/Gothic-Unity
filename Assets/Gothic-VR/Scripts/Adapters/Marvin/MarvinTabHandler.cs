#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Gothic.Core.Creator;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using ZenKit.Daedalus;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;
using LogCat = Gothic.Core.Logging.LogCat;

namespace Gothic.VR.Adapters.Marvin
{
    public class MarvinTabHandler : MonoBehaviour
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly GameTimeService _gameTimeService;
        [Inject] private readonly NpcRoutineService _npcRoutineService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly Gothic.Core.Services.Player.PlayerService _playerService;
        [Inject] private readonly VmCacheService _vmCacheService;

        private void Start()
        {
            var placeholder = transform.Find("Text");
            if (placeholder != null)
                placeholder.gameObject.SetActive(false);

            CreateButtons();
        }

        private void CreateButtons()
        {
            var buttons = new System.Collections.Generic.List<(string label, System.Action onClick)>();

            if (_configService.Dev.EnableLevel5Cheat)
                buttons.Add(("Level +5", CheatAddLevels));

            if (_configService.Dev.EnableGuildCheat)
                buttons.Add(("Guild → Novice", CheatToNovice));

            if (_configService.Dev.EnableScavengerGuildCheat)
                buttons.Add(("Guild → Scavenger", CheatToScavenger));

            if (_configService.Dev.EnableTimeSkip)
                buttons.Add(("Skip Time +30min", SkipTime30Min));

            if (!string.IsNullOrEmpty(_configService.Dev.MarvinTeleportWaypoint))
                buttons.Add(($"TP → {_configService.Dev.MarvinTeleportWaypoint}", CheatTeleportToWaypoint));

            if (!string.IsNullOrEmpty(_configService.Dev.MarvinSpawnNpcSymbol))
                buttons.Add(($"Spawn {_configService.Dev.MarvinSpawnNpcSymbol}", CheatSpawnNpc));

            if (!string.IsNullOrEmpty(_configService.Dev.MarvinGiveItems))
                buttons.Add(("Give items", CheatGiveItems));

            const float buttonHeight = 50f;
            const float gap = 10f;
            var totalHeight = buttons.Count * buttonHeight + (buttons.Count - 1) * gap;
            var startY = totalHeight / 2f - buttonHeight / 2f;

            for (var i = 0; i < buttons.Count; i++)
            {
                var (label, onClick) = buttons[i];
                CreateButton(label, onClick, startY - i * (buttonHeight + gap));
            }
        }

        private const int _manaMaxPerLevelCheat = 100;

        private void CheatAddLevels()
        {
            const int levelsToAdd = 5;
            var hero = _npcService.GetHeroContainer();
            var oldLevel = hero.Instance.Level;
            hero.Instance.Level += levelsToAdd;
            hero.Instance.Lp += levelsToAdd * 10;
            // HP and mana max up, both filled. Set on the instance too: SyncHeroInstanceToVob() copies the instance
            // into the vob - vob-only values were reverted at once.
            var hpMax = hero.Instance.GetAttribute(NpcAttribute.HitPointsMax) + levelsToAdd * 12;
            var manaMax = hero.Instance.GetAttribute(NpcAttribute.ManaMax) + _manaMaxPerLevelCheat;
            hero.Instance.SetAttribute(NpcAttribute.HitPointsMax, hpMax);
            hero.Instance.SetAttribute(NpcAttribute.HitPoints, hpMax);
            hero.Instance.SetAttribute(NpcAttribute.ManaMax, manaMax);
            hero.Instance.SetAttribute(NpcAttribute.Mana, manaMax);
            _npcService.SyncHeroInstanceToVob();
            Logger.Log($"[MarvinMode] Level cheat: {oldLevel}→{hero.Instance.Level} (+{levelsToAdd * 10} LP, " +
                       $"HP {hpMax}/{hpMax}, mana {manaMax}/{manaMax})", LogCat.Ui);
        }

        private void CheatToNovice()
        {
            var hero = _npcService.GetHeroContainer();
            hero.Instance.Guild = (int)VmGothicEnums.Guild.GIL_NOV;
            Logger.Log("[MarvinMode] Guild cheat: set to GIL_NOV", LogCat.Ui);
        }

        private void CheatToScavenger()
        {
            var hero = _npcService.GetHeroContainer();
            hero.Instance.Guild = (int)VmGothicEnums.Guild.GIL_SCAVENGER;
            Logger.Log("[MarvinMode] Guild cheat: set to GIL_SCAVENGER (24)", LogCat.Ui);
        }

        private void CheatTeleportToWaypoint()
        {
            var wpName = _configService.Dev.MarvinTeleportWaypoint;
            var wp = _wayNetService.GetWayNetPoint(wpName);
            if (wp == null)
            {
                Logger.LogWarning($"[MarvinMode] Teleport WP '{wpName}' not found", LogCat.Ui);
                return;
            }
            _contextInteractionService.TeleportPlayerTo(wp.Position, wp.Rotation);
            Logger.Log($"[MarvinMode] Teleported to '{wpName}'", LogCat.Ui);
        }

        private void CheatSpawnNpc()
        {
            var symbol = _configService.Dev.MarvinSpawnNpcSymbol;
            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
            {
                Logger.LogWarning($"[MarvinMode] Spawn NPC: hero GO not found", LogCat.Ui);
                return;
            }
            var pos = hero.Go.transform.position + hero.Go.transform.forward * 2f;
            var rot = hero.Go.transform.rotation;
            var ok = _npcService.SpawnNpcByName(symbol, pos, rot);
            if (ok)
                Logger.Log($"[MarvinMode] Spawned '{symbol}' near player", LogCat.Ui);
        }

        /// <summary>
        /// DeveloperConfig.MarvinGiveItems: "ITRU_FIREBOLT, ITPO_MANA_03:5" - into the hero's inventory (backpack).
        /// </summary>
        private void CheatGiveItems()
        {
            foreach (var entry in _configService.Dev.MarvinGiveItems.Split(',', ';', '\n'))
            {
                var parts = entry.Trim().Split(':');
                var itemName = parts[0].Trim().ToUpper();
                if (itemName.Length == 0)
                    continue;
                var amount = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var parsed) ? Mathf.Max(1, parsed) : 1;

                if (_vmCacheService.TryGetItemData(itemName) == null)
                {
                    Logger.LogWarning($"[MarvinMode] Give items: unknown item '{itemName}'", LogCat.Ui);
                    continue;
                }
                _playerService.AddItem(itemName, amount);
                Logger.Log($"[MarvinMode] Gave {itemName} x{amount}", LogCat.Ui);
            }
        }

        private void SkipTime30Min()
        {
            var t = _gameTimeService.GetCurrentTime();
            var next = t.Add(System.TimeSpan.FromMinutes(30));
            _gameTimeService.SetTime(next.Hours, next.Minutes);
            _npcRoutineService.RecalculateAllNpcRoutines();
            Logger.Log($"[MarvinMode] Time skip → {next.Hours:D2}:{next.Minutes:D2}", LogCat.Ui);
        }

        private void CreateButton(string label, System.Action onClick, float anchoredY)
        {
            var go = _resourceCacheService.TryGetPrefabObject(PrefabType.UiDebugButton, parent: gameObject);
            if (go == null)
            {
                Logger.LogWarning($"[MarvinMode] UiDebugButton prefab not found for: {label}", LogCat.Ui);
                return;
            }

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = new Vector2(0f, anchoredY);

            var text = go.GetComponentInChildren<TMP_Text>();
            if (text != null)
                text.text = label;

            var button = go.GetComponentInChildren<Button>();
            if (button != null)
                button.onClick.AddListener(() => onClick());
        }
    }
}
#endif
