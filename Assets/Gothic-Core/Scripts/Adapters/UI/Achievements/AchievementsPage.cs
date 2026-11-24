using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Achievements;
using Gothic.Core.Services.Achievements;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Gothic.Services.UI;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.UI.Achievements
{
    /// <summary>
    /// The achievements list (VR: help panel tab, Flat: loaded the same way via PrefabType.UiAchievementsPage).
    /// Shows completion, the running game's tier, what the tiers mean and the achievements, a few per page. Pages are
    /// flipped with Gothic's up/down arrows right of the lines, like the game menus (AbstractMenu). Rebuilt when it
    /// opens (first page), on a flip and on an unlock while it's open - no per-frame work. Lines are pooled copies of
    /// the inactive template, one per line of a page. Item icons not drawn yet are requested for the shown lines only
    /// (ItemIconService, one per frame) and swapped into their line when ready.
    /// </summary>
    public class AchievementsPage : MonoBehaviour
    {
        private const string _headlineKey = "achievements.tab";
        private const string _completionKey = "achievements.completion";
        private const string _runKey = "achievements.run";
        private const string _noRunKey = "achievements.run.none";
        private const string _tierLegendKey = "achievements.tiers";
        private const string _offKey = "achievements.off";
        private const string _unavailableKey = "achievements.unavailable";

        [SerializeField] private TMP_Text _headlineText;
        [Tooltip("Completion (x / y) and the running game's tier.")]
        [SerializeField] private TMP_Text _summaryText;
        [Tooltip("What the tiers (Adanos, Beliar) mean, one line each.")]
        [SerializeField] private TMP_Text _tierLegendText;
        [Tooltip("Shown instead of the list when achievements are switched off (GameSettings.json).")]
        [SerializeField] private TMP_Text _offText;
        [Tooltip("The list area. Hidden when achievements are switched off. The page arrows sit at its right edge.")]
        [SerializeField] private GameObject _listRoot;
        [Tooltip("Parent of the lines (layout group, top aligned).")]
        [SerializeField] private RectTransform _content;
        [Tooltip("Inactive line, cloned per line of a page.")]
        [SerializeField] private AchievementsPageEntry _entryTemplate;
        [Tooltip("Lines per page. The list area has to fit them, also when a title wraps.")]
        [SerializeField] private int _linesPerPage = 4;
        [Tooltip("Width and height of a page arrow (Canvas units).")]
        [SerializeField] private float _arrowSize = 40f;
        [Tooltip("Distance of a page arrow from the list's right and top/bottom edge (Canvas units), off the frame.")]
        [SerializeField] private float _arrowMargin = 24f;

        [Inject] private readonly AchievementService _achievementService;
        [Inject] private readonly LocalizationService _localizationService;
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly ItemIconService _itemIconService;
        [Inject] private readonly TextureService _textureService;

        private readonly List<AchievementsPageEntry> _entries = new();
        // What each active line shows, to swap in an item icon once it's drawn.
        private readonly List<AchievementEntry> _shownEntries = new();
        private GameObject _pageUpArrow;
        private GameObject _pageDownArrow;
        private int _currentPage;

        // Created from its prefab at runtime (VRAchievementsTab, Flat), outside a scene scope's injection pass.
        private void Awake()
        {
            this.Inject();
            _entryTemplate.gameObject.SetActive(false);
            // Before the first show: VRAchievementsTab puts all pictures of the page on top after it (in-game menu).
            _pageUpArrow = CreatePageArrow("ARROW_UP", _textureService.ArrowUpMaterial, true,
                () => ShowPage(_currentPage - 1));
            _pageDownArrow = CreatePageArrow("ARROW_DOWN", _textureService.ArrowDownMaterial, false,
                () => ShowPage(_currentPage + 1));
        }

        private void OnEnable()
        {
            // A story variable changed by a script we don't hook counts now at the latest. Before the listener: its
            // unlocks are in the one Refresh() below.
            _achievementService.RecheckNow();
            GlobalEventDispatcher.AchievementUnlocked.AddListener(OnAchievementUnlocked);
            GlobalEventDispatcher.ItemIconReady.AddListener(OnItemIconReady);
            _currentPage = 0;
            Refresh();
        }

        private void OnDisable()
        {
            GlobalEventDispatcher.AchievementUnlocked.RemoveListener(OnAchievementUnlocked);
            GlobalEventDispatcher.ItemIconReady.RemoveListener(OnItemIconReady);
        }

        /// <summary>
        /// Only the lines with this item get their icon, no rebuild.
        /// </summary>
        private void OnItemIconReady(string itemInstance)
        {
            for (var i = 0; i < _shownEntries.Count; i++)
            {
                if (string.Equals(_shownEntries[i].IconItem, itemInstance, System.StringComparison.OrdinalIgnoreCase))
                    ShowIcon(_entries[i], _shownEntries[i]);
            }
        }

        private void OnAchievementUnlocked(AchievementDefinition definition, AchievementTier tier)
        {
            Refresh();
        }

        private void ShowPage(int page)
        {
            _currentPage = page;
            Refresh();
        }

        private void Refresh()
        {
            _headlineText.text = _localizationService.GetText(_headlineKey);
            _shownEntries.Clear();

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
                AchievementTierStyle.GetColoredName(AchievementTier.Adanos),
                AchievementTierStyle.GetColoredName(AchievementTier.Beliar));

            var entries = _achievementService.GetEntries();
            var linesPerPage = Mathf.Max(1, _linesPerPage);
            var pageCount = Mathf.Max(1, (entries.Count + linesPerPage - 1) / linesPerPage);
            _currentPage = Mathf.Clamp(_currentPage, 0, pageCount - 1);
            _pageUpArrow.SetActive(_currentPage > 0);
            _pageDownArrow.SetActive(_currentPage < pageCount - 1);

            var first = _currentPage * linesPerPage;
            var count = Mathf.Min(linesPerPage, entries.Count - first);
            for (var i = 0; i < count; i++)
            {
                _shownEntries.Add(entries[first + i]);
                ShowEntry(GetLine(i), entries[first + i]);
            }

            for (var i = count; i < _entries.Count; i++)
                _entries[i].gameObject.SetActive(false);

            FitTitles(count);
            // Gothic's font sprites only measure right from the next frame on (a fresh page measured too wide).
            StartCoroutine(FitTitlesNextFrame(count));
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

        /// <summary>
        /// One of Gothic's page arrows (O.TGA, U.TGA, as in the game menus: AbstractMenu.CreatePageArrow) in the
        /// top/bottom right corner of the list. A UI picture, not the menus' MeshRenderer button (UiButtonTextured):
        /// the VR help panel's background is a UI picture too, and the on-top pass (VRUiOnTop) draws meshes before it.
        /// </summary>
        private GameObject CreatePageArrow(string arrowName, Material material, bool isTop, UnityAction onClick)
        {
            var go = new GameObject(arrowName, typeof(RectTransform), typeof(RawImage), typeof(Button))
            {
                layer = _listRoot.layer
            };
            var rect = (RectTransform)go.transform;
            rect.SetParent(_listRoot.transform, false);
            var corner = new Vector2(1f, isTop ? 1f : 0f);
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.sizeDelta = new Vector2(_arrowSize, _arrowSize);
            rect.anchoredPosition = new Vector2(-_arrowMargin, isTop ? -_arrowMargin : _arrowMargin);

            var image = go.GetComponent<RawImage>();
            image.texture = material.mainTexture;
            image.SetUpright(true);
            if (image.texture == null)
                Logger.LogWarning($"[AchievementsPage] No picture for {arrowName}.", LogCat.Achievements);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            return go;
        }

        private void ShowEntry(AchievementsPageEntry line, AchievementEntry entry)
        {
            string state;
            if (entry.IsUnavailable)
                state = _localizationService.GetText(_unavailableKey);
            else if (entry.IsUnlocked)
                state = AchievementTierStyle.GetColoredName(entry.Tier);
            else
                state = string.Empty;

            var isGreyed = !entry.IsUnlocked || entry.IsUnavailable;
            line.Show(entry.Title, entry.Description, state, isGreyed);
            ShowIcon(line, entry);
            line.SetProgress(entry.IsUnavailable ? 0 : entry.ProgressCurrent,
                entry.IsUnavailable ? 0 : entry.ProgressTarget);
            line.gameObject.SetActive(true);
        }

        /// <summary>
        /// An item not drawn yet shows its texture or the placeholder until ItemIconReady.
        /// </summary>
        private void ShowIcon(AchievementsPageEntry line, AchievementEntry entry)
        {
            if (entry.IconItem != null && _itemIconService.TryGetIcon(entry.IconItem) == null)
                _itemIconService.Request(entry.IconItem);

            if (!line.ShowIcon(entry.IconItem, entry.IconTexture, _itemIconService, _textureCacheService))
                Logger.LogWarning($"[AchievementsPage] Icon texture >{entry.IconTexture}< of >{entry.Id}< not found.",
                    LogCat.Achievements);
        }
    }
}
