using Gothic.Core.Services.Caches;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gothic.Core.Adapters.UI.Achievements
{
    /// <summary>
    /// One line of the achievements page: icon, title, description, state and progress. Filled by AchievementsPage.
    /// </summary>
    public class AchievementsPageEntry : MonoBehaviour
    {
        [SerializeField] private RawImage _icon;
        [Tooltip("Shown instead of the icon when the achievement has no texture.")]
        [SerializeField] private GameObject _iconPlaceholder;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [Tooltip("Tier of the unlock or \"unavailable\".")]
        [SerializeField] private TMP_Text _stateText;
        [Tooltip("Parent of the progress bar. Hidden when the achievement has no counter.")]
        [SerializeField] private GameObject _progressRoot;
        [Tooltip("Fill of the progress bar. Its right anchor is set to the progress.")]
        [SerializeField] private RectTransform _progressFill;
        [SerializeField] private TMP_Text _progressText;
        [Tooltip("Greys out the whole line (locked or unavailable).")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Tooltip("Alpha of a line that isn't earned.")]
        [SerializeField] private float _greyedAlpha = 0.45f;
        [Tooltip("The line's height (LayoutElement); it grows when the title wraps.")]
        [SerializeField] private LayoutElement _layoutElement;
        [Tooltip("Gap between the title's last line and the description.")]
        [SerializeField] private float _titleGap = 6f;
        [Tooltip("Space below the description for the progress bar.")]
        [SerializeField] private float _bottomSpace = 36f;

        private float _baseHeight = -1f;
        private float _baseDescriptionTop;

        public void Show(string title, string description, string state, bool isGreyed)
        {
            _titleText.text = title;
            _descriptionText.text = description;
            _stateText.text = state;
            _canvasGroup.alpha = isGreyed ? _greyedAlpha : 1f;
        }

        /// <summary>
        /// TMP wraps a long title to a second line and shrinks it if needed (auto-size, two lines high); the
        /// description starts under the title's last line and the line grows by what the title took. Needs the
        /// line's final width - AchievementsPage calls it after a layout pass.
        /// </summary>
        public void FitTitle()
        {
            var description = _descriptionText.rectTransform;
            if (_baseHeight < 0f)
            {
                _baseHeight = _layoutElement.preferredHeight;
                _baseDescriptionTop = -description.anchoredPosition.y;
            }

            _titleText.ForceMeshUpdate(true, true);
            var title = _titleText.rectTransform;
            var descriptionTop = Mathf.Max(_baseDescriptionTop,
                -title.anchoredPosition.y + _titleText.renderedHeight + _titleGap);
            description.anchoredPosition = new Vector2(description.anchoredPosition.x, -descriptionTop);

            var height = Mathf.Max(_baseHeight, descriptionTop + description.sizeDelta.y + _bottomSpace);
            _layoutElement.preferredHeight = height;
            _layoutElement.minHeight = height;
        }

        /// <returns>False: the named texture isn't in the installation.</returns>
        public bool ShowIcon(string item, string texture, ItemIconService itemIconService,
            TextureCacheService textureCacheService)
        {
            return AchievementIconView.Show(_icon, _iconPlaceholder, item, texture, itemIconService,
                textureCacheService);
        }

        /// <param name="target">0: no progress bar.</param>
        public void SetProgress(int current, int target)
        {
            _progressRoot.SetActive(target > 0);
            if (target <= 0)
                return;

            var anchorMax = _progressFill.anchorMax;
            anchorMax.x = Mathf.Clamp01((float)current / target);
            _progressFill.anchorMax = anchorMax;
            _progressText.text = $"{current} / {target}";
        }
    }
}
