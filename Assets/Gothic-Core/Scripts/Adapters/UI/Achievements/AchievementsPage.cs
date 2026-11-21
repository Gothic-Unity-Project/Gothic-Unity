using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Achievements;
using Gothic.Core.Services.Achievements;
using Gothic.Core.Services.Caches;
using Gothic.Services.UI;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.UI.Achievements
{
    /// <summary>
    /// The achievements list (VR: help panel tab, Flat: loaded the same way via PrefabType.UiAchievementsPage).
    /// Shows completion, the running game's tier, what the tiers mean and every achievement. Rebuilt when it opens
    /// and on an unlock while it's open - no per-frame work. Lines are pooled copies of the inactive template.
    /// </summary>
    public class AchievementsPage : MonoBehaviour
    {
        private const string _headlineKey = "achievements.tab";
        private const string _completionKey = "achievements.completion";
        private const string _runKey = "achievements.run";
        private const string _noRunKey = "achievements.run.none";
        private const string _tierLegendKey = "achievements.tiers";
        private const string _offKey = "achievements.off";
        private const string _unsealedKey = "achievements.unsealed";
        private const string _unavailableKey = "achievements.unavailable";
        private const string _hiddenText = "?";

        [SerializeField] private TMP_Text _headlineText;
        [Tooltip("Completion (x / y) and the running game's tier.")]
        [SerializeField] private TMP_Text _summaryText;
        [Tooltip("What the three tiers (Innos, Adanos, Beliar) mean, one line each.")]
        [SerializeField] private TMP_Text _tierLegendText;
        [Tooltip("Shown instead of the list when achievements are switched off (GameSettings.json).")]
        [SerializeField] private TMP_Text _offText;
        [Tooltip("The scrollable list. Hidden when achievements are switched off.")]
        [SerializeField] private GameObject _listRoot;
        [Tooltip("Parent of the lines (ScrollRect content with a layout group).")]
        [SerializeField] private RectTransform _content;
        [Tooltip("Inactive line, cloned per achievement.")]
        [SerializeField] private AchievementsPageEntry _entryTemplate;

        [Inject] private readonly AchievementService _achievementService;
        [Inject] private readonly LocalizationService _localizationService;
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly ItemIconService _itemIconService;

        private readonly List<AchievementsPageEntry> _entries = new();

        // Created from its prefab at runtime (VRAchievementsTab, Flat), outside a scene scope's injection pass.
        private void Awake()
        {
            this.Inject();
            _entryTemplate.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            GlobalEventDispatcher.AchievementUnlocked.AddListener(OnAchievementUnlocked);
            // A story variable changed by a script we don't hook counts now at the latest.
            _achievementService.RecheckNow();
            Refresh();
        }

        private void OnDisable()
        {
            GlobalEventDispatcher.AchievementUnlocked.RemoveListener(OnAchievementUnlocked);
        }

        private void OnAchievementUnlocked(AchievementDefinition definition, AchievementTier tier)
        {
            Refresh();
        }

        private void Refresh()
        {
            _headlineText.text = _localizationService.GetText(_headlineKey);

            var isEnabled = _achievementService.IsEnabled;
            _offText.gameObject.SetActive(!isEnabled);
            _listRoot.SetActive(isEnabled);
            _summaryText.gameObject.SetActive(isEnabled);
            _tierLegendText.gameObject.SetActive(isEnabled);
            if (!isEnabled)
            {
                _offText.text = _localizationService.GetText(_offKey);
                return;
            }

            _achievementService.GetCompletion(out var unlocked, out var total);
            var tier = _achievementService.CurrentTier;
            var run = tier.HasValue
                ? string.Format(_localizationService.GetText(_runKey), AchievementTierStyle.GetColoredName(tier.Value))
                : _localizationService.GetText(_noRunKey);
            _summaryText.text = $"{string.Format(_localizationService.GetText(_completionKey), unlocked, total)}" +
                                $"    {run}";
            _tierLegendText.text = string.Format(_localizationService.GetText(_tierLegendKey),
                AchievementTierStyle.GetColoredName(AchievementTier.Innos),
                AchievementTierStyle.GetColoredName(AchievementTier.Adanos),
                AchievementTierStyle.GetColoredName(AchievementTier.Beliar));

            var entries = _achievementService.GetEntries();
            for (var i = 0; i < entries.Count; i++)
                ShowEntry(GetLine(i), entries[i]);

            for (var i = entries.Count; i < _entries.Count; i++)
                _entries[i].gameObject.SetActive(false);

            FitTitles(entries.Count);
            // Gothic's font sprites only measure right from the next frame on (a fresh page measured too wide).
            StartCoroutine(FitTitlesNextFrame(entries.Count));
        }

        private IEnumerator FitTitlesNextFrame(int count)
        {
            yield return null;
            FitTitles(count);
        }

        /// <summary>
        /// A wrapped title pushes its description down: the lines need their width first (one layout pass), then
        /// they know their height (second pass). Only when the page is (re)built.
        /// </summary>
        private void FitTitles(int count)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            for (var i = 0; i < count; i++)
                _entries[i].FitTitle();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        private AchievementsPageEntry GetLine(int index)
        {
            if (index < _entries.Count)
                return _entries[index];

            var line = Instantiate(_entryTemplate, _content, false);
            _entries.Add(line);
            return line;
        }

        private void ShowEntry(AchievementsPageEntry line, AchievementEntry entry)
        {
            var isMasked = entry.IsHidden && !entry.IsUnlocked;
            var title = isMasked ? _hiddenText : entry.Title;
            var description = isMasked ? _hiddenText : entry.Description;

            string state;
            if (entry.IsUnavailable)
                state = _localizationService.GetText(_unavailableKey);
            else if (entry.IsUnlocked && !entry.IsSealed)
                state = _localizationService.GetText(_unsealedKey);
            else if (entry.IsUnlocked)
                state = AchievementTierStyle.GetColoredName(entry.Tier);
            else
                state = string.Empty;

            var isGreyed = !entry.IsUnlocked || !entry.IsSealed || entry.IsUnavailable;
            line.Show(title, description, state, isGreyed);
            ShowIcon(line, entry, isMasked);
            line.SetProgress(entry.IsUnavailable ? 0 : entry.ProgressCurrent,
                entry.IsUnavailable ? 0 : entry.ProgressTarget);
            line.gameObject.SetActive(true);
        }

        /// <summary>
        /// A masked (hidden, locked) line shows the placeholder.
        /// </summary>
        private void ShowIcon(AchievementsPageEntry line, AchievementEntry entry, bool isMasked)
        {
            var item = isMasked ? null : entry.IconItem;
            var texture = isMasked ? null : entry.IconTexture;
            if (!line.ShowIcon(item, texture, _itemIconService, _textureCacheService))
                Logger.LogWarning($"[AchievementsPage] Icon texture >{texture}< of >{entry.Id}< not found.",
                    LogCat.Achievements);
        }
    }
}
