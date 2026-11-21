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

        private readonly Queue<(AchievementDefinition Definition, AchievementTier Tier)> _queue = new();
        private float _hideAt;

        public void Init()
        {
            GlobalEventDispatcher.AchievementUnlocked.AddListener(OnAchievementUnlocked);
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.AchievementUnlocked.RemoveListener(OnAchievementUnlocked);
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
            var language = _gameStateService.Language;

            _tierStripe.color = AchievementTierStyle.GetColor(tier);
            var header = _localizationService.GetText(_headerKey);
            _headerText.text = $"{header} - {AchievementTierStyle.GetColoredName(tier)}";
            _titleText.text = definition.Title.Get(language);
            _descriptionText.text = definition.Description.Get(language);
            SetIcon(definition.Icon.Texture);

            _hideAt = Time.unscaledTime + _seconds;
            gameObject.SetActive(true);

            Logger.Log($"[AchievementToast] Showing >{definition.Id}< ({tier}).", LogCat.Achievements);
        }

        private void SetIcon(string textureName)
        {
            var texture = string.IsNullOrEmpty(textureName) ? null : _textureCacheService.TryGetTexture(textureName);
            if (!string.IsNullOrEmpty(textureName) && texture == null)
                Logger.LogWarning($"[AchievementToast] Icon texture >{textureName}< not found.", LogCat.Achievements);

            _icon.texture = texture;
            _icon.gameObject.SetActive(texture != null);
            _iconPlaceholder.SetActive(texture == null);
        }
    }
}
