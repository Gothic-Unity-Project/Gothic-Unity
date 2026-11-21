using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Models.Achievements;
using Gothic.Core.Services;
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
    /// Short notification when an achievement is earned or upgraded (GlobalEventDispatcher.AchievementUnlocked):
    /// tier in its colour, title, description, icon. Several unlocks queue up and show one after another.
    /// One instance per game, created by PlayerScene. The GameObject is only active (and Update only runs) while a
    /// toast is shown. Flat: corner of the screen (Core prefab); VR: the Prefab Variant follows the camera.
    /// </summary>
    public class AchievementToast : MonoBehaviour
    {
        private const string _headerKey = "achievements.toast";

        [Tooltip("Seconds one achievement stays visible.")]
        [SerializeField] private float _seconds = 4f;
        [Tooltip("Coloured bar in the tier's colour.")]
        [SerializeField] private Graphic _tierStripe;
        [Tooltip("\"Achievement earned - <tier>\" line.")]
        [SerializeField] private TMP_Text _headerText;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [Tooltip("The achievement's texture, if it has one.")]
        [SerializeField] private RawImage _icon;
        [Tooltip("Shown instead of the icon when the achievement has no texture.")]
        [SerializeField] private GameObject _iconPlaceholder;

        [Inject] private readonly AchievementService _achievementService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly LocalizationService _localizationService;
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly ItemIconService _itemIconService;

        private readonly Queue<(AchievementDefinition Definition, AchievementTier Tier)> _queue = new();
        private AchievementDefinition _shown;
        private float _hideAt;

        public void Init()
        {
            GlobalEventDispatcher.AchievementUnlocked.AddListener(OnAchievementUnlocked);
            GlobalEventDispatcher.ItemIconReady.AddListener(OnItemIconReady);
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.AchievementUnlocked.RemoveListener(OnAchievementUnlocked);
            GlobalEventDispatcher.ItemIconReady.RemoveListener(OnItemIconReady);
        }

        /// <summary>
        /// The shown achievement's item was drawn a frame after it was requested: swap it in.
        /// </summary>
        private void OnItemIconReady(string itemInstance)
        {
            if (gameObject.activeSelf && _shown != null &&
                string.Equals(_shown.Icon.Item, itemInstance, System.StringComparison.OrdinalIgnoreCase))
                SetIcon(_shown.Icon);
        }

        private void Update()
        {
            // Unscaled: a toast also disappears while the game is paused.
            if (Time.unscaledTime < _hideAt)
                return;

            if (_queue.Count > 0)
                ShowNext();
            else
                gameObject.SetActive(false);
        }

        private void OnAchievementUnlocked(AchievementDefinition definition, AchievementTier tier)
        {
            if (!_achievementService.IsEnabled)
                return;

            _queue.Enqueue((definition, tier));
            if (!gameObject.activeSelf)
                ShowNext();
        }

        private void ShowNext()
        {
            var (definition, tier) = _queue.Dequeue();
            _shown = definition;
            var language = _gameStateService.Language;

            _tierStripe.color = AchievementTierStyle.GetColor(tier);
            var header = _localizationService.GetText(_headerKey);
            _headerText.text = $"{header} - {AchievementTierStyle.GetColoredName(tier)}";
            _titleText.text = definition.Title.Get(language);
            _descriptionText.text = definition.Description.Get(language);
            SetIcon(definition.Icon);

            _hideAt = Time.unscaledTime + _seconds;
            gameObject.SetActive(true);

            Logger.Log($"[AchievementToast] Showing >{definition.Id}< ({tier}).", LogCat.Achievements);
        }

        /// <summary>
        /// An item not drawn yet shows its texture or the placeholder until ItemIconReady.
        /// </summary>
        private void SetIcon(AchievementIcon icon)
        {
            if (!string.IsNullOrEmpty(icon.Item) && _itemIconService.TryGetIcon(icon.Item) == null)
                _itemIconService.Request(icon.Item);

            if (!AchievementIconView.Show(_icon, _iconPlaceholder, icon.Item, icon.Texture, _itemIconService,
                    _textureCacheService))
                Logger.LogWarning($"[AchievementToast] Icon texture >{icon.Texture}< not found.", LogCat.Achievements);
        }
    }
}
