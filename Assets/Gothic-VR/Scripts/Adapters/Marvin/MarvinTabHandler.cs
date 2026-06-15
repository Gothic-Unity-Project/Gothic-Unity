#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Adapters.UI.Menus;
using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
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
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        private StatusMenu _statusMenu;

        private void Start()
        {
            _statusMenu = FindAnyObjectByType<StatusMenu>(FindObjectsInactive.Include);

            var placeholder = transform.Find("Text");
            if (placeholder != null)
                placeholder.gameObject.SetActive(false);

            CreateButtons();
        }

        private void CreateButtons()
        {
            var buttons = new System.Collections.Generic.List<(string label, System.Action onClick)>();

            if (_configService.Dev.EnableLevel5Cheat)
                buttons.Add(("Level +5  [K]", () => { _statusMenu?.ExecuteLevelCheat(); Logger.Log("[MarvinMode] Level cheat triggered", LogCat.Ui); }));

            if (_configService.Dev.EnableGuildCheat)
                buttons.Add(("Guild → Novice  [L]", () => { _statusMenu?.ExecuteGuildCheat(); Logger.Log("[MarvinMode] Guild cheat triggered", LogCat.Ui); }));

            if (_configService.Dev.EnableTimeSkip)
                buttons.Add(("Skip Time +30min  [N]", SkipTime30Min));

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
