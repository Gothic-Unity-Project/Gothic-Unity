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
        [Tooltip("Tier of the unlock, \"unsealed\" or \"unavailable\".")]
        [SerializeField] private TMP_Text _stateText;
        [Tooltip("Parent of the progress bar. Hidden when the achievement has no counter.")]
        [SerializeField] private GameObject _progressRoot;
        [Tooltip("Fill of the progress bar. Its right anchor is set to the progress.")]
        [SerializeField] private RectTransform _progressFill;
        [SerializeField] private TMP_Text _progressText;
        [Tooltip("Greys out the whole line (locked, unsealed or unavailable).")]
        [SerializeField] private CanvasGroup _canvasGroup;

        [Tooltip("Alpha of a line that isn't earned (or whose seal is broken).")]
        [SerializeField] private float _greyedAlpha = 0.45f;

        public void Show(string title, string description, string state, Texture icon, bool isGreyed)
        {
            _titleText.text = title;
            _descriptionText.text = description;
            _stateText.text = state;
            _icon.texture = icon;
            _icon.gameObject.SetActive(icon != null);
            _iconPlaceholder.SetActive(icon == null);
            _canvasGroup.alpha = isGreyed ? _greyedAlpha : 1f;
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
